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

_summary_cache: dict = {}      # tx_hash -> {session_id, summary}
_session_to_summary: dict = {} # session_id -> summary  (reverse lookup for /chat)
_chat_history: dict = {}       # session_id -> [{role, content}]

CHAT_SYSTEM = (
    "You are a transaction approval advisor. "
    "Answer the user's question using only the context provided. "
    "Be concise and precise. If the answer is not in the context, say so.\n\n"
    "After your answer, on a new line write exactly 'FOLLOW_UP:' then list 2–3 short "
    "follow-up questions a compliance reviewer might want to ask next, "
    "one per line starting with '- '. Keep each question under 10 words."
)


def _load_caches() -> None:
    global _summary_cache, _session_to_summary, _chat_history
    if CACHE_FILE.exists():
        try:
            _summary_cache = json.loads(CACHE_FILE.read_text(encoding="utf-8"))
            _session_to_summary = {
                v["session_id"]: v["summary"]
                for v in _summary_cache.values()
            }
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
        _session_to_summary[session_id] = summary
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
    """Answer a question using the cached summary as grounding + conversation history +
    semantically relevant evidence chunks. The summary is sent once (prepended to the
    first message) and conversation history carries context forward on every subsequent call."""
    try:
        body = request.get_json() or {}
        question = body.get("question", "").strip()
        session_id = body.get("session_id", "").strip()
        # history_key lets callers bucket history separately (e.g. suggest_<session_id>)
        history_key = body.get("history_key") or session_id

        if not question or not session_id:
            return jsonify({"error": "Missing question or session_id"}), 400

        # Retrieve only evidence chunks — the summary covers all transaction facts
        ev_chunks = query_context(question, session_id)
        ev_context = "\n\n---\n\n".join(ev_chunks) if ev_chunks else ""

        summary = _session_to_summary.get(session_id, "")
        history = _chat_history.get(history_key, [])

        # Build the messages array.
        # Summary is included once, prepended to the first user message, so the LLM
        # always has the full transaction picture without repeating it every turn.
        messages = [{"role": "system", "content": CHAT_SYSTEM}]

        if not history:
            # First question: ground the conversation with the summary
            content = f"Transaction summary:\n{summary}"
            if ev_context:
                content += f"\n\nRelevant evidence:\n{ev_context}"
            content += f"\n\nQuestion: {question}"
            messages.append({"role": "user", "content": content})
        else:
            # Reconstruct conversation history; prepend summary only to the first user turn
            for i, msg in enumerate(history):
                role = "user" if msg["role"] == "user" else "assistant"
                content = msg["content"]
                if i == 0 and role == "user":
                    content = f"Transaction summary:\n{summary}\n\nQuestion: {content}"
                messages.append({"role": role, "content": content})
            # Current question with fresh evidence context
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

        # Split answer from LLM-suggested follow-up questions
        if "\nFOLLOW_UP:" in raw:
            answer_part, _, questions_part = raw.partition("\nFOLLOW_UP:")
            answer = answer_part.strip()
            questions = [
                line.lstrip("- ").strip()
                for line in questions_part.strip().splitlines()
                if line.strip().startswith("-")
            ]
        else:
            answer = raw.strip()
            questions = []

        _append_history(history_key, "user", question)
        _append_history(history_key, "assistant", answer)

        return jsonify({"answer": answer, "questions": questions})

    except Exception as error:  # noqa: BLE001
        return jsonify({"error": f"Could not answer: {error}"}), 500


@app.route("/history", methods=["GET"])
def history():
    """Return saved chat history for a session."""
    session_id = request.args.get("session_id", "").strip()
    if not session_id:
        return jsonify({"error": "Missing session_id"}), 400
    return jsonify({"messages": _chat_history.get(session_id, [])})


@app.route("/reset", methods=["POST"])
def reset():
    """Clear chat and suggestions history for a session."""
    body = request.get_json() or {}
    session_id = body.get("session_id", "").strip()
    if not session_id:
        return jsonify({"error": "Missing session_id"}), 400
    with _history_lock:
        _chat_history.pop(session_id, None)
        _chat_history.pop(f"suggest_{session_id}", None)
        HISTORY_FILE.write_text(json.dumps(_chat_history, indent=2), encoding="utf-8")
    return jsonify({"ok": True})


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
