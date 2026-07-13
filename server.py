import json
import re
import uuid
from pathlib import Path
from threading import Lock

# pyrefly: ignore [missing-import]
from flask import Flask, jsonify, render_template, request

from app import create_client
from agent_prompt import AGENT_ANALYZE_PROMPT, AGENT_TRANSACTION_PROMPT, AGENT_CHAT_PROMPT
from evidence_extractor import build_user_message

# ── RAG pipeline (commented out) ────────────────────────────────────────────
# The RAG pipeline embeds transaction evidence (PDFs/XLSXs) into Pinecone and
# retrieves relevant chunks per question. It is disabled because at current
# scale (1–3 evidence files) the Pinecone query latency (~200 ms) outweighs
# the token savings. Re-enable when evidence files grow large or numerous.
# See README § RAG Pipeline for full instructions.
# Note: evidence_extractor.build_user_message (used above) is NOT part of RAG —
# it extracts evidence text/images directly into the LLM prompt.
#
# from filter_data import build_summary_payload
# from chunker import chunk_transaction, chunk_evidence
# from vector_store import upsert_chunks, query_context, _get_embedder
# ────────────────────────────────────────────────────────────────────────────

app = Flask(__name__)

TRANSACTION_DIR = Path("transactions")
HISTORY_FILE = Path("chat_history.json")

_history_lock = Lock()
_agent_contexts: dict = {}   # context_id -> {page_context, summary, page_type, txn_id}
_chat_history: dict = {}     # history_key -> [{role, content}]


def _load_history() -> None:
    global _chat_history
    if HISTORY_FILE.exists():
        try:
            _chat_history = json.loads(HISTORY_FILE.read_text(encoding="utf-8"))
        except Exception:
            _chat_history = {}


def _append_history(history_key: str, role: str, content: str) -> None:
    with _history_lock:
        if history_key not in _chat_history:
            _chat_history[history_key] = []
        _chat_history[history_key].append({"role": role, "content": content})
        HISTORY_FILE.write_text(json.dumps(_chat_history, indent=2), encoding="utf-8")


def list_transactions() -> list[dict]:
    txns = []
    for p in sorted(TRANSACTION_DIR.glob("*.json")):
        try:
            data = json.loads(p.read_text(encoding="utf-8-sig"))
            txd = data.get("ActualTransactionData", {})
            txns.append({
                "id": data.get("TransactionId", p.stem),
                "client": data.get("Client", {}).get("ClientName", ""),
                "entity": data.get("Entity", {}).get("EntityName", ""),
                "amount": txd.get("Amount", ""),
                "currency": txd.get("Currency", ""),
                "date": txd.get("Transaction date", ""),
                "status": data.get("ApprovalStatus", ""),
                "risk_rating": data.get("RiskRating", ""),
                "evidence_count": len(data.get("evidence_files") or []),
            })
        except Exception:
            pass
    return txns


def load_transaction(txn_id: str) -> dict:
    path = TRANSACTION_DIR / f"{txn_id}.json"
    with open(path, "r", encoding="utf-8-sig") as f:
        return json.load(f)


def _build_transaction_context(data: dict) -> dict:
    """Build structured context dict for a transaction (used by nav agent)."""
    txd = data.get("ActualTransactionData", {})
    ogs = data.get("OGSRiskCategoryDetails", {})
    checklists = data.get("Checklists", {})
    additional = data.get("AdditionalInformationData", {})

    checklist_flat = {}
    for items in checklists.values():
        if isinstance(items, list):
            for item in items:
                checklist_flat[item.get("Item", "")] = item.get("Ans")

    additional_flat: dict = {}
    if isinstance(additional, dict) and additional:
        first = next(iter(additional))
        info = additional.get(first, {})
        if isinstance(info, dict):
            additional_flat = {
                "background": info.get("TransactionBackground"),
                "risks": info.get("Risks"),
                "mitigants": info.get("Mitigants"),
                "conclusion": info.get("Conclusion"),
                "monitoring_route": info.get("MonitoringRoute"),
            }

    return {
        "pageType": "transaction_detail",
        "title": f"Transaction {data.get('TransactionId', '')}",
        "data": {
            "transaction_id": data.get("TransactionId", ""),
            "transaction": {
                "amount": txd.get("Amount"),
                "currency": txd.get("Currency"),
                "transaction_date": txd.get("Transaction date"),
                "transaction_category": txd.get("Transaction Category"),
                "transaction_type": txd.get("Transaction Types"),
                "frequency": txd.get("Frequency"),
                "relation_to_client_entity": txd.get("Relation to CE"),
                "country": txd.get("Country of residence"),
                "bank_jurisdiction": txd.get("Bank Jurisdiction"),
                "approval_status": data.get("ApprovalStatus"),
            },
            "risk_flags": {
                "is_pep": ogs.get("IsPEP"),
                "is_sanction": ogs.get("IsSanction"),
                "negative_media": ogs.get("IsNegativeMedia"),
                "law_enforcement": ogs.get("IsLawEnforcement"),
                "regulatory_enforcement": ogs.get("IsRegulatoryEnforcement"),
                "country_risk": ogs.get("CountryRisk"),
                "risk_rating": data.get("RiskRating"),
            },
            "checklist_results": checklist_flat,
            "additional_information": additional_flat,
            "evidence_count": len(data.get("evidence_files") or []),
        },
    }


