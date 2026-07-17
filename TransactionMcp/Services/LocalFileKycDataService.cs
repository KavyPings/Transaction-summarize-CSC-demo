using System.Text.Json;
using TransactionMcp.Models;

namespace TransactionMcp.Services;

public class LocalFileKycDataService : IKycDataService
{
    private readonly string _kycPath;

    public LocalFileKycDataService(IConfiguration config, IWebHostEnvironment env)
    {
        var relative = config["LocalFiles:KycPath"] ?? "../kyc";
        _kycPath = Path.GetFullPath(Path.Combine(env.ContentRootPath, relative));
    }

    public Task<IEnumerable<KycSummary>> ListKycAsync()
    {
        var result = new List<KycSummary>();
        if (!Directory.Exists(_kycPath))
            return Task.FromResult<IEnumerable<KycSummary>>(result);

        foreach (var file in Directory.GetFiles(_kycPath, "*.json").OrderBy(f => f))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                var root = doc.RootElement;
                var flags = root.TryGetProperty("risk_flags", out var rf) ? rf : default;

                result.Add(new KycSummary(
                    Id:             GetStr(root, "id")             ?? Path.GetFileNameWithoutExtension(file),
                    FullName:       GetStr(root, "full_name")      ?? "",
                    Nationality:    GetStr(root, "nationality")     ?? "",
                    RiskRating:     GetStr(root, "risk_rating")     ?? "",
                    Status:         GetStr(root, "status")          ?? "",
                    NextReviewDate: GetStr(root, "next_review_date") ?? "",
                    Pep:       GetBool(flags, "pep"),
                    Sanctions: GetBool(flags, "sanctions")));
            }
            catch { }
        }
        return Task.FromResult<IEnumerable<KycSummary>>(result);
    }

    public Task<JsonDocument?> LoadKycAsync(string id)
    {
        var path = Path.Combine(_kycPath, $"{id}.json");
        if (!File.Exists(path)) return Task.FromResult<JsonDocument?>(null);
        return Task.FromResult<JsonDocument?>(JsonDocument.Parse(File.ReadAllText(path)));
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
