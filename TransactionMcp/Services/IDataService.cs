using System.Text.Json;
using TransactionMcp.Models;

namespace TransactionMcp.Services;

public interface IDataService
{
    Task<IEnumerable<TransactionSummary>> ListTransactionsAsync();
    Task<JsonDocument?> LoadTransactionAsync(string id);
    // Returns local file paths (LocalFiles mode) or temp-downloaded paths (PortalApi mode)
    Task<List<string>> GetEvidencePathsAsync(string id, JsonDocument doc);
}
