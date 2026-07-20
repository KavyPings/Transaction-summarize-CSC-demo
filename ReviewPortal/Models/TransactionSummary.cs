namespace ReviewPortal.Models;

public class TransactionSummary
{
    public string Id { get; set; } = "";
    public string Client { get; set; } = "";
    public string Entity { get; set; } = "";
    public string Amount { get; set; } = "";
    public string Currency { get; set; } = "";
    public string Date { get; set; } = "";
    public string Status { get; set; } = "";
    public string RiskRating { get; set; } = "";
    public int EvidenceCount { get; set; }
}
