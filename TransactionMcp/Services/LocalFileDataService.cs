using System.Text.Json;
using TransactionMcp.Models;

namespace TransactionMcp.Services;

public class LocalFileDataService : IDataService
{
    private readonly string _transactionsPath;

    public LocalFileDataService(IConfiguration config, IWebHostEnvironment env)
    {
        var relative = config["LocalFiles:TransactionsPath"] ?? "../transactions";
        _transactionsPath = Path.GetFullPath(Path.Combine(env.ContentRootPath, relative));
    }

    public Task<IEnumerable<TransactionSummary>> ListTransactionsAsync()
    {
        var result = new List<TransactionSummary>();
        if (!Directory.Exists(_transactionsPath))
            return Task.FromResult<IEnumerable<TransactionSummary>>(result);

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
        return Task.FromResult<IEnumerable<TransactionSummary>>(result);
    }

    public Task<JsonDocument?> LoadTransactionAsync(string id)
    {
        var path = Path.Combine(_transactionsPath, $"{id.ToUpperInvariant()}.json");
        if (!File.Exists(path)) return Task.FromResult<JsonDocument?>(null);
        return Task.FromResult<JsonDocument?>(JsonDocument.Parse(File.ReadAllText(path)));
    }

    public Task<List<string>> GetEvidenceNamesAsync(string id, JsonDocument doc)
    {
        var root = doc.RootElement;
        if (!root.TryGetProperty("evidence_files", out var ef) || ef.ValueKind != JsonValueKind.Array)
            return Task.FromResult(new List<string>());

        var names = ef.EnumerateArray()
            .Select(v => v.GetString() ?? "")
            .Where(s => s.Length > 0)
            .Select(p => Path.GetFileName(p) ?? p)
            .ToList();

        return Task.FromResult(names);
    }

    public Task<List<string>> GetEvidencePathsAsync(string id, JsonDocument doc)
    {
        var root = doc.RootElement;
        if (!root.TryGetProperty("evidence_files", out var ef) || ef.ValueKind != JsonValueKind.Array)
            return Task.FromResult(new List<string>());

        var paths = ef.EnumerateArray()
            .Select(v => v.GetString() ?? "")
            .Where(s => s.Length > 0)
            .ToList();

        return Task.FromResult(paths);
    }

    public string BuildContextJson(JsonDocument doc)
    {
        var root = doc.RootElement;
        var txd        = root.TryGetProperty("ActualTransactionData",    out var t) ? t : default;
        var ogs        = root.TryGetProperty("OGSRiskCategoryDetails",   out var o) ? o : default;
        var checklists = root.TryGetProperty("Checklists",               out var c) ? c : default;
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
                    var val = item.TryGetProperty("Ans",  out var v) ? JsonValueToObject(v) : null;
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
                additionalFlat["background"]      = GetStr(first.Value, "TransactionBackground");
                additionalFlat["risks"]           = GetStr(first.Value, "Risks");
                additionalFlat["mitigants"]       = GetStr(first.Value, "Mitigants");
                additionalFlat["conclusion"]      = GetStr(first.Value, "Conclusion");
                additionalFlat["monitoring_route"] = GetStr(first.Value, "MonitoringRoute");
            }
        }

        var ctx = new
        {
            transaction_id = GetStr(root, "TransactionId") ?? "",
            transaction = new
            {
                amount                    = GetStr(txd, "Amount"),
                currency                  = GetStr(txd, "Currency"),
                transaction_date          = GetStr(txd, "Transaction date"),
                transaction_category      = GetStr(txd, "Transaction Category"),
                transaction_type          = GetStr(txd, "Transaction Types"),
                frequency                 = GetStr(txd, "Frequency"),
                relation_to_client_entity = GetStr(txd, "Relation to CE"),
                country                   = GetStr(txd, "Country of residence"),
                bank_jurisdiction         = GetStr(txd, "Bank Jurisdiction"),
                approval_status           = GetStr(root, "ApprovalStatus"),
            },
            risk_flags = new
            {
                pep                = GetBool(ogs, "IsPEP"),
                watchlist          = GetBool(ogs, "IsSanction"),
                adverse_media      = GetBool(ogs, "IsNegativeMedia"),
                enforcement_matter = GetBool(ogs, "IsLawEnforcement"),
                regulatory_matter  = GetBool(ogs, "IsRegulatoryEnforcement"),
                country_risk       = GetStr(ogs, "CountryRisk"),
                risk_rating        = GetStr(root, "RiskRating"),
            },
            checklist_results      = checklistFlat,
            additional_information = additionalFlat,
        };

        return JsonSerializer.Serialize(ctx, new JsonSerializerOptions { WriteIndented = true });
    }

    private static string? GetStr(JsonElement el, string prop)
        => el.ValueKind != JsonValueKind.Undefined &&
           el.TryGetProperty(prop, out var v) &&
           v.ValueKind != JsonValueKind.Null
            ? v.ToString() : null;

    private static bool? GetBool(JsonElement el, string prop)
    {
        if (el.ValueKind == JsonValueKind.Undefined || !el.TryGetProperty(prop, out var v))
            return null;
        return v.ValueKind switch
        {
            JsonValueKind.True  => true,
            JsonValueKind.False => false,
            _ => null,
        };
    }

    private static object? JsonValueToObject(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.True  => (object?)true,
        JsonValueKind.False => false,
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        JsonValueKind.Number => v.TryGetInt64(out var i) ? i : v.GetDouble(),
        _ => v.GetString(),
    };

    private static int GetEvidenceCount(JsonElement root)
        => root.TryGetProperty("evidence_files", out var ef) && ef.ValueKind == JsonValueKind.Array
            ? ef.GetArrayLength() : 0;
}
