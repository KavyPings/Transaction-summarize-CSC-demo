using Microsoft.AspNetCore.Mvc.RazorPages;
using ReviewPortal.Models;
using ReviewPortal.Services;

namespace ReviewPortal.Pages;

public class KycModel : PageModel
{
    private readonly KycService _kycSvc;
    public List<KycRecord> Records { get; private set; } = new();

    public KycModel(KycService kycSvc) => _kycSvc = kycSvc;

    public void OnGet() => Records = _kycSvc.ListKyc();
}
