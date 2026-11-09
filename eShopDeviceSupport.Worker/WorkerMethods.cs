using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using eShopDeviceSupport.Diagnostics;
using System.IO.Compression;
using System.Text;

namespace eShopDeviceSupport.Worker;

[SupportedOSPlatform("browser")]
public static partial class WorkerMethods
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [JSExport]
    public static string Analyze(string bundleBase64, string catalogJson)
    {
        var catalog = JsonSerializer.Deserialize<DiagnosticCatalog>(catalogJson, JsonOptions)
            ?? throw new InvalidDataException("The worker did not receive a diagnostic rule catalog.");
        var result = DiagnosticAnalyzer.Analyze(Convert.FromBase64String(bundleBase64), catalog);
        return JsonSerializer.Serialize(result, JsonOptions);
    }

    [JSExport]
    public static bool Ready() => WarmUp();

    public static bool WarmUp()
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var manifest = archive.CreateEntry("manifest.json").Open())
            {
                manifest.Write(Encoding.UTF8.GetBytes("{\"formatVersion\":1,\"productId\":\"DOCK-PRO\"}"));
            }
            using var events = archive.CreateEntry("events/warmup.jsonl").Open();
            var start = new DateTimeOffset(2026, 11, 8, 9, 0, 0, TimeSpan.Zero);
            foreach (var (code, offset) in new[] { ("POWER_RESET", 0), ("DISPLAY_DISCONNECT", 1), ("USB_RECONNECT", 2) })
            {
                events.Write(JsonSerializer.SerializeToUtf8Bytes(
                    new DiagnosticEvent(start.AddSeconds(offset), code, "dock", "Synthetic warm-up event"), JsonOptions));
                events.WriteByte((byte)'\n');
            }
        }
        return DiagnosticAnalyzer.Analyze(output.ToArray(), DockDiagnosticCatalog.Create()).Analysis is not null;
    }
}
