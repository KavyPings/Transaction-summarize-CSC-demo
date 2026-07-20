using Microsoft.AspNetCore.Mvc.RazorPages;
using ReviewPortal.Models;
using ReviewPortal.Services;

namespace ReviewPortal.Pages;

public class IndexModel : PageModel
{
    private readonly TransactionService _txnSvc;

    public List<TransactionSummary> Transactions { get; private set; } = [];

    public IndexModel(TransactionService txnSvc) => _txnSvc = txnSvc;

    public void OnGet() => Transactions = _txnSvc.ListTransactions();
}
