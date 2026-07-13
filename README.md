# Transaction Approval System

An internal tool for reviewing and approving financial transactions. A Flask web app backed by Azure OpenAI GPT-4.1 that reads structured transaction data and lets compliance reviewers interact with an AI agent directly inside the UI.

---

## What It Does

- **Transaction list** — landing page showing all pending transactions (ID, client, entity, amount, date, status, risk rating, evidence count).
- **Transaction detail** — full breakdown of a single transaction: transaction info, OGS risk flags, checklist answers, and additional information.
- **Page Agent** — a context-aware AI panel available on every page. Click **Agent** to:
  - Get an instant AI-generated summary of whatever page you are on.
  - Receive up to 7 suggested questions relevant to that page's data.
  - Ask follow-up questions in a chat; the agent remembers the conversation within the session.
  - Ask about a specific transaction by ID from any page (e.g. "What is the risk on ATXN000001?") — the navigation agent silently reads that transaction's file and answers without requiring you to navigate away.

---

## Architecture

```
Browser
  │  GET /                    → list.html   (transaction list)
  │  GET /transaction/<id>    → transaction.html (detail page)
  │  POST /agent/analyze      ← buildContext() reads DOM, sends structured JSON
  │  POST /agent/chat         ← askQuestion() sends question + history
  │  GET  /history            ← restore chat on modal reopen
  │
Flask (server.py)
  │
  ├── Azure OpenAI GPT-4.1   (chat completions — summary, Q&A, follow-ups)
  ├── transactions/<id>.json  (one file per transaction, gitignored)
  └── chat_history.json       (persisted conversation history, gitignored)

Browser-side JS (static/universal-agent.js)
  ├── buildContext()     reads page DOM → structured JSON (not raw HTML)
  ├── analyzeCurrentPage() → POST /agent/analyze
  └── askQuestion()      → POST /agent/chat (+ sessionStorage nav history)
```

---

## Prerequisites

- Python 3.11+
- An **Azure OpenAI** resource with a GPT-4.1 deployment
- Corporate SSL certificates handled automatically via `truststore`

---

## Setup

### 1. Clone and create a virtual environment

```bash
git clone <repo-url>
cd "transaction summarise"
python -m venv .venv
.venv\Scripts\activate      # Windows
# source .venv/bin/activate  # Mac / Linux
```

### 2. Install dependencies

```bash
pip install flask openai python-dotenv truststore
```

If you plan to re-enable the RAG pipeline (see below), also install:

```bash
pip install pinecone sentence-transformers openpyxl pdfplumber
```

### 3. Create a `.env` file

Create `.env` in the project root (it is gitignored):

```env
AZURE_OPENAI_ENDPOINT=https://<your-resource>.openai.azure.com/
AZURE_OPENAI_API_KEY=<your-key>
AZURE_OPENAI_API_VERSION=2024-12-01-preview
AZURE_OPENAI_DEPLOYMENT=<your-deployment-name>

# Only needed when RAG pipeline is enabled:
# PINECONE_API_KEY=<your-pinecone-key>
# PINECONE_INDEX_NAME=transaction-rag
```

### 4. Add transaction files

Create a `transactions/` directory (gitignored) and drop in one JSON file per transaction, named by transaction ID:

```
transactions/
  ATXN000001.json
  ATXN123456.json
  ...
```

Each file must follow the existing transaction JSON schema (same format as the original `transaction.json`). The server reads every `*.json` file in this directory at startup.

### 5. Run the server

```bash
python server.py
```

Open `http://localhost:5000` in a browser.

---

## How It Works

### Page Agent flow

1. User clicks **Agent** on any page.
2. `universal-agent.js` calls `buildContext()`, which reads the page's data from the DOM into a compact structured object — **no raw HTML is sent**.
3. The context is POSTed to `/agent/analyze`. The server sends it to GPT-4.1 with `AGENT_ANALYZE_PROMPT` and parses the response for a `SUMMARY:` block and a `QUESTIONS:` list.
4. The summary appears in the left panel; suggested questions appear as clickable pills on the right.
5. Clicking a question (or typing a custom one) POSTs to `/agent/chat`. The server:
   - Prepends the full page context + summary to the first message.
   - Maintains conversation history so subsequent questions have full context.
   - Parses a `FOLLOW_UP:` block from the LLM response to surface follow-up questions.
6. Conversation history is persisted to `chat_history.json` (keyed by `agent_<context_id>`) and restored when the modal is reopened within the same server session.

### Navigation agent

When a question mentions a transaction ID (pattern `ATXN\d+`) that is not the current page, the server:
1. Checks whether `transactions/<ID>.json` exists.
2. If it does, builds a structured context for that transaction and passes it as additional context to the LLM.
3. Returns the navigated transaction ID to the browser, which stores it in `sessionStorage['agent_nav_history']` (cleared automatically when the tab closes).

Subsequent questions about the same transaction do not re-fetch; the answer is already in conversation history.

---

## RAG Pipeline

### Why it is commented out

The RAG pipeline (Pinecone + sentence-transformers) was designed to embed evidence files (PDFs, XLSXs) and retrieve relevant chunks per question. It is currently disabled for three reasons:

1. **Latency vs. benefit trade-off** — at 1–3 evidence files per transaction, the Pinecone network round-trip (~150–300 ms per query) adds more time than it saves in token reduction.
2. **Structured context is sufficient** — the page agent already extracts all transaction fields, risk flags, checklist results, and additional information from the DOM. For most questions, this structured context plus GPT-4.1 is sufficient.
3. **Operational simplicity** — removing the embedding step means no local model download (~90 MB), no Pinecone API key requirement, and faster cold starts.

### When to re-enable it

Consider re-enabling RAG when:

- Evidence files are large (10+ pages each, generating many chunks).
- Users regularly ask questions whose answers are only in the evidence documents (e.g. exact figures from a bank statement or fund flow schedule), not in the structured transaction fields.
- You are processing many transactions and the evidence content cannot fit in a single context window.

### How to re-enable

**1. Install the extra dependencies:**

```bash
pip install pinecone sentence-transformers openpyxl pdfplumber
```

**2. Add Pinecone credentials to `.env`:**

```env
PINECONE_API_KEY=<your-key>
PINECONE_INDEX_NAME=transaction-rag
```

**3. Uncomment `vector_store.py`** — remove the `#` prefix from every commented line (everything below the header comment block).

**4. Uncomment the RAG imports in `server.py`:**

```python
from filter_data import build_summary_payload
from evidence_extractor import extract_evidence_text, is_image_file
from chunker import chunk_transaction, chunk_evidence
from vector_store import upsert_chunks, query_context, _get_embedder
```

Also restore `import threading` at the top of the imports.

**5. Uncomment the ingest block in `/agent/analyze`** (the `# RAG: background Pinecone ingest` block) and the `query_context` call in `/agent/chat` (the `# RAG: evidence retrieval` block).

**6. Uncomment the startup preload in `__main__`:**

```python
_get_embedder()  # pre-loads the local sentence-transformer model
```

**7. Restart the server.** On the first `Agent` click for a transaction detail page, evidence files are embedded and upserted into Pinecone in the background. Subsequent questions on that page will retrieve the top-5 most relevant evidence chunks and include them in the LLM prompt.
