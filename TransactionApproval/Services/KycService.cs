using System.Text.Json;
using TransactionApproval.Models;

namespace TransactionApproval.Services;

public class KycService
{
    private readonly string _kycPath;

    public KycService(IConfiguration config, IWebHostEnvironment env)
    {
        var relative = config["KycPath"] ?? "../kyc";
        _kycPath = Path.GetFullPath(Path.Combine(env.ContentRootPath, relative));
    }

    public JsonDocument? LoadKyc(string id)
    {
        var path = Path.Combine(_kycPath, $"{id}.json");
        if (!File.Exists(path)) return null;
        return JsonDocument.Parse(File.ReadAllText(path));
    }

    public List<KycRecord> ListKyc()
    {
        var result = new List<KycRecord>();
        if (!Directory.Exists(_kycPath)) return result;

        foreach (var file in Directory.GetFiles(_kycPath, "*.json").OrderBy(f => f))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                var root = doc.RootElement;
                var flags = root.TryGetProperty("risk_flags", out var rf) ? rf : default;

                result.Add(new KycRecord(
                    Id:             GetStr(root, "id")              ?? Path.GetFileNameWithoutExtension(file),
                    FullName:       GetStr(root, "full_name")       ?? "",
                    Nationality:    GetStr(root, "nationality")      ?? "",
                    RiskRating:     GetStr(root, "risk_rating")      ?? "",
                    Status:         GetStr(root, "status")           ?? "",
                    NextReviewDate: GetStr(root, "next_review_date") ?? "",
                    Pep:       GetBool(flags, "pep"),
                    Sanctions: GetBool(flags, "sanctions")));
            }
            catch { }
        }
        return result;
    }

    private static string? GetStr(JsonElement el, string prop)
        => el.ValueKind != JsonValueKind.Undefined &&
           el.TryGetProperty(prop, out var v) &&
           v.ValueKind != JsonValueKind.Null
            ? v.ToString() : null;

    private static bool GetBool(JsonElement el, string prop)
        => el.ValueKind != JsonValueKind.Undefined &&
           el.TryGetProperty(prop, out var v) &&
           v.ValueKind == JsonValueKind.True;
}
