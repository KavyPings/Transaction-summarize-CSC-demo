using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using DataMcpServer.Services;

namespace DataMcpServer.Tools;

[McpServerToolType]
public sealed class KycTools(IKycDataService kyc)
{
    [McpServerTool, Description("List all KYC records with summary fields (id, fullName, nationality, riskRating, status, nextReviewDate, pep, sanctions).")]
    public async Task<string> ListKyc()
    {
        var records = await kyc.ListKycAsync();
        return JsonSerializer.Serialize(records, new JsonSerializerOptions { WriteIndented = true });
    }

    [McpServerTool, Description("Get full KYC details for an individual — identity, occupation, source of funds/wealth, risk flags, screening results, status, and compliance notes.")]
    public async Task<string> GetKyc(
        [Description("KYC record ID, e.g. KYC001")] string id)
    {
        id = id.Trim().ToUpperInvariant();
        using var doc = await kyc.LoadKycAsync(id);
        if (doc == null)
            return $"KYC record {id} not found.";

        return JsonSerializer.Serialize(
            doc.RootElement,
            new JsonSerializerOptions { WriteIndented = true });
    }
}