def _detect_nav_target(
    question: str, current_context: dict, nav_history: list
) -> dict | None:
    """If the question mentions a transaction ID not already in context/history, load it."""
    ids_in_history = {e.get("id", "").upper() for e in nav_history}
    current_id = ((current_context.get("data") or {}).get("transaction_id") or "").upper()

    for match in re.findall(r"ATXN\d+", question, re.IGNORECASE):
        mid = match.upper()
        if mid != current_id and mid not in ids_in_history:
            path = TRANSACTION_DIR / f"{mid}.json"
            if path.exists():
                data = json.loads(path.read_text(encoding="utf-8-sig"))
                return {"id": mid, "context": _build_transaction_context(data)}
    return None


def _parse_analyze_response(raw: str) -> tuple[str, list[str]]:
    summary = ""
    questions: list[str] = []
    if "SUMMARY:" in raw:
        after = raw.split("SUMMARY:", 1)[1]
        if "QUESTIONS:" in after:
            summary_part, _, questions_part = after.partition("QUESTIONS:")
            summary = summary_part.strip()
            questions = [
                line.lstrip("- ").strip()
                for line in questions_part.strip().splitlines()
                if line.strip().startswith("-")
            ]
        else:
            summary = after.strip()
    else:
        summary = raw.strip()
    return summary, questions


@app.route("/")
def index():
    return render_template("list.html", transactions=list_transactions())


@app.route("/transaction/<txn_id>")
def transaction_detail(txn_id: str):
    try:
        transaction = load_transaction(txn_id)
    except FileNotFoundError:
        return "Transaction not found", 404
    return render_template("transaction.html", transaction=transaction, txn_id=txn_id)


@app.route("/agent/analyze", methods=["POST"])
def agent_analyze():
    """Accept structured page context → LLM summary + questions → store context."""
    try:
        body = request.get_json() or {}
        context = body.get("context", {})
        if not context:
            return jsonify({"error": "Missing context"}), 400

        page_type = context.get("pageType", "")
        txn_id = ((context.get("data") or {}).get("transaction_id") or "")
        context_id = str(uuid.uuid4())

        evidence_files: list[str] = []
        if page_type == "transaction_detail" and txn_id:
            # Use the rich compliance-report prompt for transaction pages.
            # Also enrich the context with evidence file labels — the browser only
            # knows the count, not the filenames/types.
            system_prompt = AGENT_TRANSACTION_PROMPT
            try:
                full_data = load_transaction(txn_id)
                evidence_files = full_data.get("evidence_files") or []
                context.setdefault("data", {})["evidence_labels"] = [
                    f"Evidence {i} ({Path(p).suffix.lstrip('.').upper()})"
                    for i, p in enumerate(evidence_files, 1)
                ]
            except Exception:
                pass
            user_message = build_user_message(json.dumps(context, indent=2), evidence_files)
        else:
            system_prompt = AGENT_ANALYZE_PROMPT
            user_message = f"Page data:\n{json.dumps(context, indent=2)}"

        chat_client, chat_deployment = create_client()
        response = chat_client.chat.completions.create(
            model=chat_deployment,
            temperature=0.2,
            messages=[
                {"role": "system", "content": system_prompt},
                {"role": "user", "content": user_message},
            ],
        )
        raw = response.choices[0].message.content or ""
        summary, questions = _parse_analyze_response(raw)

        _agent_contexts[context_id] = {
            "page_context": context,
            "summary": summary,
            "page_type": page_type,
            "txn_id": txn_id,
        }

        # RAG: background Pinecone ingest — commented out
        # if page_type == "transaction_detail" and txn_id:
        #     def _ingest():
        #         try:
        #             data = load_transaction(txn_id)
        #             payload = build_summary_payload(data)
        #             chunks = chunk_transaction(payload)
        #             for i, path in enumerate(data.get("evidence_files") or [], 1):
        #                 label = f"Evidence {i} ({Path(path).suffix.lstrip('.').upper()})"
        #                 if not is_image_file(path):
        #                     try:
        #                         chunks.extend(chunk_evidence(extract_evidence_text(path), label))
        #                     except Exception:
        #                         pass
        #             upsert_chunks(chunks, context_id)
        #         except Exception:
        #             pass
        #     threading.Thread(target=_ingest, daemon=True).start()

        return jsonify({"summary": summary, "questions": questions, "context_id": context_id})

    except Exception as error:  # noqa: BLE001
        return jsonify({"error": f"Could not analyze: {error}"}), 500


