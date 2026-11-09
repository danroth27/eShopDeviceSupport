namespace eShopDeviceSupport.Diagnostics;

public sealed record DiagnosticCatalog(
    string ProductId, string ProductName, string RuleVersion, IReadOnlyList<DiagnosticRule> Rules);

public sealed record DiagnosticRule(
    string Id, string Category, string EventCode, string Severity, string Summary, string Guidance,
    string? PrecedingEventCode = null, int CorrelationWindowSeconds = 8);

public static class DockDiagnosticCatalog
{
    public static DiagnosticCatalog Create() => new(
        "DOCK-PRO", "USB-C Dock Pro", "2026.11",
        [
            new("display-disconnect", "Display", "DISPLAY_DISCONNECT", "Warning",
                "Display loss follows a power reset", "Use the original power adapter, then update the dock firmware.",
                "POWER_RESET"),
            new("power-reset", "Power", "POWER_RESET", "Warning",
                "Dock power reset", "Connect the original power adapter directly to the dock."),
            new("usb-reconnect", "USB", "USB_RECONNECT", "Information",
                "USB reconnect follows a power reset", "Check the dock power supply before replacing the USB cable.",
                "POWER_RESET")
        ]);
}
