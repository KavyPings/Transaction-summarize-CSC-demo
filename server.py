import json
import uuid

# pyrefly: ignore [missing-import]
from flask import Flask, jsonify, render_template, request

from app import create_client
from filter_data import build_summary_payload
from prompt import SYSTEM_PROMPT
from evidence_extractor import build_user_message, extract_evidence_text, is_image_file
from chunker import chunk_transaction, chunk_evidence
from vector_store import upsert_chunks, query_context

app = Flask(__name__)

TRANSACTION_FILE = "transaction.json"

CHAT_SYSTEM = (
    "You are a transaction approval advisor. "
    "Answer the user's question using only the context provided. "
    "Be concise and precise. If the answer is not in the context, say so."
)


def load_transaction() -> dict:
    with open(TRANSACTION_FILE, "r", encoding="utf-8-sig") as f:
        return json.load(f)


@app.route("/")
def index():
    return render_template("index.html", transaction=load_transaction())


@app.route("/start", methods=["POST"])
def start():
    """Ingest transaction + evidence into Pinecone, then generate the initial summary."""
    try:
        transaction = load_transaction()

        # Page agent sends pre-filtered payload; fall back to server-side filtering
        payload = request.get_json(silent=True) or build_summary_payload(transaction)

        session_id = str(uuid.uuid4())

        # --- Chunk and ingest ---
        chunks = chunk_transaction(payload)

        evidence_files = transaction.get("evidence_files") or []
        for i, path in enumerate(evidence_files, 1):
            label = f"Evidence {i} ({__import__('pathlib').Path(path).suffix.lstrip('.').upper()})"
            if not is_image_file(path):
                try:
                    chunks.extend(chunk_evidence(extract_evidence_text(path), label))
                except Exception:
                    pass

        upsert_chunks(chunks, session_id)

        # --- Generate initial summary (same LLM call as before) ---
        user_message = build_user_message(json.dumps(payload, indent=2), evidence_files)
        chat_client, chat_deployment = create_client()
        response = chat_client.chat.completions.create(
            model=chat_deployment,
            temperature=0.2,
            messages=[
                {"role": "system", "content": SYSTEM_PROMPT},
                {"role": "user", "content": user_message},
            ],
        )
        summary = response.choices[0].message.content or ""

        return jsonify({"summary": summary, "session_id": session_id})

    except Exception as error:  # noqa: BLE001
        return jsonify({"error": f"Could not start: {error}"}), 500


@app.route("/chat", methods=["POST"])
def chat():
    """Answer a follow-up question using RAG over the ingested session data."""
    try:
        body = request.get_json() or {}
        question = body.get("question", "").strip()
        session_id = body.get("session_id", "").strip()

        if not question or not session_id:
            return jsonify({"error": "Missing question or session_id"}), 400

        context_chunks = query_context(question, session_id)
        context = "\n\n---\n\n".join(context_chunks)

        chat_client, chat_deployment = create_client()
        response = chat_client.chat.completions.create(
            model=chat_deployment,
            temperature=0.2,
            messages=[
                {"role": "system", "content": CHAT_SYSTEM},
                {"role": "user", "content": f"Context:\n{context}\n\nQuestion: {question}"},
            ],
        )
        return jsonify({"answer": response.choices[0].message.content or ""})

    except Exception as error:  # noqa: BLE001
        return jsonify({"error": f"Could not answer: {error}"}), 500


# Legacy endpoint — used by app.py CLI; no ingestion
@app.route("/summarize", methods=["POST"])
def summarize():
    try:
        transaction = load_transaction()
        payload = request.get_json(silent=True) or build_summary_payload(transaction)
        evidence_files = transaction.get("evidence_files") or []
        user_message = build_user_message(json.dumps(payload, indent=2), evidence_files)
        client, deployment = create_client()
        response = client.chat.completions.create(
            model=deployment, temperature=0.2,
            messages=[
                {"role": "system", "content": SYSTEM_PROMPT},
                {"role": "user", "content": user_message},
            ],
        )
        return jsonify({"summary": response.choices[0].message.content or ""})
    except Exception as error:  # noqa: BLE001
        return jsonify({"error": f"Could not generate summary: {error}"}), 500


if __name__ == "__main__":
    app.run(debug=True, port=5000)
