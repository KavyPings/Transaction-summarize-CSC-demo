# Transaction Approval System

An internal tool for reviewing and approving financial transactions. An ASP.NET Core web app backed by Azure OpenAI GPT-4.1 that reads structured transaction data and lets compliance reviewers interact with an AI agent directly inside the UI.

---

## What It Does

- **Transaction list** — landing page showing all pending transactions (ID, client, entity, amount, date, status, risk rating, evidence count).
- **Transaction detail** — full breakdown of a single transaction: transaction info, OGS risk flags, checklist answers, and additional information.
- **Page Agent** — a context-aware AI panel available on every page. Click **Agent** to:
  - Get an instant AI-generated summary of whatever page you are on.
  - Receive suggested questions relevant to that page's data.
  - Ask follow-up questions in a chat; the agent remembers the conversation for as long as you stay on the page.
  - Ask about a specific transaction by ID from any page (e.g. "What is the risk on ATXN000001?") — the navigation agent silently reads that transaction's file and answers without requiring you to navigate away.

---

## Architecture

```
Browser
  │  GET /                       → Index.cshtml      (transaction list)
  │  GET /transaction/{id}       → Transaction.cshtml (detail page)
  │  POST /agent/analyze         ← buildContext() reads DOM, sends structured JSON
  │  POST /agent/chat            ← askQuestion() sends question + visible chat history
  │
ASP.NET Core (Program.cs)
  │
  ├── Azure OpenAI GPT-4.1       (chat completions — summary, Q&A, follow-ups)
  ├── EvidenceExtractorService   (extracts text/images from evidence files into LLM prompt)
  └── transactions/<id>.json     (one file per transaction, gitignored)

Browser-side JS (wwwroot/js/universal-agent.js)
  ├── buildContext()       reads page DOM → structured JSON (not raw HTML)
  ├── analyzeCurrentPage() → POST /agent/analyze
  └── askQuestion()        → POST /agent/chat (+ sessionStorage nav history)
```

---

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- An **Azure OpenAI** resource with a GPT-4.1 deployment

---

## Setup

### 1. Clone

```bash
git clone <repo-url>
cd "transaction summarise"
```

### 2. Fill in credentials

Open `TransactionApproval/appsettings.Development.json` (gitignored — never committed) and fill in your Azure OpenAI details:

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

### 4. Run

```bash
cd TransactionApproval
dotnet run
```

Open `http://localhost:5000` in a browser.

---

## Project Structure

```
TransactionApproval/
├── Program.cs                        ← app startup + /agent/analyze + /agent/chat endpoints
├── appsettings.json                  ← default config (no secrets)
├── appsettings.Development.json      ← local secrets (gitignored — you fill this in)
├── Prompts/
│   └── AgentPrompts.cs               ← system prompts for analyze, transaction, and chat
├── Models/
│   ├── AgentContext.cs               ← in-memory session model
│   ├── NavUpdate.cs                  ← navigation agent response model
│   └── TransactionSummary.cs         ← list page row model
├── Services/
│   ├── AzureOpenAiService.cs         ← Azure OpenAI client
│   ├── TransactionService.cs         ← loads transactions, builds context, nav agent
│   ├── EvidenceExtractorService.cs   ← extracts XLSX / PDF / MSG / image content
│   ├── AgentContextStore.cs          ← in-memory context store (ConcurrentDictionary)
│   └── AgentService.cs               ← parses LLM responses (SUMMARY/QUESTIONS/FOLLOW_UP)
├── Pages/
│   ├── Index.cshtml + .cs            ← transaction list page
│   └── Transaction.cshtml + .cs      ← transaction detail page
└── wwwroot/
    └── js/universal-agent.js         ← browser agent (buildContext, analyzeCurrentPage, askQuestion)
```

---

## How It Works

### Page Agent flow

1. User clicks **Agent** on any page.
2. `universal-agent.js` calls `buildContext()`, which reads the page's data from the DOM into a compact structured object — **no raw HTML is sent**.
3. For transaction detail pages, the server also extracts the full contents of any attached evidence files (XLSX rows, PDF text, email body, images) and sends them to the LLM alongside the structured JSON.
4. GPT-4.1 produces a structured 5-section compliance report (summary, risk & compliance, evidence review, recommendation, justification) plus suggested questions.
5. The summary appears in the left panel; suggested questions appear as clickable pills on the right.
6. Clicking a question (or typing a custom one) sends it to `/agent/chat` along with the visible conversation history. The server prepends the full page context to the first message so the LLM always has complete transaction data regardless of where in the conversation you are.
7. A `FOLLOW_UP:` block in each LLM response surfaces follow-up questions after every answer.
8. Chat history is held client-side for the duration of the page session and cleared on navigation — only what is visible in the chat is ever sent to the LLM.

### Navigation agent

When a question mentions a transaction ID (pattern `ATXN\d+`) that is not the current page, the server:
1. Checks whether `transactions/<ID>.json` exists.
2. If it does, builds a structured context for that transaction and passes it as additional context to the LLM.
3. Returns the navigated transaction ID to the browser, which stores it in `sessionStorage['agent_nav_history']` (cleared automatically when the tab closes).

---

## Scaling: When to Consider a RAG Pipeline

The current approach sends the full evidence content directly into the LLM prompt on every `/agent/analyze` call. This works well for the typical case of 1–3 small evidence files per transaction, and requires no additional infrastructure.

If usage grows to the point where evidence files become large or numerous, a RAG (Retrieval-Augmented Generation) pipeline would help by embedding evidence chunks into a vector store and retrieving only the most relevant chunks per question, rather than sending everything every time. Consider this when:

- Evidence files regularly exceed 10+ pages each.
- Users ask questions whose answers are buried deep in evidence documents rather than in the structured transaction fields.
- Token costs from large evidence payloads become significant.
