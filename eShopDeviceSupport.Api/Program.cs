using eShopDeviceSupport.Diagnostics;
using Microsoft.AspNetCore.Http.HttpResults;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddProblemDetails();

var app = builder.Build();
app.UseExceptionHandler();
app.MapDefaultEndpoints();
app.MapGet("/catalog/{productId}", Results<Ok<DiagnosticCatalog>, NotFound> (string productId) =>
    productId == "DOCK-PRO" ? TypedResults.Ok(DockDiagnosticCatalog.Create()) : TypedResults.NotFound());
app.Run();
