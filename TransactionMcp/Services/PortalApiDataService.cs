using System.Text.Json;
using TransactionMcp.Models;

namespace TransactionMcp.Services;

// Stub implementation. Fill in when portal APIs are available.
// Activate by setting DataSource: "PortalApi" in appsettings.Development.json
// and filling in the PortalApi section (BaseUrl, AuthType, ApiKey/BearerToken, endpoint paths).
public class PortalApiDataService : IDataService
{
    private readonly IConfiguration _config;
    private readonly HttpClient _http;

    public PortalApiDataService(IConfiguration config, IHttpClientFactory httpFactory)
    {
        _config = config;
        _http = httpFactory.CreateClient("portal");
    }

    public Task<IEnumerable<TransactionSummary>> ListTransactionsAsync()
        => throw new NotImplementedException(
            "PortalApiDataService is not yet implemented. " +
            "Set DataSource: \"LocalFiles\" in appsettings.json to use local files.");

    public Task<JsonDocument?> LoadTransactionAsync(string id)
        => throw new NotImplementedException("PortalApiDataService is not yet implemented.");

    // Hits a metadata-only evidence-list endpoint — no file downloads
    public Task<List<string>> GetEvidenceNamesAsync(string id, JsonDocument doc)
        => throw new NotImplementedException("PortalApiDataService is not yet implemented.");

    public Task<List<string>> GetEvidencePathsAsync(string id, JsonDocument doc)
        => throw new NotImplementedException("PortalApiDataService is not yet implemented.");

    // TODO: map the portal's JSON shape to the neutral context object the LLM receives.
    // Field names and nesting will differ from the local-file shape — implement here.
    public string BuildContextJson(JsonDocument doc)
        => throw new NotImplementedException("PortalApiDataService.BuildContextJson is not yet implemented.");
}
