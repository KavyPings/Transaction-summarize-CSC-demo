from __future__ import annotations

from typing import Any


def build_summary_payload(data: dict[str, Any]) -> dict[str, Any]:
    txn = data.get("ActualTransactionData", {})
    ogs = data.get("OGSRiskCategoryDetails", {})
    additional = data.get("AdditionalInformationData", {})

    checklist_data: dict[str, Any] = {}
    checklists = data.get("Checklists", {})

    if isinstance(checklists, dict):
        for items in checklists.values():
            if not isinstance(items, list):
                continue

            for item in items:
                if not isinstance(item, dict):
                    continue

                question = item.get("Item", "")
                answer = item.get("Ans", False)

                if "Sanction hit" in question:
                    checklist_data["sanction_hit"] = answer
                elif "PEP hit" in question:
                    checklist_data["pep_hit"] = answer
                elif "high risk jurisdiction" in question:
                    checklist_data["high_risk_jurisdiction"] = answer
                elif "no-go Jurisdiction" in question:
                    checklist_data["no_go_jurisdiction"] = answer
                elif "authorization" in question:
                    checklist_data["client_authorization"] = answer
                elif "relevant evidences" in question:
                    checklist_data["evidence_attached"] = answer
                elif "bank balance" in question:
                    checklist_data["sufficient_balance"] = answer
                elif "Blocked" in question:
                    checklist_data["blocked_account"] = answer
                elif "Refer to Compliance" in question:
                    checklist_data["refer_to_compliance"] = answer

    additional_info: dict[str, Any] = {}
    if isinstance(additional, dict) and additional:
        first_key = next(iter(additional))
        info = additional.get(first_key, {})

        if isinstance(info, dict):
            additional_info = {
                "background": info.get("TransactionBackground"),
                "risks": info.get("Risks"),
                "mitigants": info.get("Mitigants"),
                "conclusion": info.get("Conclusion"),
                "monitoring_route": info.get("MonitoringRoute"),
            }

    return {
        "transaction": {
            "amount": txn.get("Amount"),
            "currency": txn.get("Currency"),
            "transaction_date": txn.get("Transaction date"),
            "transaction_category": txn.get("Transaction Category"),
            "transaction_type": txn.get("Transaction Types"),
            "frequency": txn.get("Frequency"),
            "relation_to_client_entity": txn.get("Relation to CE"),
            "country": txn.get("Country of residence"),
            "country_risk": ogs.get("CountryRisk"),
            "bank_jurisdiction": txn.get("Bank Jurisdiction"),
            "bank_jurisdiction_risk": txn.get("Bank Jurisdiction_RiskText"),
            "risk_rating": data.get("RiskRating"),
            "approval_status": data.get("ApprovalStatus"),
        },
        "risk_flags": {
            "is_pep": ogs.get("IsPEP"),
            "is_sanction": ogs.get("IsSanction"),
            "negative_media": ogs.get("IsNegativeMedia"),
            "law_enforcement": ogs.get("IsLawEnforcement"),
            "regulatory_enforcement": ogs.get("IsRegulatoryEnforcement"),
        },
        "checklist_results": checklist_data,
        "additional_information": additional_info,
    }
