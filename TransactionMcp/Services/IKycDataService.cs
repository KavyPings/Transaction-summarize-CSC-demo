using System.Text.Json;
using TransactionMcp.Models;

namespace TransactionMcp.Services;

public interface IKycDataService
{
    Task<IEnumerable<KycSummary>> ListKycAsync();
    Task<JsonDocument?> LoadKycAsync(string id);
}
