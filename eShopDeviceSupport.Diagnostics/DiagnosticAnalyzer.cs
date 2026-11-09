using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;

namespace eShopDeviceSupport.Diagnostics;

public static class DiagnosticAnalyzer
{
    public const int MaxBundleBytes = 16 * 1024 * 1024;
    public const int MaxExpandedBytes = 96 * 1024 * 1024;
    public const int MaxEvents = 275_000;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static DiagnosticAnalysisResult Analyze(
        byte[] bundle, DiagnosticCatalog catalog, CancellationToken cancellationToken = default)
    {
        if (bundle.Length is 0 or > MaxBundleBytes)
        {
            return Invalid("Choose a diagnostic ZIP bundle smaller than 16 MB.");
        }

        if (catalog.Rules.Count is 0 or > 32 || catalog.Rules.Any(r =>
                string.IsNullOrWhiteSpace(r.EventCode) || r.CorrelationWindowSeconds is < 0 or > 300))
        {
            throw new InvalidDataException("The diagnostic service returned an invalid rule catalog.");
        }

        var clock = Stopwatch.StartNew();
        try
        {
            using var source = new MemoryStream(bundle, writable: false);
            using var archive = new ZipArchive(source, ZipArchiveMode.Read);
            if (archive.Entries.Count > 16)
            {
                return Invalid("The bundle contains too many event streams.");
            }

            var manifestEntry = archive.GetEntry("manifest.json");
            if (manifestEntry is null || manifestEntry.Length > 65_536)
            {
                return Invalid("The bundle needs a valid manifest.json file.");
            }

            var manifest = JsonSerializer.Deserialize<DiagnosticBundleManifest>(ReadEntry(manifestEntry), JsonOptions);
            if (manifest is null || manifest.FormatVersion != 1 || manifest.ProductId != catalog.ProductId)
            {
                return Invalid("This bundle is not a supported diagnostic export for USB-C Dock Pro.");
            }

            var events = new List<DiagnosticEvent>();
            long expandedBytes = manifestEntry.Length;
            foreach (var entry in archive.Entries)
            {
                CheckDeadline(clock, cancellationToken);
                if (entry.FullName == "manifest.json")
                {
                    continue;
                }

                if (!entry.FullName.StartsWith("events/", StringComparison.Ordinal)
                    || !entry.FullName.EndsWith(".jsonl", StringComparison.Ordinal))
                {
                    return Invalid("The bundle may contain only its manifest and exported event streams.");
                }

                expandedBytes += entry.Length;
                if (entry.Length > MaxExpandedBytes || expandedBytes > MaxExpandedBytes)
                {
                    return Invalid("The expanded diagnostic bundle exceeds the 96 MB limit.");
                }

                var bytes = ReadEntry(entry);
                var remaining = bytes.AsSpan();
                while (!remaining.IsEmpty)
                {
                    if ((events.Count & 2047) == 0)
                    {
                        CheckDeadline(clock, cancellationToken);
                    }

                    var newline = remaining.IndexOf((byte)'\n');
                    var line = newline < 0 ? remaining : remaining[..newline];
                    remaining = newline < 0 ? [] : remaining[(newline + 1)..];
                    if (line.IsEmpty)
                    {
                        continue;
                    }

                    if (line.Length > 4096 || events.Count >= MaxEvents)
                    {
                        return Invalid("The export exceeds the event size or 275,000-event limit.");
                    }

                    var item = JsonSerializer.Deserialize<DiagnosticEvent>(line, JsonOptions);
                    if (item is null || string.IsNullOrWhiteSpace(item.Code) || item.Code.Length > 64
                        || string.IsNullOrWhiteSpace(item.Component) || item.Component.Length > 64
                        || item.Message is null || item.Message.Length > 512
                        || item.Timestamp == default)
                    {
                        return Invalid("The export contains an invalid diagnostic event.");
                    }

                    events.Add(item);
                }
            }

            if (events.Count == 0)
            {
                return Invalid("The bundle contains no diagnostic events.");
            }

            // Separate subsystem streams must be merged before matching time-dependent rules.
            events.Sort((left, right) => left.Timestamp.CompareTo(right.Timestamp));
            CheckDeadline(clock, cancellationToken);
            return new(Correlate(events, catalog, clock, cancellationToken), null);
        }
        catch (JsonException)
        {
            return Invalid("The export contains invalid diagnostic JSON.");
        }
        catch (IOException)
        {
            return Invalid("The diagnostic ZIP bundle is damaged or incomplete.");
        }
        catch (InvalidDataException)
        {
            return Invalid("The file is not a valid diagnostic ZIP bundle.");
        }
        catch (TimeoutException)
        {
            return Invalid("Analysis exceeded the one-minute limit. Choose a smaller export.");
        }
    }

