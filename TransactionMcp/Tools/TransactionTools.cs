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

    [McpServerTool, Description("Get full details of a transaction — structured fields (amount, currency, risk flags, checklist, additional info) plus a list of attached evidence files with their index and type. Does not include evidence file contents; call GetEvidence for those.")]
    public async Task<string> GetTransaction(
        [Description("Transaction ID, e.g. ATXN001")] string id)
    {
        id = id.Trim().ToUpperInvariant();
        using var doc = await data.LoadTransactionAsync(id);
        if (doc == null)
            return $"Transaction {id} not found.";

        var sb = new StringBuilder();
        sb.AppendLine(data.BuildContextJson(doc));

        var evidenceNames = await data.GetEvidenceNamesAsync(id, doc);
        if (evidenceNames.Count == 0)
        {
            sb.AppendLine("\nNo evidence files attached.");
        }
        else
        {
            sb.AppendLine("\nEvidence files (call GetEvidence(id, index) to read one):");
            for (int i = 0; i < evidenceNames.Count; i++)
            {
                var path = evidenceNames[i];
                var name = Path.GetFileNameWithoutExtension(path);
                var ext  = Path.GetExtension(path).TrimStart('.').ToUpperInvariant();
                var type = ext switch
                {
                    "PNG" or "JPG" or "JPEG" or "BMP" or "TIFF" => $"{ext} image",
                    "XLSX" or "XLS" => $"{ext} spreadsheet",
                    _ => ext,
                };
                sb.AppendLine($"  {i}: {name} ({type})");
            }
        }

        return sb.ToString();
    }

    [McpServerTool, Description("Read the content of a single evidence file attached to a transaction. Call this when the question specifically requires evidence file contents.")]
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
