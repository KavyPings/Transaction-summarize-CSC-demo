namespace TransactionApproval.Models;

public class AgentContext
{
    public required string PageContextJson { get; set; }
    public required string Summary { get; set; }
    public required string PageType { get; set; }
    public string TxnId { get; set; } = "";
}
