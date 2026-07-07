import hashlib
import json
import uuid
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path
from threading import Lock

# pyrefly: ignore [missing-import]
from flask import Flask, jsonify, render_template, request

from app import create_client
from filter_data import build_summary_payload
from prompt import SYSTEM_PROMPT
from evidence_extractor import build_user_message, extract_evidence_text, is_image_file
from chunker import chunk_transaction, chunk_evidence
from vector_store import upsert_chunks, query_context, _get_embedder

app = Flask(__name__)

TRANSACTION_FILE = "transaction.json"
CACHE_FILE = Path("summary_cache.json")
HISTORY_FILE = Path("chat_history.json")

_cache_lock = Lock()
_history_lock = Lock()

_summary_cache: dict = {}  # tx_hash -> {session_id, summary}
_chat_history: dict = {}   # session_id -> [{role, content}]

CHAT_SYSTEM = (
    "You are a transaction approval advisor. "
    "Answer the user's question using only the context provided. "
    "Be concise and precise. If the answer is not in the context, say so."
)


def _load_caches() -> None:
    global _summary_cache, _chat_history
    if CACHE_FILE.exists():
        try:
            _summary_cache = json.loads(CACHE_FILE.read_text(encoding="utf-8"))
        except Exception:
            _summary_cache = {}
    if HISTORY_FILE.exists():
        try:
            _chat_history = json.loads(HISTORY_FILE.read_text(encoding="utf-8"))
        except Exception:
            _chat_history = {}


def _tx_hash(transaction: dict) -> str:
    return hashlib.md5(json.dumps(transaction, sort_keys=True).encode()).hexdigest()


def _get_cached(tx_hash: str) -> dict | None:
    return _summary_cache.get(tx_hash)


def _set_cached(tx_hash: str, session_id: str, summary: str) -> None:
    with _cache_lock:
        _summary_cache[tx_hash] = {"session_id": session_id, "summary": summary}
        CACHE_FILE.write_text(json.dumps(_summary_cache, indent=2), encoding="utf-8")


def _append_history(session_id: str, role: str, content: str) -> None:
    with _history_lock:
        if session_id not in _chat_history:
            _chat_history[session_id] = []
        _chat_history[session_id].append({"role": role, "content": content})
        HISTORY_FILE.write_text(json.dumps(_chat_history, indent=2), encoding="utf-8")


def load_transaction() -> dict:
    with open(TRANSACTION_FILE, "r", encoding="utf-8-sig") as f:
        return json.load(f)


@app.route("/")
def index():
    return render_template("index.html", transaction=load_transaction())


@app.route("/start", methods=["POST"])
def start():
    """Ingest transaction + evidence into Pinecone and generate the initial summary — in parallel.
    Returns cached summary and session immediately if the transaction hasn't changed."""
    try:
        transaction = load_transaction()
        payload = request.get_json(silent=True) or build_summary_payload(transaction)

        # Return cached result immediately if transaction hasn't changed
        tx_hash = _tx_hash(transaction)
        cached = _get_cached(tx_hash)
        if cached:
            return jsonify({
                "summary": cached["summary"],
                "session_id": cached["session_id"],
                "cached": True,
            })

        session_id = str(uuid.uuid4())

        # Build chunks
        chunks = chunk_transaction(payload)
        evidence_files = transaction.get("evidence_files") or []
        for i, path in enumerate(evidence_files, 1):
            label = f"Evidence {i} ({Path(path).suffix.lstrip('.').upper()})"
            if not is_image_file(path):
                try:
                    chunks.extend(chunk_evidence(extract_evidence_text(path), label))
                except Exception:
                    pass

        user_message = build_user_message(json.dumps(payload, indent=2), evidence_files)
        chat_client, chat_deployment = create_client()

        def do_ingest():
            upsert_chunks(chunks, session_id)

        def do_summarize():
            response = chat_client.chat.completions.create(
                model=chat_deployment,
                temperature=0.2,
                messages=[
                    {"role": "system", "content": SYSTEM_PROMPT},
                    {"role": "user", "content": user_message},
                ],
            )
            return response.choices[0].message.content or ""

        # Run ingestion and summary generation at the same time
        with ThreadPoolExecutor(max_workers=2) as pool:
            ingest_future = pool.submit(do_ingest)
            summary_future = pool.submit(do_summarize)
            summary = summary_future.result()
            ingest_future.result()

        _set_cached(tx_hash, session_id, summary)
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
        answer = response.choices[0].message.content or ""

        _append_history(session_id, "user", question)
        _append_history(session_id, "assistant", answer)

        return jsonify({"answer": answer})

    except Exception as error:  # noqa: BLE001
        return jsonify({"error": f"Could not answer: {error}"}), 500


@app.route("/history", methods=["GET"])
def history():
    """Return saved chat history for a session."""
    session_id = request.args.get("session_id", "").strip()
    if not session_id:
        return jsonify({"error": "Missing session_id"}), 400
    return jsonify({"messages": _chat_history.get(session_id, [])})


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
    print("Loading embedding model...")
    _get_embedder()
    _load_caches()
    print("Ready.")
    app.run(debug=False, port=5000)
