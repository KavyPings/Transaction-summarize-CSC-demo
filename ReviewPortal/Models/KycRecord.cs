namespace ReviewPortal.Models;

public record KycRecord(
    string Id,
    string FullName,
    string Nationality,
    string RiskRating,
    string Status,
    string NextReviewDate,
    bool Pep,
    bool Sanctions);
