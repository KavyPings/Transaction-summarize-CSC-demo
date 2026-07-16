using System.ComponentModel;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Server;
using TransactionMcp.Services;

namespace TransactionMcp.Tools;

[McpServerToolType]
public sealed class TransactionTools(IDataService data, EvidenceExtractorService evidence)
{
    [McpServerTool, Description("List all transactions with summary fields (id, client, entity, amount, currency, date, status, riskRating, evidenceCount).")]
    public async Task<string> ListTransactions()
    {
        var transactions = await data.ListTransactionsAsync();
        return JsonSerializer.Serialize(transactions, new JsonSerializerOptions { WriteIndented = true });
    }

    [McpServerTool, Description("Get full details of a transaction including risk flags, checklist results, additional information, and all extracted evidence file contents. Use this whenever the user asks about a specific transaction.")]
    public async Task<string> GetTransaction(
        [Description("Transaction ID, e.g. ATXN001")] string id)
    {
        id = id.Trim().ToUpperInvariant();
        using var doc = await data.LoadTransactionAsync(id);
        if (doc == null)
            return $"Transaction {id} not found.";

        var sb = new StringBuilder();
        sb.AppendLine(LocalFileDataService.BuildContextJson(doc));

        var evidencePaths = await data.GetEvidencePathsAsync(id, doc);
        if (evidencePaths.Count == 0)
        {
            sb.AppendLine("\nNo evidence files attached.");
            return sb.ToString();
        }

        sb.AppendLine($"\n{evidencePaths.Count} evidence file(s):");
        for (int i = 0; i < evidencePaths.Count; i++)
        {
            var path = evidencePaths[i];
            var label = EvidenceExtractorService.Label(path, i + 1);
            sb.AppendLine($"\n--- {label} ---");
            try { sb.AppendLine(evidence.ExtractText(path)); }
            catch (Exception ex) { sb.AppendLine($"[Could not extract: {ex.Message}]"); }
        }

        return sb.ToString();
    }

    [McpServerTool, Description("Get the extracted text content of a single evidence file attached to a transaction.")]
    public async Task<string> GetEvidence(
        [Description("Transaction ID")] string transactionId,
        [Description("Zero-based index of the evidence file")] int evidenceIndex)
    {
        transactionId = transactionId.Trim().ToUpperInvariant();
        using var doc = await data.LoadTransactionAsync(transactionId);
        if (doc == null)
            return $"Transaction {transactionId} not found.";

        var paths = await data.GetEvidencePathsAsync(transactionId, doc);
        if (evidenceIndex < 0 || evidenceIndex >= paths.Count)
            return $"Evidence index {evidenceIndex} out of range (transaction has {paths.Count} file(s)).";

        var path = paths[evidenceIndex];
        var label = EvidenceExtractorService.Label(path, evidenceIndex + 1);
        try
        {
            return $"{label}:\n{evidence.ExtractText(path)}";
        }
        catch (Exception ex)
        {
            return $"{label}: [Could not extract — {ex.Message}]";
        }
    }
}
