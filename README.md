# eShop device support

A standalone Blazor WebAssembly workbench demonstrating local diagnostic processing with .NET 11, Blazor Web Workers, free partner components, and Aspire service discovery and observability.

A fictional support case investigates disconnecting displays on a USB-C dock. The browser decompresses a synthetic diagnostic ZIP, parses and correlates its events, and displays findings with Infragistics Grid Lite and Combo Box plus a Syncfusion Toolkit timeline. The same C# algorithm runs on the UI thread or in a Web Worker. The worker's benefit is responsiveness, not necessarily faster execution.

Raw logs and support notes stay in the browser. The API serves product rules; it does not receive or analyze the diagnostic export. Telemetry contains aggregate counts, execution mode, and timing, not raw diagnostic content.

## Setup and run

Use the .NET 11 SDK (tested with **11.0.100**) and the Aspire CLI Daily build **17.1.0-preview.1.26558.3**. The AppHost SDK and Blazor integration are pinned to that build. This demo currently depends on that Daily build for the official Blazor Gateway and Visual Studio debugging integration; a stable Aspire installation is not the tested configuration. The repository includes the public NuGet sources needed to restore its packages.

For debugging, use Visual Studio with .NET 11 support and the ASP.NET and web development workload, and Microsoft Edge. The original machine used Visual Studio Insiders 18.12. No containers, cloud credentials, API keys, or user-secret values are required. Trust the ASP.NET Core development certificate on each machine.

```powershell
dotnet --version
aspire --version
dotnet dev-certs https --trust
dotnet restore eShopDeviceSupport.slnx
dotnet build eShopDeviceSupport.slnx
aspire start --non-interactive
aspire wait app --non-interactive
```

Open the app link in the Aspire dashboard. To debug in Visual Studio, open `eShopDeviceSupport.slnx`, select the AppHost as the startup project, and use F5 instead of starting a second CLI instance. Open the Gateway-served app link, not a separately launched client.

## Demo flow

1. Wait for **Background runtime ready**, then download and select the bundled **synthetic demo bundle**. It contains 22,048 events covering one hour. Product rules load through the logical `diagnostics` service.
2. Select **Analyze on UI thread** and try typing a support note while processing. The browser's UI thread is busy.
3. Select **Analyze in Web Worker** and keep typing. Notes remain responsive while the same C# parser runs in the worker.
4. Show **Identical findings verified in both modes**, filter the findings by category, and inspect the timeline. Starting a new run clears current results but preserves notes and previous timings.
5. In the Aspire dashboard, inspect the catalog request trace and the app's `diagnostics.analyses.completed` and `diagnostics.analysis.duration` metrics. Compare the `execution.mode` values `main_thread` and `web_worker`; browser metrics export every five seconds.

The included ZIP is synthetic and safe to share. Do not add real customer exports to this repository.

## Projects and checks

`Client` owns the workbench and browser telemetry; `Api` supplies the catalog; `Diagnostics` contains the shared parser, correlation logic, and data contracts; `Worker` exposes that algorithm in a second browser runtime. `AppHost`, `ServiceDefaults`, and `ClientDefaults` provide orchestration, service discovery, and OpenTelemetry. `Diagnostics` is a library, not a running service.

```powershell
dotnet test eShopDeviceSupport.Tests\eShopDeviceSupport.Tests.csproj
dotnet format whitespace eShopDeviceSupport.slnx --verify-no-changes --no-restore
aspire stop --non-interactive
```

Stop your running demo before rebuilding its live outputs. Visual Studio settings, build outputs, runtime state, and local logs are excluded from Git.

## Current limitations

The computation-only worker clears `modulesAfterRuntimeReady` in `onConfigLoaded` to avoid importing page-only Ignite UI/Lit initializers. This is a scoped workaround, not a universal initializer policy: worker-compatible initializers in that phase are also excluded. See [dotnet/aspnetcore#70399](https://github.com/dotnet/aspnetcore/issues/70399).

Experimental/migration warnings remain visible. Separate partner-module import errors in headless tests and a hot-reload-settings request returning HTTP 404 remain unexplained; the partner controls rendered successfully in a normal Visual Studio/browser run. Rehearse debugging, category filtering, cancellation, and telemetry on the presentation machine before going live.