    private static byte[] ReadEntry(ZipArchiveEntry entry)
    {
        var bytes = new byte[checked((int)entry.Length)];
        using var stream = entry.Open();
        stream.ReadExactly(bytes);
        if (stream.ReadByte() != -1)
        {
            throw new InvalidDataException("The ZIP entry length does not match its contents.");
        }

        return bytes;
    }

    private static DiagnosticAnalysis Correlate(
        List<DiagnosticEvent> events, DiagnosticCatalog catalog, Stopwatch clock, CancellationToken token)
    {
        var rules = catalog.Rules.ToLookup(r => r.EventCode, StringComparer.Ordinal);
        var previous = new Dictionary<(string Code, string Component), DateTimeOffset>();
        var matches = catalog.Rules.ToDictionary(r => r.Id, _ => new List<DateTimeOffset>(), StringComparer.Ordinal);
        var start = events[0].Timestamp;
        var end = events[^1].Timestamp;
        var width = Math.Max(TimeSpan.TicksPerMinute, (end - start).Ticks / 60 + 1);
        var buckets = new int[60, 3];
        for (var index = 0; index < events.Count;)
        {
            if ((index & 2047) == 0)
            {
                CheckDeadline(clock, token);
            }

            var groupEnd = index + 1;
            while (groupEnd < events.Count && events[groupEnd].Timestamp == events[index].Timestamp)
            {
                groupEnd++;
            }

            // Equal timestamps establish no ordering across subsystem streams.
            for (var current = index; current < groupEnd; current++)
            {
                if ((current & 2047) == 0) CheckDeadline(clock, token);
                var item = events[current];
                foreach (var rule in rules[item.Code])
                {
                    if (rule.PrecedingEventCode is { } preceding
                        && (!previous.TryGetValue((preceding, item.Component), out var time)
                            || (item.Timestamp - time).TotalSeconds > rule.CorrelationWindowSeconds))
                    {
                        continue;
                    }

                    matches[rule.Id].Add(item.Timestamp);
                    var bucket = Math.Min(59, (int)((item.Timestamp - start).Ticks / width));
                    var category = rule.Category switch { "Display" => 0, "Power" => 1, _ => 2 };
                    buckets[bucket, category]++;
                }
            }

            for (var current = index; current < groupEnd; current++)
            {
                var item = events[current];
                previous[(item.Code, item.Component)] = item.Timestamp;
            }
            index = groupEnd;
        }

        var findings = catalog.Rules.Where(r => matches[r.Id].Count > 0)
            .Select(r => new DiagnosticFinding(r.Id, r.Category, r.Severity, r.Summary,
                matches[r.Id].Count, matches[r.Id][0], matches[r.Id][^1], r.Guidance))
            .OrderByDescending(f => f.Occurrences).ToArray();
        var points = Enumerable.Range(0, 60).Select(i =>
            new DiagnosticTimelinePoint(start.AddTicks(i * width).ToString("HH:mm"),
                buckets[i, 0], buckets[i, 1], buckets[i, 2])).ToArray();
        return new(events.Count, start, end, catalog.RuleVersion, findings, points);
    }

    private static void CheckDeadline(Stopwatch clock, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (clock.Elapsed > TimeSpan.FromMinutes(1))
        {
            throw new TimeoutException();
        }
    }

    private static DiagnosticAnalysisResult Invalid(string message) => new(null, message);
}
