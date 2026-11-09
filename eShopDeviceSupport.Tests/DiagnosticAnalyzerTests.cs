using System.IO.Compression;
using System.Text;
using System.Text.Json;
using eShopDeviceSupport.Diagnostics;

namespace eShopDeviceSupport.Tests;

public class DiagnosticAnalyzerTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 8, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void MergesStreamsAndCorrelatesOnlyTheSameComponentWithinTheWindow()
    {
        var bundle = Bundle(new Dictionary<string, DiagnosticEvent[]>
        {
            ["display"] = [
                Event(3, "DISPLAY_DISCONNECT"), Event(10, "DISPLAY_DISCONNECT"),
                Event(4, "DISPLAY_DISCONNECT", "other-dock")],
            ["power"] = [Event(1, "POWER_RESET")],
            ["usb"] = [Event(2, "USB_RECONNECT")]
        });
        var result = DiagnosticAnalyzer.Analyze(bundle, DockDiagnosticCatalog.Create());
        Assert.Null(result.Error);
        var analysis = Assert.IsType<DiagnosticAnalysis>(result.Analysis);
        Assert.Equal(5, analysis.EventCount);
        Assert.Equal(3, analysis.Findings.Count);
        Assert.Equal(1, analysis.Findings.Single(f => f.Category == "Display").Occurrences);
        Assert.Equal(1, analysis.Timeline.Sum(p => p.Display));
        Assert.Equal(1, analysis.Timeline.Sum(p => p.Power));
        Assert.Equal(1, analysis.Timeline.Sum(p => p.Usb));
        Assert.DoesNotContain("private-local-message", JsonSerializer.Serialize(analysis));
        Assert.DoesNotContain("other-dock", JsonSerializer.Serialize(analysis));
    }

    [Fact]
    public void KeepsUnknownEventsButDoesNotInventFindings()
    {
        var result = DiagnosticAnalyzer.Analyze(Bundle(new() { ["display"] = [Event(1, "STATUS_SAMPLE")] }),
            DockDiagnosticCatalog.Create());
        Assert.NotNull(result.Analysis);
        Assert.Empty(result.Analysis.Findings);
        Assert.Equal(1, result.Analysis.EventCount);
    }

    [Theory]
    [InlineData("not-json", "DOCK-PRO")]
    [InlineData("", "DOCK-PRO")]
    [InlineData("{}", "DOCK-PRO")]
    [InlineData("{}", "WRONG-PRODUCT")]
    public void RejectsMalformedOrUnsupportedExportsWithoutEchoingTheirContents(string events, string product)
    {
        var result = DiagnosticAnalyzer.Analyze(RawBundle(events, product), DockDiagnosticCatalog.Create());
        Assert.Null(result.Analysis);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
        Assert.DoesNotContain("WRONG-PRODUCT", result.Error);
    }

    [Fact]
    public void RejectsCompressedAndExpandedSizeLimits()
    {
        Assert.NotNull(DiagnosticAnalyzer.Analyze(new byte[DiagnosticAnalyzer.MaxBundleBytes + 1],
            DockDiagnosticCatalog.Create()).Error);
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            Write(archive, "manifest.json", "{\"formatVersion\":1,\"productId\":\"DOCK-PRO\"}");
            using var data = archive.CreateEntry("events/oversized.jsonl").Open();
            var block = new byte[1024 * 1024];
            for (var i = 0; i < 97; i++)
            {
                data.Write(block);
            }
        }
        var result = DiagnosticAnalyzer.Analyze(output.ToArray(), DockDiagnosticCatalog.Create());
        Assert.Null(result.Analysis);
        Assert.Contains("96 MB", result.Error);
    }

    [Fact]
    public void TreatsEqualTimestampsAsUnorderedAndIncludesTheWindowBoundary()
    {
        var result = DiagnosticAnalyzer.Analyze(Bundle(new()
        {
            ["power"] = [Event(1, "POWER_RESET"), Event(9, "POWER_RESET")],
            ["display"] = [Event(1, "DISPLAY_DISCONNECT"), Event(9, "DISPLAY_DISCONNECT"),
                Event(17, "DISPLAY_DISCONNECT"), Event(18, "DISPLAY_DISCONNECT")]
        }), DockDiagnosticCatalog.Create());
        var analysis = Assert.IsType<DiagnosticAnalysis>(result.Analysis);
        Assert.Equal(2, analysis.Findings.Single(f => f.Category == "Display").Occurrences);
    }

    [Fact]
    public void RejectsOversizedEventLinesAndEventCounts()
    {
        Assert.NotNull(DiagnosticAnalyzer.Analyze(RawBundle(new string('x', 4097), "DOCK-PRO"),
            DockDiagnosticCatalog.Create()).Error);
        var line = JsonSerializer.Serialize(Event(1, "STATUS_SAMPLE"),
            new JsonSerializerOptions(JsonSerializerDefaults.Web)) + "\n";
        var result = DiagnosticAnalyzer.Analyze(
            RawBundle(string.Concat(Enumerable.Repeat(line, DiagnosticAnalyzer.MaxEvents + 1)), "DOCK-PRO"),
            DockDiagnosticCatalog.Create());
        Assert.Null(result.Analysis);
        Assert.Contains("275,000", result.Error);
    }

    [Fact]
    public void HonorsCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => DiagnosticAnalyzer.Analyze(
            RawBundle("{}", "DOCK-PRO"), DockDiagnosticCatalog.Create(), cancellation.Token));
    }

    private static DiagnosticEvent Event(int second, string code, string component = "dock") =>
        new(Start.AddSeconds(second), code, component, "private-local-message");

    private static byte[] Bundle(Dictionary<string, DiagnosticEvent[]> streams)
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            Write(archive, "manifest.json", "{\"formatVersion\":1,\"productId\":\"DOCK-PRO\"}");
            foreach (var (name, events) in streams)
            {
                Write(archive, $"events/{name}.jsonl",
                    string.Join('\n', events.Select(e => JsonSerializer.Serialize(e,
                        new JsonSerializerOptions(JsonSerializerDefaults.Web)))));
            }
        }
        return output.ToArray();
    }

    private static byte[] RawBundle(string events, string product)
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            Write(archive, "manifest.json", JsonSerializer.Serialize(new { formatVersion = 1, productId = product }));
            Write(archive, "events/display.jsonl", events);
        }
        return output.ToArray();
    }

    private static void Write(ZipArchive archive, string name, string text)
    {
        using var stream = archive.CreateEntry(name).Open();
        stream.Write(Encoding.UTF8.GetBytes(text));
    }
}
