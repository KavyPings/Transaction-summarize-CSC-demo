class UniversalPageAgent {

  buildContext() {
    const pageType = document.body.dataset.pageType;
    const url = window.location.href;
    const title = document.title;

    if (pageType === 'transaction_list') {
      const transactions = [];
      document.querySelectorAll('.txn-table tbody tr').forEach(row => {
        const cells = row.querySelectorAll('td');
        if (cells.length >= 8) {
          transactions.push({
            id:        cells[0]?.textContent?.trim() || '',
            client:    cells[1]?.textContent?.trim() || '',
            entity:    cells[2]?.textContent?.trim() || '',
            amount:    cells[3]?.textContent?.trim() || '',
            currency:  cells[4]?.textContent?.trim() || '',
            date:      cells[5]?.textContent?.trim() || '',
            status:    cells[6]?.textContent?.trim() || '',
            risk:      cells[7]?.textContent?.trim() || '',
            evidences: cells[8]?.textContent?.trim() || '',
          });
        }
      });
      return { pageType, title, url, data: { transactions } };
    }

    if (pageType === 'transaction_detail') {
      const txnId        = document.body.dataset.txnId || '';
      const evidenceCount = parseInt(document.body.dataset.evidenceCount || '0');

      const txn        = this._readSection('Transaction Information');
      const ogs        = this._readSection('Risk Category Details');
      const checklist  = this._readSection('Checklist');
      const additional = this._readSection('Additional Information');

      const checklistResults = {};
      for (const [q, v] of Object.entries(checklist)) {
        const ans = this._bool(v);
        if      (q.includes('Sanction hit'))            checklistResults.sanction_hit           = ans;
        else if (q.includes('PEP hit'))                 checklistResults.pep_hit                = ans;
        else if (q.includes('high risk jurisdiction'))  checklistResults.high_risk_jurisdiction = ans;
        else if (q.includes('no-go Jurisdiction'))      checklistResults.no_go_jurisdiction     = ans;
        else if (q.includes('authorization'))           checklistResults.client_authorization   = ans;
        else if (q.includes('relevant evidences'))      checklistResults.evidence_attached      = ans;
        else if (q.includes('bank balance'))            checklistResults.sufficient_balance     = ans;
        else if (q.includes('Blocked'))                 checklistResults.blocked_account        = ans;
        else if (q.includes('Refer to Compliance'))     checklistResults.refer_to_compliance    = ans;
      }

      return {
        pageType,
        title,
        url,
        data: {
          transaction_id: txnId,
          transaction: {
            amount:                    txn['Amount'],
            currency:                  txn['Currency'],
            transaction_date:          txn['Transaction date'],
            transaction_category:      txn['Transaction Category'],
            transaction_type:          txn['Transaction Types'],
            frequency:                 txn['Frequency'],
            relation_to_client_entity: txn['Relation to CE'],
            country:                   txn['Country of residence'],
            country_risk:              ogs['CountryRisk'],
            bank_jurisdiction:         txn['Bank Jurisdiction'],
            bank_jurisdiction_risk:    txn['Bank Jurisdiction_RiskText'],
            approval_status:           txn['Approval Status'],
          },
          risk_flags: {
            is_pep:                 this._bool(ogs['IsPEP']),
            is_sanction:            this._bool(ogs['IsSanction']),
            negative_media:         this._bool(ogs['IsNegativeMedia']),
            law_enforcement:        this._bool(ogs['IsLawEnforcement']),
            regulatory_enforcement: this._bool(ogs['IsRegulatoryEnforcement']),
          },
          checklist_results: checklistResults,
          additional_information: {
            background:       additional['TransactionBackground'],
            risks:            additional['Risks'],
            mitigants:        additional['Mitigants'],
            conclusion:       additional['Conclusion'],
            monitoring_route: additional['MonitoringRoute'],
          },
          evidence_count: evidenceCount,
        },
      };
    }

    return { pageType: 'unknown', title, url, data: {} };
  }

  _readSection(title) {
    for (const section of document.querySelectorAll('.section')) {
      const titleEl = section.querySelector('.section-title');
      if (!titleEl || titleEl.textContent.trim() !== title) continue;
      const result = {};
      for (const row of section.querySelectorAll('.row')) {
        const label = row.querySelector('.label')?.textContent.trim();
        if (!label) continue;
        let raw = row.querySelector('.value')?.textContent.trim() ?? '';
        const emDash = raw.indexOf('—');
        if (emDash > 0) raw = raw.slice(0, emDash).trim();
        result[label] = (raw === '—' || raw === '') ? null : raw;
      }
      return result;
    }
    return {};
  }

  _bool(v) {
    if (v === 'Yes') return true;
    if (v === 'No')  return false;
    return null;
  }

  async analyzeCurrentPage() {
    const context = this.buildContext();
    const res = await fetch('/agent/analyze', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ context }),
    });
    return res.json();
  }

  async askQuestion(question, contextId, chatHistory) {
    const navHistory = JSON.parse(sessionStorage.getItem('agent_nav_history') || '[]');
    const res = await fetch('/agent/chat', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        question,
        context_id: contextId,
        chat_history: chatHistory,
        nav_history: navHistory,
      }),
    });
    const data = await res.json();
    if (data.nav_update) {
      navHistory.push({ id: data.nav_update.id, context: data.nav_update.context });
      sessionStorage.setItem('agent_nav_history', JSON.stringify(navHistory));
    }
    return data;
  }
}
