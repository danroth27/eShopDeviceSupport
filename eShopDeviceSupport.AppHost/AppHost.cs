var builder = DistributedApplication.CreateBuilder(args);

var api = builder.AddProject<Projects.eShopDeviceSupport_Api>("diagnostics")
    .WithHttpHealthCheck("/health");

var client = builder.AddBlazorWasmProject<Projects.eShopDeviceSupport_Client>("app")
    .WithBlazorDebuggerBrowser("msedge")
    .WithReference(api);

builder.AddBlazorGateway("gateway")
    .WithExternalHttpEndpoints()
    .WithOtlpExporter(OtlpProtocol.HttpProtobuf)
    .WithBlazorClientApp(client, proxyTelemetry: true);

builder.Build().Run();
