using System.Net.Http.Json;
using eShopDeviceSupport.Diagnostics;

namespace eShopDeviceSupport.Client.Services;

public sealed class DiagnosticCatalogClient(HttpClient http)
{
    public async Task<DiagnosticCatalog> GetAsync(string productId, CancellationToken cancellationToken = default)
    {
        return await http.GetFromJsonAsync<DiagnosticCatalog>(
            $"catalog/{Uri.EscapeDataString(productId)}", cancellationToken)
            ?? throw new InvalidDataException("The diagnostic catalog response was empty.");
    }
}
