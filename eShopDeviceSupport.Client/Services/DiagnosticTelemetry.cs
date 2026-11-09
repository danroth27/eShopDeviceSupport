using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace eShopDeviceSupport.Client.Services;

public sealed class DiagnosticTelemetry
{
    public const string SourceName = "eShop.DeviceSupport";
    private static readonly ActivitySource ActivitySource = new(SourceName);
    private readonly Counter<long> completed;
    private readonly Histogram<double> duration;

    public DiagnosticTelemetry(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(SourceName);
        completed = meter.CreateCounter<long>("diagnostics.analyses.completed", "{analysis}",
            "Completed local diagnostic analyses");
        duration = meter.CreateHistogram<double>("diagnostics.analysis.duration", "s",
            "Local diagnostic analysis duration, including worker dispatch");
    }

    public Activity? StartAnalysis(AnalysisMode mode) =>
        ActivitySource.StartActivity("Analyze diagnostic bundle")?.SetTag("analysis.execution_mode", ModeName(mode));

    public void RecordCompleted(AnalysisMode mode, double seconds)
    {
        var tag = new KeyValuePair<string, object?>("execution.mode", ModeName(mode));
        completed.Add(1, tag);
        duration.Record(seconds, tag);
    }

    private static string ModeName(AnalysisMode mode) => mode == AnalysisMode.MainThread ? "main_thread" : "web_worker";
}
