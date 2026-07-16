using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using TransactionApproval.Services;

namespace TransactionApproval.Pages;

public class TransactionModel : PageModel, IDisposable
{
    private readonly TransactionService _txnSvc;
    private JsonDocument? _doc;

    public string TxnId { get; private set; } = "";
    public JsonElement Root { get; private set; }
    public JsonElement TxnData { get; private set; }
    public JsonElement OgsData { get; private set; }
    public JsonElement ChecklistsData { get; private set; }
    public JsonElement AdditionalData { get; private set; }
    public TransactionModel(TransactionService txnSvc) => _txnSvc = txnSvc;

    public IActionResult OnGet(string txnId)
    {
        _doc = _txnSvc.LoadTransaction(txnId);
        if (_doc == null) return NotFound();

        TxnId = txnId;
        Root = _doc.RootElement;
        TxnData        = Get(Root, "ActualTransactionData");
        OgsData        = Get(Root, "OGSRiskCategoryDetails");
        ChecklistsData = Get(Root, "Checklists");
        AdditionalData = Get(Root, "AdditionalInformationData");
        return Page();
    }

    public string ValueOrDash(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.Undefined or JsonValueKind.Null => "—",
        JsonValueKind.True  => "Yes",
        JsonValueKind.False => "No",
        _ => v.ToString() is { Length: > 0 } s ? s : "—",
    };

    private static JsonElement Get(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) ? v : default;

    public void Dispose() => _doc?.Dispose();
}
