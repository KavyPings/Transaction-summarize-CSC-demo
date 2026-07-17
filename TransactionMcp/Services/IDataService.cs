using System.Text.Json;
using TransactionMcp.Models;

namespace TransactionMcp.Services;

public interface IDataService
{
    Task<IEnumerable<TransactionSummary>> ListTransactionsAsync();
    Task<JsonDocument?> LoadTransactionAsync(string id);
    // Returns display names for the lean evidence listing (no file I/O or downloads)
    Task<List<string>> GetEvidenceNamesAsync(string id, JsonDocument doc);
    // Returns local file paths (LocalFiles mode) or temp-downloaded paths (PortalApi mode)
    Task<List<string>> GetEvidencePathsAsync(string id, JsonDocument doc);
    // Shapes the loaded document into the JSON string sent to the LLM.
    // Each implementation owns the field mapping for its own data source.
    string BuildContextJson(JsonDocument doc);
}