@app.route("/agent/chat", methods=["POST"])
def agent_chat():
    """Answer a question using agent context + history + optional nav agent."""
    try:
        body = request.get_json() or {}
        question = body.get("question", "").strip()
        context_id = body.get("context_id", "").strip()
        history_key = body.get("history_key") or context_id
        nav_history = body.get("nav_history") or []

        if not question or not context_id:
            return jsonify({"error": "Missing question or context_id"}), 400

        stored = _agent_contexts.get(context_id)
        if not stored:
            return jsonify({"error": "Session expired — please reopen the Agent panel."}), 404

        page_context = stored["page_context"]
        summary = stored["summary"]

        # Navigation agent
        nav_update = _detect_nav_target(question, page_context, nav_history)
        if nav_update:
            nav_ctx = nav_update["context"]
            effective_context = nav_ctx
            effective_summary = (
                f"[Data for transaction {nav_update['id']}]\n"
                + json.dumps(nav_ctx, indent=2)
            )
        else:
            effective_context = page_context
            effective_summary = summary

        # RAG: evidence retrieval from Pinecone — commented out
        # if page_type == "transaction_detail":
        #     ev_chunks = query_context(question, context_id)
        #     ev_context = "\n\n---\n\n".join(ev_chunks) if ev_chunks else ""
        ev_context = ""

        history = _chat_history.get(history_key, [])
        ctx_text = json.dumps(effective_context, indent=2)

        messages = [{"role": "system", "content": AGENT_CHAT_PROMPT}]

        if not history:
            content = f"Page context:\n{ctx_text}\n\nSummary:\n{effective_summary}"
            if ev_context:
                content += f"\n\nRelevant evidence:\n{ev_context}"
            content += f"\n\nQuestion: {question}"
            messages.append({"role": "user", "content": content})
        else:
            for i, msg in enumerate(history):
                role = "user" if msg["role"] == "user" else "assistant"
                content = msg["content"]
                if i == 0 and role == "user":
                    content = (
                        f"Page context:\n{ctx_text}\n\n"
                        f"Summary:\n{effective_summary}\n\n"
                        f"Question: {content}"
                    )
                messages.append({"role": role, "content": content})
            content = f"Relevant evidence:\n{ev_context}\n\n" if ev_context else ""
            content += f"Question: {question}"
            messages.append({"role": "user", "content": content})

        chat_client, chat_deployment = create_client()
        response = chat_client.chat.completions.create(
            model=chat_deployment,
            temperature=0.2,
            messages=messages,
        )
        raw = response.choices[0].message.content or ""

        if "\nFOLLOW_UP:" in raw:
            answer_part, _, questions_part = raw.partition("\nFOLLOW_UP:")
            answer = answer_part.strip()
            follow_ups = [
                line.lstrip("- ").strip()
                for line in questions_part.strip().splitlines()
                if line.strip().startswith("-")
            ]
        else:
            answer = raw.strip()
            follow_ups = []

        _append_history(history_key, "user", question)
        _append_history(history_key, "assistant", answer)

        return jsonify({"answer": answer, "questions": follow_ups, "nav_update": nav_update})

    except Exception as error:  # noqa: BLE001
        return jsonify({"error": f"Could not answer: {error}"}), 500


@app.route("/history", methods=["GET"])
def history():
    key = request.args.get("session_id", "").strip()
    if not key:
        return jsonify({"error": "Missing session_id"}), 400
    return jsonify({"messages": _chat_history.get(key, [])})


if __name__ == "__main__":
    # RAG: _get_embedder()  # pre-loads sentence-transformer model — commented out
    _load_history()
    print("Ready.")
    app.run(debug=False, port=5000)
