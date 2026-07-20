using System.Text.Json;
using ReviewPortal.Models;

namespace ReviewPortal.Services;

public class TransactionService
{
    private readonly string _transactionsPath;

    public TransactionService(IConfiguration config, IWebHostEnvironment env)
    {
        var relative = config["TransactionsPath"] ?? "../transactions";
        _transactionsPath = Path.GetFullPath(
            Path.Combine(env.ContentRootPath, relative));
    }

    public List<TransactionSummary> ListTransactions()
    {
        var result = new List<TransactionSummary>();
        if (!Directory.Exists(_transactionsPath)) return result;

        foreach (var file in Directory.GetFiles(_transactionsPath, "*.json").OrderBy(f => f))
        {
            try
            {
                var json = File.ReadAllText(file);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                var txd = root.TryGetProperty("ActualTransactionData", out var t) ? t : default;

                result.Add(new TransactionSummary
                {
                    Id = GetStr(root, "TransactionId") ?? Path.GetFileNameWithoutExtension(file),
                    Client = root.TryGetProperty("Client", out var c) ? GetStr(c, "ClientName") ?? "" : "",
                    Entity = root.TryGetProperty("Entity", out var e) ? GetStr(e, "EntityName") ?? "" : "",
                    Amount = GetStr(txd, "Amount") ?? "",
                    Currency = GetStr(txd, "Currency") ?? "",
                    Date = GetStr(txd, "Transaction date") ?? "",
                    Status = GetStr(root, "ApprovalStatus") ?? "",
                    RiskRating = GetStr(root, "RiskRating") ?? "",
                    EvidenceCount = GetEvidenceCount(root),
                });
            }
            catch { }
        }
        return result;
    }

    public JsonDocument? LoadTransaction(string txnId)
    {
        var path = Path.Combine(_transactionsPath, $"{txnId}.json");
        if (!File.Exists(path)) return null;
        return JsonDocument.Parse(File.ReadAllText(path));
    }

    private static string? GetStr(JsonElement el, string prop)
        => el.ValueKind != JsonValueKind.Undefined &&
           el.TryGetProperty(prop, out var v) &&
           v.ValueKind != JsonValueKind.Null
            ? v.ToString()
            : null;

    private static int GetEvidenceCount(JsonElement root)
        => root.TryGetProperty("evidence_files", out var ef) && ef.ValueKind == JsonValueKind.Array
            ? ef.GetArrayLength() : 0;
}
