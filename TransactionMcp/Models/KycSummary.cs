namespace TransactionMcp.Models;

public record KycSummary(
    string Id,
    string FullName,
    string Nationality,
    string RiskRating,
    string Status,
    string NextReviewDate,
    bool Pep,
    bool Sanctions);
