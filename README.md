# Transaction Approval System

An internal tool for reviewing and approving financial transactions. An ASP.NET Core web app backed by Azure OpenAI GPT-4.1 that reads structured transaction data and lets compliance reviewers interact with an AI agent directly inside the UI.

---

## What It Does

- **Transaction list** — landing page showing all pending transactions (ID, client, entity, amount, date, status, risk rating, evidence count).
- **Transaction detail** — full breakdown of a single transaction: transaction info, OGS risk flags, checklist answers, and additional information.
- **Page Agent** — a context-aware AI panel available on every page. Click **Agent** to:
  - Get an instant AI-generated compliance summary of the current transaction.
  - Receive suggested questions relevant to the transaction.
  - Ask follow-up questions in a chat; ask about any transaction by ID from any page.

---

## Architecture

```
Browser
  │  GET /                       → Index.cshtml          (transaction list)
  │  GET /transaction/{id}       → Transaction.cshtml     (detail page)
  │  POST /agent/start           → agentic loop (LLM calls tools)  → summary + questions
  │  POST /agent/chat            → agentic loop (LLM calls tools)  → answer + follow-ups
  │
ASP.NET Core — TransactionApproval/ (port 5000)
  │  McpClientService            connects to MCP server, routes tool calls
  │  AzureOpenAiService          Azure OpenAI GPT-4.1 chat completions
  │  TransactionService          loads transaction JSON for page rendering only
  │
  │  (MCP protocol — HTTP/SSE)
  │
MCP Server — TransactionMcp/ (port 5001)
  │  list_transactions           returns all transaction summaries as JSON
  │  get_transaction             returns full transaction data + extracted evidence text
  │  get_evidence                returns text of a single evidence file by index
  │
  │  LocalFileDataService        reads from transactions/*.json  (current)
  │  PortalApiDataService        calls portal REST APIs          (switch via config)
  │  EvidenceExtractorService    XLSX, PDF, MSG, images (OCR via Tesseract)
```

**Key properties:**
- LLM never sees portal credentials — MCP server holds them.
- LLM fetches data on demand via tool calls (no pre-built DOM dump).
- Browser sends only `{ question, page: {pageType, txnId}, chat_history }` — no page scraping.
- No server-side session state; chat history lives in the browser.

---

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- An **Azure OpenAI** resource with a GPT-4.1 deployment
- (Optional) Tesseract OCR language data for image evidence extraction

---

## Setup

### 1. Clone

```bash
git clone <repo-url>
cd "transaction summarise"
```

### 2. Configure web app credentials

Create `TransactionApproval/appsettings.Development.json` (gitignored — never committed):

```json
{
  "AzureOpenAI": {
    "Endpoint":   "https://<your-resource>.openai.azure.com/",
    "ApiKey":     "<your-api-key>",
    "ApiVersion": "2024-12-01-preview",
    "Deployment": "<your-deployment-name>"
  }
}
```

### 3. Add transaction files

Create a `transactions/` directory (gitignored) in the project root and drop in one JSON file per transaction named by transaction ID:

```
transactions/
  ATXN000001.json
  ATXN123456.json
  ...
```

### 4. (Optional) Add Tesseract OCR for image evidence

Download `eng.traineddata` (~4 MB) from:

```
https://github.com/tesseract-ocr/tessdata/raw/main/eng.traineddata
```

Place it at `TransactionMcp/tessdata/eng.traineddata`. Without this, image evidence files are skipped gracefully.

### 5. Run

Open two terminals:

**Terminal 1 — MCP server:**
```bash
cd TransactionMcp
dotnet run
# Listening on http://localhost:5001
```

**Terminal 2 — Web app:**
```bash
cd TransactionApproval
dotnet run
# Listening on http://localhost:5000
```

Open `http://localhost:5000` in a browser.

---

## Project Structure

```
TransactionMcp/                            ← MCP server (data + tool definitions)
├── Program.cs                             ← startup, DI, app.MapMcp("/mcp")
├── appsettings.json                       ← default config (no secrets)
├── appsettings.Development.json           ← local/portal secrets (gitignored)
├── Tools/
│   └── TransactionTools.cs                ← list_transactions, get_transaction, get_evidence
├── Services/
│   ├── IDataService.cs                    ← data access interface
│   ├── LocalFileDataService.cs            ← reads transactions/*.json
│   ├── PortalApiDataService.cs            ← calls portal REST APIs (stub — configure when ready)
│   └── EvidenceExtractorService.cs        ← XLSX / PDF / MSG / image (OCR) text extraction
└── Models/
    └── TransactionSummary.cs

TransactionApproval/                       ← Web app (UI + LLM orchestration)
├── Program.cs                             ← startup + /agent/start + /agent/chat endpoints
├── appsettings.json                       ← default config (no secrets)
├── appsettings.Development.json           ← Azure OpenAI credentials (gitignored)
├── Prompts/
│   └── AgentPrompts.cs                    ← StartPrompt, ChatPrompt
├── Models/
│   └── TransactionSummary.cs              ← list page row model
├── Services/
│   ├── AzureOpenAiService.cs              ← Azure OpenAI client wrapper
│   ├── McpClientService.cs                ← MCP client — connects, lists tools, routes calls
│   └── TransactionService.cs              ← loads JSON for page rendering (list + detail)
├── Pages/
│   ├── Index.cshtml + .cs                 ← transaction list page
│   └── Transaction.cshtml + .cs           ← transaction detail page
└── wwwroot/
    └── js/universal-agent.js              ← PageAgent: start() and ask() only
```

---

## How It Works

### Agent flow

1. User clicks **Agent** on any page.
2. Browser sends `POST /agent/start` with `{ page: { pageType, txnId } }` — no DOM content.
3. Web app runs an agentic loop: builds a system prompt, calls Azure OpenAI with MCP tools attached.
4. LLM calls `get_transaction(txnId)` — the MCP server reads the JSON file and extracts all evidence text, returning everything as a single text block.
5. LLM produces a structured compliance report (summary, risk & compliance, evidence, recommendation, justification) plus suggested questions.
6. For follow-up questions, `POST /agent/chat` sends `{ question, page, chat_history }`. LLM calls tools as needed and answers in context.
7. Chat history is held client-side for the page session only.

### Switching to Portal APIs

When your portal REST APIs are available, update `TransactionMcp/appsettings.Development.json`:

```json
{
  "DataSource": "PortalApi",
  "PortalApi": {
    "BaseUrl":          "https://yourportal.example.com/api",
    "AuthType":         "ApiKey",
    "ApiKey":           "YOUR_PORTAL_API_KEY",
    "ListEndpoint":     "/transactions",
    "GetEndpoint":      "/transactions/{id}",
    "EvidenceEndpoint": "/transactions/{id}/evidence/{index}"
  }
}
```

All other code is unchanged. Portal credentials stay in the MCP server — they are never passed to the LLM or the web app.

---

## Secrets Checklist

| File | Contains | Committed? |
|------|----------|-----------|
| `TransactionApproval/appsettings.Development.json` | Azure OpenAI key | **Never** |
| `TransactionMcp/appsettings.Development.json` | Portal API key | **Never** |
| `transactions/` directory | Transaction data | **Never** |
| `TransactionMcp/tessdata/` | OCR model (large binary) | **Never** |
