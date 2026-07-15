using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using TransactionApproval.Models;

namespace TransactionApproval.Services;

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
        var json = File.ReadAllText(path);
        return JsonDocument.Parse(json);
    }

    public List<string> GetEvidenceFiles(JsonDocument doc)
    {
        var root = doc.RootElement;
        if (!root.TryGetProperty("evidence_files", out var ef) || ef.ValueKind != JsonValueKind.Array)
            return [];
        return ef.EnumerateArray()
            .Select(v => v.GetString() ?? "")
            .Where(s => s.Length > 0)
            .ToList();
    }

    public NavUpdate? DetectNavTarget(string question, string pageContextJson, List<NavHistoryItem> navHistory)
    {
        // TryAdd keeps first occurrence; tolerates duplicates from stale sessionStorage
        var historyById = new Dictionary<string, NavHistoryItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in navHistory)
            historyById.TryAdd(item.Id.ToUpperInvariant(), item);

        string currentId = "";
        try
        {
            using var ctxDoc = JsonDocument.Parse(pageContextJson);
            var root = ctxDoc.RootElement;
            if (root.TryGetProperty("data", out var data) &&
                data.TryGetProperty("transaction_id", out var tid))
                currentId = tid.GetString()?.ToUpperInvariant() ?? "";
        }
        catch { }

        foreach (Match match in Regex.Matches(question, @"ATXN\d+", RegexOptions.IgnoreCase))
        {
            var mid = match.Value.ToUpperInvariant();
            if (mid == currentId) continue;

            // Already seen — reuse cached context rather than dropping it
            if (historyById.TryGetValue(mid, out var cached))
                return new NavUpdate { Id = mid, Context = cached.Context };

            // New transaction — load from file
            var path = Path.Combine(_transactionsPath, $"{mid}.json");
            if (!File.Exists(path)) continue;

            try
            {
                var json = File.ReadAllText(path);
                using var doc = JsonDocument.Parse(json);
                var ctxJson = BuildTransactionContextJson(doc);
                using var ctxDoc = JsonDocument.Parse(ctxJson);
                return new NavUpdate { Id = mid, Context = ctxDoc.RootElement.Clone() };
            }
            catch { }
        }
        return null;
    }

    public string BuildTransactionContextJson(JsonDocument data)
    {
        var root = data.RootElement;
        var txd = root.TryGetProperty("ActualTransactionData", out var t) ? t : default;
        var ogs = root.TryGetProperty("OGSRiskCategoryDetails", out var o) ? o : default;
        var checklists = root.TryGetProperty("Checklists", out var c) ? c : default;
        var additional = root.TryGetProperty("AdditionalInformationData", out var a) ? a : default;

        var checklistFlat = new Dictionary<string, object?>();
        if (checklists.ValueKind == JsonValueKind.Object)
        {
            foreach (var group in checklists.EnumerateObject())
            {
                if (group.Value.ValueKind != JsonValueKind.Array) continue;
                foreach (var item in group.Value.EnumerateArray())
                {
                    var key = item.TryGetProperty("Item", out var k) ? k.GetString() ?? "" : "";
                    var val = item.TryGetProperty("Ans", out var v) ? JsonValueToObject(v) : null;
                    checklistFlat[key] = val;
                }
            }
        }

        var additionalFlat = new Dictionary<string, string?>();
        if (additional.ValueKind == JsonValueKind.Object)
        {
            var first = additional.EnumerateObject().FirstOrDefault();
            if (first.Value.ValueKind == JsonValueKind.Object)
            {
                additionalFlat["background"] = GetStr(first.Value, "TransactionBackground");
                additionalFlat["risks"] = GetStr(first.Value, "Risks");
                additionalFlat["mitigants"] = GetStr(first.Value, "Mitigants");
                additionalFlat["conclusion"] = GetStr(first.Value, "Conclusion");
                additionalFlat["monitoring_route"] = GetStr(first.Value, "MonitoringRoute");
            }
        }

        var txnId = GetStr(root, "TransactionId") ?? "";
        var ctx = new
        {
            pageType = "transaction_detail",
            title = $"Transaction {txnId}",
            data = new
            {
                transaction_id = txnId,
                transaction = new
                {
                    amount = GetStr(txd, "Amount"),
                    currency = GetStr(txd, "Currency"),
                    transaction_date = GetStr(txd, "Transaction date"),
                    transaction_category = GetStr(txd, "Transaction Category"),
                    transaction_type = GetStr(txd, "Transaction Types"),
                    frequency = GetStr(txd, "Frequency"),
                    relation_to_client_entity = GetStr(txd, "Relation to CE"),
                    country = GetStr(txd, "Country of residence"),
                    bank_jurisdiction = GetStr(txd, "Bank Jurisdiction"),
                    approval_status = GetStr(root, "ApprovalStatus"),
                },
                risk_flags = new
                {
                    is_pep = GetBool(ogs, "IsPEP"),
                    is_sanction = GetBool(ogs, "IsSanction"),
                    negative_media = GetBool(ogs, "IsNegativeMedia"),
                    law_enforcement = GetBool(ogs, "IsLawEnforcement"),
                    regulatory_enforcement = GetBool(ogs, "IsRegulatoryEnforcement"),
                    country_risk = GetStr(ogs, "CountryRisk"),
                    risk_rating = GetStr(root, "RiskRating"),
                },
                checklist_results = checklistFlat,
                additional_information = additionalFlat,
                evidence_count = GetEvidenceCount(root),
            }
        };

        return JsonSerializer.Serialize(ctx, new JsonSerializerOptions { WriteIndented = true });
    }

    private static string? GetStr(JsonElement el, string prop)
        => el.ValueKind != JsonValueKind.Undefined &&
           el.TryGetProperty(prop, out var v) &&
           v.ValueKind != JsonValueKind.Null
            ? v.ToString()
            : null;

    private static bool? GetBool(JsonElement el, string prop)
    {
        if (el.ValueKind == JsonValueKind.Undefined || !el.TryGetProperty(prop, out var v))
            return null;
        return v.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null,
        };
    }

    private static int GetEvidenceCount(JsonElement root)
        => root.TryGetProperty("evidence_files", out var ef) && ef.ValueKind == JsonValueKind.Array
            ? ef.GetArrayLength() : 0;

    private static object? JsonValueToObject(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.True => (object?)true,
        JsonValueKind.False => false,
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        JsonValueKind.Number => v.TryGetInt64(out var i) ? i : v.GetDouble(),
        _ => v.GetString(),
    };
}

public record NavHistoryItem(string Id, System.Text.Json.JsonElement Context);
