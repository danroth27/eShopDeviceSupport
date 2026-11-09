using System.Diagnostics;
using System.Text.Json;
using eShopDeviceSupport.Diagnostics;
using eShopDeviceSupport.Worker;
using Microsoft.JSInterop;

namespace eShopDeviceSupport.Client.Services;

public enum AnalysisMode { MainThread, WebWorker }

public sealed record AnalysisRun(DiagnosticAnalysisResult Outcome, double Seconds);

public sealed record WorkbenchState(
    bool IsBusy, bool WorkerReady, string Status, double? MainSeconds, double? WorkerSeconds, bool ResultsMatch,
    bool WorkerRunning = false);

public sealed class DiagnosticWorkbenchSession(IJSRuntime js, DiagnosticTelemetry telemetry) : IAsyncDisposable
{
    private const string AnalyzeMethod = "eShopDeviceSupport.Worker.WorkerMethods.Analyze";
    private const string ReadyMethod = "eShopDeviceSupport.Worker.WorkerMethods.Ready";
    private const string ModulePath = "./Pages/Home.razor.js";
    private const string PaintMethod = "afterPaint";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private WebWorkerClient? worker;
    private IJSObjectReference? module;

    public async Task InitializeAsync(CancellationToken token)
    {
        try
        {
            if (!OperatingSystem.IsBrowser())
            {
                throw new PlatformNotSupportedException("The diagnostic workbench requires a browser runtime.");
            }
            module ??= await js.InvokeAsync<IJSObjectReference>("import", token, ModulePath);
            if (!WorkerMethods.WarmUp())
            {
                throw new InvalidOperationException("The local diagnostic parser did not initialize.");
            }
            worker = await WebWorkerClient.CreateAsync(js, cancellationToken: token);
            if (!await worker.InvokeAsync<bool>(ReadyMethod, [], cancellationToken: token))
            {
                throw new InvalidOperationException("The diagnostic worker did not initialize.");
            }
        }
        catch (JSException exception)
        {
            throw new InvalidOperationException("The background .NET runtime could not be prepared.", exception);
        }
    }

    public async Task<AnalysisRun> AnalyzeAsync(
        byte[] bundle, DiagnosticCatalog catalog, AnalysisMode mode, CancellationToken token)
    {
        if (module is null)
        {
            throw new InvalidOperationException("The diagnostic workbench has not initialized.");
        }
        try
        {
            await module.InvokeVoidAsync(PaintMethod, token);
        }
        catch (JSException exception)
        {
            throw new InvalidOperationException("The diagnostic workbench could not start the analysis.", exception);
        }
        using var activity = telemetry.StartAnalysis(mode);
        var clock = Stopwatch.StartNew();
        DiagnosticAnalysisResult outcome;
        if (mode == AnalysisMode.MainThread)
        {
            outcome = DiagnosticAnalyzer.Analyze(bundle, catalog, token);
        }
        else
        {
            var activeWorker = worker ?? throw new InvalidOperationException("The diagnostic worker is not ready.");
            var json = await activeWorker.InvokeAsync<string>(AnalyzeMethod,
                [Convert.ToBase64String(bundle), JsonSerializer.Serialize(catalog, JsonOptions)],
                timeoutMs: 75_000, cancellationToken: token);
            outcome = JsonSerializer.Deserialize<DiagnosticAnalysisResult>(json, JsonOptions)
                ?? throw new InvalidDataException("The diagnostic worker returned an empty response.");
        }

        clock.Stop();
        if (outcome.Analysis is { } result)
        {
            activity?.SetTag("analysis.event_count", result.EventCount);
            telemetry.RecordCompleted(mode, clock.Elapsed.TotalSeconds);
        }
        else
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Invalid diagnostic export");
        }
        return new(outcome, clock.Elapsed.TotalSeconds);
    }

    public async Task ResetWorkerAsync(CancellationToken token)
    {
        if (worker is not null)
        {
            await worker.DisposeAsync();
            worker = null;
        }
        await InitializeAsync(token);
    }

    public async ValueTask DisposeAsync()
    {
        if (worker is not null)
        {
            await worker.DisposeAsync();
            worker = null;
        }
        if (module is not null)
        {
            await module.DisposeAsync();
            module = null;
        }
    }
}
