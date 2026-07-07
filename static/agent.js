/**
 * PageAgent — reads transaction data from the rendered DOM,
 * applies the same filtering as filter_data.py, and sends
 * the result to /summarize. No raw JSON is embedded in the page.
 */
class PageAgent {

  /**
   * Read all label→value pairs from a section identified by its title text.
   * Booleans are stored as "Yes"/"No" in the DOM; "—" means null/empty.
   * Checklist values may have a comment appended after an em-dash — strip it.
   */
  _readSection(title) {
    for (const section of document.querySelectorAll(".section")) {
      const titleEl = section.querySelector(".section-title");
      if (!titleEl || titleEl.textContent.trim() !== title) continue;

      const result = {};
      for (const row of section.querySelectorAll(".row")) {
        const label = row.querySelector(".label")?.textContent.trim();
        if (!label) continue;

        let raw = row.querySelector(".value")?.textContent.trim() ?? "";

        // Strip trailing comment after em-dash (e.g. "No — some comment")
        // but only when the em-dash is not the whole value
        const emDash = raw.indexOf("—");
        if (emDash > 0) raw = raw.slice(0, emDash).trim();

        result[label] = raw === "—" || raw === "" ? null : raw;
      }
      return result;
    }
    return {};
  }

  /** Convert DOM "Yes"/"No" back to boolean; anything else stays as-is or null. */
  _bool(v) {
    if (v === "Yes") return true;
    if (v === "No")  return false;
    return null;
  }

  /** Mirrors build_summary_payload() in filter_data.py using only DOM data. */
  filter() {
    const txn        = this._readSection("Transaction Information");
    const ogs        = this._readSection("Risk Category Details");
    const checklist  = this._readSection("Checklist");
    const additional = this._readSection("Additional Information");

    // Checklist — same keyword matching as Python
    const checklistResults = {};
    for (const [q, v] of Object.entries(checklist)) {
      const ans = this._bool(v);
      if      (q.includes("Sanction hit"))              checklistResults.sanction_hit           = ans;
      else if (q.includes("PEP hit"))                   checklistResults.pep_hit                = ans;
      else if (q.includes("high risk jurisdiction"))    checklistResults.high_risk_jurisdiction = ans;
      else if (q.includes("no-go Jurisdiction"))        checklistResults.no_go_jurisdiction     = ans;
      else if (q.includes("authorization"))             checklistResults.client_authorization   = ans;
      else if (q.includes("relevant evidences"))        checklistResults.evidence_attached      = ans;
      else if (q.includes("bank balance"))              checklistResults.sufficient_balance     = ans;
      else if (q.includes("Blocked"))                   checklistResults.blocked_account        = ans;
      else if (q.includes("Refer to Compliance"))       checklistResults.refer_to_compliance    = ans;
    }

    return {
      transaction: {
        amount:                    txn["Amount"],
        currency:                  txn["Currency"],
        transaction_date:          txn["Transaction date"],
        transaction_category:      txn["Transaction Category"],
        transaction_type:          txn["Transaction Types"],
        frequency:                 txn["Frequency"],
        relation_to_client_entity: txn["Relation to CE"],
        country:                   txn["Country of residence"],
        country_risk:              ogs["CountryRisk"],
        bank_jurisdiction:         txn["Bank Jurisdiction"],
        bank_jurisdiction_risk:    txn["Bank Jurisdiction_RiskText"],
        approval_status:           txn["Approval Status"],
      },
      risk_flags: {
        is_pep:                  this._bool(ogs["IsPEP"]),
        is_sanction:             this._bool(ogs["IsSanction"]),
        negative_media:          this._bool(ogs["IsNegativeMedia"]),
        law_enforcement:         this._bool(ogs["IsLawEnforcement"]),
        regulatory_enforcement:  this._bool(ogs["IsRegulatoryEnforcement"]),
      },
      checklist_results: checklistResults,
      additional_information: {
        background:       additional["TransactionBackground"],
        risks:            additional["Risks"],
        mitigants:        additional["Mitigants"],
        conclusion:       additional["Conclusion"],
        monitoring_route: additional["MonitoringRoute"],
      },
    };
  }

  /** POST the filtered payload to the given endpoint and return the parsed JSON response. */
  async send(endpoint = "/start") {
    const res = await fetch(endpoint, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(this.filter()),
    });
    return res.json();
  }
}
