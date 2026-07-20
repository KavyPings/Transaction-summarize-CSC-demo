using System.Text.Json;
using DataMcpServer.Models;

namespace DataMcpServer.Services;

// Stub implementation. Fill in when portal KYC APIs are available.
// Activate by setting DataSource: "PortalApi" in appsettings.Development.json.
public class PortalApiKycDataService : IKycDataService
{
    public Task<IEnumerable<KycSummary>> ListKycAsync()
        => throw new NotImplementedException(
            "PortalApiKycDataService is not yet implemented. " +
            "Set DataSource: \"LocalFiles\" in appsettings.json to use local files.");

    public Task<JsonDocument?> LoadKycAsync(string id)
        => throw new NotImplementedException("PortalApiKycDataService is not yet implemented.");
}
