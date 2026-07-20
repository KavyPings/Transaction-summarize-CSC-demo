using System.Text.Json;
using DataMcpServer.Models;

namespace DataMcpServer.Services;

public interface IKycDataService
{
    Task<IEnumerable<KycSummary>> ListKycAsync();
    Task<JsonDocument?> LoadKycAsync(string id);
}
