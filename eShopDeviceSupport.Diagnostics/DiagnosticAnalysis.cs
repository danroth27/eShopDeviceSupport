namespace eShopDeviceSupport.Diagnostics;

public sealed record DiagnosticAnalysis(
    int EventCount, DateTimeOffset StartedAt, DateTimeOffset EndedAt, string RuleVersion,
    IReadOnlyList<DiagnosticFinding> Findings, IReadOnlyList<DiagnosticTimelinePoint> Timeline);

public sealed record DiagnosticFinding(
    string Id, string Category, string Severity, string Summary, int Occurrences,
    DateTimeOffset FirstSeen, DateTimeOffset LastSeen, string Guidance);

public sealed record DiagnosticTimelinePoint(string Time, int Display, int Power, int Usb);

public sealed record DiagnosticAnalysisResult(DiagnosticAnalysis? Analysis, string? Error);

public sealed record DiagnosticEvent(DateTimeOffset Timestamp, string Code, string Component, string Message);

public sealed record DiagnosticBundleManifest(int FormatVersion, string ProductId);
