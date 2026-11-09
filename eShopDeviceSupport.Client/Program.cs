using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using eShopDeviceSupport.Client;
using eShopDeviceSupport.Client.Services;
using Microsoft.Extensions.Hosting;
using IgniteUI.Blazor.Controls;
using Syncfusion.Blazor.Toolkit;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Configuration.AddEnvironmentVariables();
builder.AddBlazorClientServiceDefaults();
builder.Services.AddIgniteUIBlazor();
builder.Services.AddSyncfusionBlazorToolkit();
builder.Services.AddMetrics();
builder.Services.AddSingleton<DiagnosticTelemetry>();
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics.AddMeter(DiagnosticTelemetry.SourceName))
    .WithTracing(tracing => tracing.AddSource(DiagnosticTelemetry.SourceName));
builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddHttpClient<DiagnosticCatalogClient>(client =>
    client.BaseAddress = new Uri("https+http://diagnostics"));

await builder.Build().RunAsync();
