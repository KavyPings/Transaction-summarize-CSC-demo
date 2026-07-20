using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ReviewPortal.Services;

namespace ReviewPortal.Pages;

public class KycDetailModel : PageModel, IDisposable
{
    private readonly KycService _kycSvc;
    private JsonDocument? _doc;

    public string KycId { get; private set; } = "";
    public JsonElement Root { get; private set; }

    public KycDetailModel(KycService kycSvc) => _kycSvc = kycSvc;

    public IActionResult OnGet(string kycId)
    {
        _doc = _kycSvc.LoadKyc(kycId);
        if (_doc == null) return NotFound();
        KycId = kycId;
        Root = _doc.RootElement;
        return Page();
    }

    public string Val(string prop)
    {
        if (!Root.TryGetProperty(prop, out var v)) return "—";
        return v.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => "—",
            JsonValueKind.True  => "Yes",
            JsonValueKind.False => "No",
            _ => v.ToString() is { Length: > 0 } s ? s : "—",
        };
    }

    public string Nested(string parent, string prop)
    {
        if (!Root.TryGetProperty(parent, out var p)) return "—";
        if (!p.TryGetProperty(prop, out var v)) return "—";
        return v.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => "—",
            JsonValueKind.True  => "Yes",
            JsonValueKind.False => "No",
            _ => v.ToString() is { Length: > 0 } s ? s : "—",
        };
    }

    public void Dispose() => _doc?.Dispose();
}
