# ReviewPortal

A generic AI-powered review portal. A compliance reviewer opens any record page, clicks **Agent**, and gets an instant structured analysis from GPT-4.1 — then asks follow-up questions in a chat panel. The agent fetches live data via an MCP tool server; it never reads the DOM.

The platform ships with two built-in domains (transactions and KYC records) and is designed so a new domain — loans, contracts, counterparties, anything — can be wired in without touching the agent engine.

---

## How It Works

```
Browser
  │  GET /                      → transaction list
  │  GET /transaction/{id}      → transaction detail
  │  GET /kyc                   → KYC records list
  │  GET /kyc/{id}              → KYC record detail
  │
  │  POST /agent/start          body: { resource: { resourceType, resourceId } }
  │  POST /agent/chat           body: { question, resource, chat_history }
  │
ReviewPortal  (port 5000)
  │  Reads resourceType → selects prompt + tool filter from prompts.json
  │  Calls Azure OpenAI GPT-4.1 in an agentic loop
  │  Routes LLM tool calls → DataMcpServer via MCP
  │
DataMcpServer  (port 5001  /mcp)
  │  ListTransactions   GetTransaction   GetEvidence
  │  ListKyc            GetKyc
  │
  │  LocalFiles mode   → reads transactions/*.json,  kyc/*.json
  │  PortalApi mode    → calls your internal REST APIs
```

**Design properties:**
- The LLM never sees portal credentials. They live in the MCP server only.
- Data is fetched on demand via tool calls. No pre-built page dumps are sent to the model.
- The browser sends only `{ resourceType, resourceId }` — a two-field label, not page content.
- Chat history is held entirely in the browser. There is no server-side session.
- The agent engine (`/agent/start`, `/agent/chat`) contains no domain logic. Adding a new domain requires no changes to it.

---

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- An **Azure OpenAI** resource with a **GPT-4.1** deployment
- (Optional) Tesseract OCR data file for reading image evidence

---

## Setup

### 1. Clone

```bash
git clone <repo-url>
cd "transaction summarise"
```

### 2. Configure Azure OpenAI credentials

Create `ReviewPortal/appsettings.Development.json` (gitignored — never committed):

```json
{
  "AzureOpenAI": {
    "Endpoint":   "https://<your-resource>.openai.azure.com/",
    "ApiKey":     "<your-api-key>",
    "Deployment": "<your-deployment-name>"
  }
}
```

### 3. Add data files

Create these directories in the project root (both are gitignored):

```
transactions/
  ATXN001.json
  ATXN002.json

kyc/
  KYC001.json
  KYC002.json
```

Each file is one record named by its ID. Transaction JSON is expected to follow the schema used by `LocalTransactionDataService`. KYC JSON is expected to follow the schema used by `LocalFileKycDataService`.

### 4. (Optional) Tesseract OCR for image evidence

Download `eng.traineddata` from:

```
https://github.com/tesseract-ocr/tessdata/raw/main/eng.traineddata
```

Place it at `DataMcpServer/tessdata/eng.traineddata`. Without it, image evidence files are silently skipped.

### 5. Run

Start the MCP server first, then the web app. Open two terminals:

**Terminal 1 — MCP server:**
```bash
cd DataMcpServer
dotnet run
# Listening on http://localhost:5001
```

**Terminal 2 — Web app:**
```bash
cd ReviewPortal
dotnet run
# Listening on http://localhost:5000
```

Open `http://localhost:5000`.

> **Important:** Always restart *both* servers together after a code change to `DataMcpServer`. The web app caches the MCP tool list on first connection; a stale cache causes the agent to receive no tools.

---

## Project Structure

```
DataMcpServer/                          ← MCP server: data access + tool definitions
├── Program.cs                          ← startup, DI, MapMcp("/mcp")
├── appsettings.json                    ← data source config, port 5001
├── appsettings.Development.json        ← portal credentials (gitignored)
├── Models/
│   ├── TransactionSummary.cs
│   └── KycSummary.cs
├── Services/
│   ├── ITransactionDataService.cs      ← data interface (list, load, build context JSON)
│   ├── LocalTransactionDataService.cs  ← reads transactions/*.json
│   ├── PortalApiTransactionDataService.cs ← calls portal REST API (stub — configure when ready)
│   ├── IKycDataService.cs
│   ├── LocalFileKycDataService.cs      ← reads kyc/*.json
│   ├── PortalApiKycDataService.cs      ← stub
│   └── EvidenceExtractorService.cs     ← XLSX / PDF / MSG / image (OCR) extraction
└── Tools/
    ├── TransactionTools.cs             ← ListTransactions, GetTransaction, GetEvidence
    └── KycTools.cs                     ← ListKyc, GetKyc

ReviewPortal/                           ← Web app: UI + LLM orchestration
├── Program.cs                          ← startup + /agent/start + /agent/chat
├── appsettings.json                    ← MCP URL, chat history limit, port 5000
├── appsettings.Development.json        ← Azure OpenAI credentials (gitignored)
├── prompts.json                        ← all prompts + tool filters, hot-reloadable
├── Services/
│   ├── AzureOpenAiService.cs           ← Azure OpenAI client
│   ├── McpClientService.cs             ← connects to DataMcpServer, routes tool calls
│   ├── PromptService.cs                ← resolves prompt by resourceType (hot-reload aware)
│   ├── AgentService.cs                 ← parses SUMMARY:/QUESTIONS:/FOLLOW_UP: from LLM output
│   ├── TransactionService.cs           ← loads transaction JSON for page rendering
│   └── KycService.cs                   ← loads KYC JSON for page rendering
├── Pages/
│   ├── Index.cshtml / .cs              ← transaction list
│   ├── Transaction.cshtml / .cs        ← transaction detail
│   ├── Kyc.cshtml / .cs               ← KYC records list
│   └── KycDetail.cshtml / .cs         ← KYC record detail
└── wwwroot/js/
    └── universal-agent.js              ← PageAgent: start() and ask() — no domain logic
```

---

## Configuration

### prompts.json (hot-reloadable)

All LLM prompts and tool filters live in `ReviewPortal/prompts.json`. Changes take effect on the next request — no restart needed.

```json
{
  "Prompts": {
    "Analyze": "...",
    "Chat":    "...",
    "ByResourceType": {
      "transaction":  "You are a senior transaction approval advisor...",
      "transactions": "You are a transaction portfolio analyst...",
      "kyc":          "You are a KYC compliance analyst...",
      "kyc_records":  "You are a KYC portfolio analyst..."
    }
  },
  "ToolFilter": {
    "transaction":  ["ListTransactions", "GetTransaction", "GetEvidence"],
    "transactions": ["ListTransactions"],
    "kyc":          ["GetKyc", "ListKyc"],
    "kyc_records":  ["ListKyc"]
  }
}
```

**ByResourceType** — prompt selected when the user opens the Agent panel on a page with that `data-resource-type`. Falls back to `Analyze` if the type has no entry, then to the hardcoded constant in `AgentPrompts.cs`.

**ToolFilter** — which MCP tools the LLM is allowed to call for each resource type. A list page usually gets only the list tool; a detail page gets the detail tool (and related tools like `GetEvidence`). If a resource type has no filter entry, all registered tools are available.

### appsettings.json keys

| Key | Where | Default | Purpose |
|-----|-------|---------|---------|
| `McpServer:Url` | ReviewPortal | `http://localhost:5001/mcp` | Address of the MCP server |
| `ChatHistoryLimit` | ReviewPortal | `10` | Max chat turns sent to the LLM |
| `DataSource` | DataMcpServer | `LocalFiles` | `LocalFiles` or `PortalApi` |
| `LocalFiles:TransactionsPath` | DataMcpServer | `../transactions` | Relative path to transaction JSON files |
| `LocalFiles:KycPath` | DataMcpServer | `../kyc` | Relative path to KYC JSON files |

---

## Adding a New Domain

This is the full process for adding a new category of records — for example, **loans**. Follow the same pattern for any other domain.

You will touch exactly five places: data model, data service, MCP tools, page, and config. The agent engine (`Program.cs`, `McpClientService`, `AgentService`) needs no changes.

---

### Step 1 — Data model (`DataMcpServer/Models/`)

Create a summary record used for list views:

```csharp
// DataMcpServer/Models/LoanSummary.cs
namespace DataMcpServer.Models;

public record LoanSummary(
    string Id,
    string Borrower,
    string Amount,
    string Currency,
    string Status,
    string RiskRating,
    string MaturityDate
);
```

---

### Step 2 — Data service interface (`DataMcpServer/Services/`)

```csharp
// DataMcpServer/Services/ILoanDataService.cs
using System.Text.Json;
using DataMcpServer.Models;

namespace DataMcpServer.Services;

public interface ILoanDataService
{
    Task<IEnumerable<LoanSummary>> ListLoansAsync();
    Task<JsonDocument?> LoadLoanAsync(string id);
}
```

Then implement it for local JSON files:

```csharp
// DataMcpServer/Services/LocalLoanDataService.cs
using System.Text.Json;
using DataMcpServer.Models;

namespace DataMcpServer.Services;

public class LocalLoanDataService : ILoanDataService
{
    private readonly string _path;

    public LocalLoanDataService(IConfiguration config, IWebHostEnvironment env)
    {
        var relative = config["LocalFiles:LoansPath"] ?? "../loans";
        _path = Path.GetFullPath(Path.Combine(env.ContentRootPath, relative));
    }

    public Task<IEnumerable<LoanSummary>> ListLoansAsync()
    {
        var result = new List<LoanSummary>();
        if (!Directory.Exists(_path))
            return Task.FromResult<IEnumerable<LoanSummary>>(result);

        foreach (var file in Directory.GetFiles(_path, "*.json").OrderBy(f => f))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                var root = doc.RootElement;
                result.Add(new LoanSummary(
                    Id:           root.TryGetProperty("id", out var v) ? v.GetString() ?? "" : Path.GetFileNameWithoutExtension(file),
                    Borrower:     root.TryGetProperty("borrower", out v) ? v.GetString() ?? "" : "",
                    Amount:       root.TryGetProperty("amount", out v) ? v.GetString() ?? "" : "",
                    Currency:     root.TryGetProperty("currency", out v) ? v.GetString() ?? "" : "",
                    Status:       root.TryGetProperty("status", out v) ? v.GetString() ?? "" : "",
                    RiskRating:   root.TryGetProperty("risk_rating", out v) ? v.GetString() ?? "" : "",
                    MaturityDate: root.TryGetProperty("maturity_date", out v) ? v.GetString() ?? "" : ""
                ));
            }
            catch { }
        }
        return Task.FromResult<IEnumerable<LoanSummary>>(result);
    }

    public Task<JsonDocument?> LoadLoanAsync(string id)
    {
        var path = Path.Combine(_path, $"{id}.json");
        if (!File.Exists(path)) return Task.FromResult<JsonDocument?>(null);
        return Task.FromResult<JsonDocument?>(JsonDocument.Parse(File.ReadAllText(path)));
    }
}
```

---

### Step 3 — MCP tools (`DataMcpServer/Tools/`)

```csharp
// DataMcpServer/Tools/LoanTools.cs
using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using DataMcpServer.Services;

namespace DataMcpServer.Tools;

[McpServerToolType]
public sealed class LoanTools(ILoanDataService loans)
{
    [McpServerTool(Name = "ListLoans"), Description("List all loans with summary fields (id, borrower, amount, currency, status, riskRating, maturityDate).")]
    public async Task<string> ListLoans()
    {
        var records = await loans.ListLoansAsync();
        return JsonSerializer.Serialize(records, new JsonSerializerOptions { WriteIndented = true });
    }

    [McpServerTool(Name = "GetLoan"), Description("Get full details of a loan — borrower info, terms, covenants, collateral, risk flags, and status.")]
    public async Task<string> GetLoan(
        [Description("Loan ID, e.g. LN001")] string id)
    {
        id = id.Trim().ToUpperInvariant();
        using var doc = await loans.LoadLoanAsync(id);
        if (doc == null)
            return $"Loan {id} not found.";

        return JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true });
    }
}
```

> **Tool naming:** Always set `Name = "..."` explicitly on `[McpServerTool]`. The SDK applies a snake_case naming policy by default; without a pinned name the tool registers as `"list_loans"` instead of `"ListLoans"`, and the ToolFilter comparison will fail silently.

---

### Step 4 — Register the service (`DataMcpServer/Program.cs`)

```csharp
// In DataMcpServer/Program.cs, inside the LocalFiles branch:
else
{
    builder.Services.AddSingleton<ITransactionDataService, LocalTransactionDataService>();
    builder.Services.AddSingleton<IKycDataService, LocalFileKycDataService>();
    builder.Services.AddSingleton<ILoanDataService, LocalLoanDataService>();  // add this
}
```

Add the path key to `DataMcpServer/appsettings.json`:

```json
{
  "LocalFiles": {
    "TransactionsPath": "../transactions",
    "KycPath":          "../kyc",
    "LoansPath":        "../loans"
  }
}
```

---

### Step 5 — Pages (`ReviewPortal/Pages/`)

Create two pages: a list page and a detail page. The only framework requirements are:

1. Set `data-resource-type` on `<body>` — this is what the agent uses to select the prompt and tool filter.
2. Set `data-resource-id` on detail pages.
3. Include `<script src="~/js/universal-agent.js"></script>` and the agent modal boilerplate (copy from `Kyc.cshtml` or `KycDetail.cshtml`).

**List page `<body>` tag:**
```html
<body data-resource-type="loans">
```

**Detail page `<body>` tag:**
```html
<body data-resource-type="loan"
      data-resource-id="@Model.LoanId">
```

You also need a page service class to load data for rendering (separate from the MCP data service):

```csharp
// ReviewPortal/Services/LoanService.cs
using System.Text.Json;

namespace ReviewPortal.Services;

public class LoanService(IConfiguration config, IWebHostEnvironment env)
{
    private readonly string _path = Path.GetFullPath(
        Path.Combine(env.ContentRootPath, config["LoanPath"] ?? "../loans"));

    public JsonDocument? LoadLoan(string id)
    {
        var file = Path.Combine(_path, $"{id.ToUpperInvariant()}.json");
        return File.Exists(file) ? JsonDocument.Parse(File.ReadAllText(file)) : null;
    }
}
```

Register it in `ReviewPortal/Program.cs`:

```csharp
builder.Services.AddSingleton<LoanService>();
```

Add the path to `ReviewPortal/appsettings.json`:

```json
{
  "LoanPath": "../loans"
}
```

---

### Step 6 — Prompts and tool filter (`ReviewPortal/prompts.json`)

Add entries for both resource types. Changes here take effect immediately — no restart needed.

```json
{
  "Prompts": {
    "ByResourceType": {
      "loan": "You are a credit analyst. Call the GetLoan tool first to retrieve the full loan record, then produce your analysis in exactly this format:\n\nSUMMARY:\n1. Loan Overview\n   - Borrower, amount, currency, term, maturity date\n\n2. Risk Assessment\n   - Risk rating, covenant compliance, collateral coverage\n\n3. Recommendation\n   Exactly one of: APPROVE / REFER FOR REVIEW / DECLINE\n\n4. Justification\n   Bullet points supporting the recommendation\n\nQUESTIONS:\n- <question 1>\n- <question 2>\nGenerate up to 5 short questions relevant to this loan. Frame as questions the user would ask you.\n\nRules: Use only tool-retrieved data. Do not invent facts.",

      "loans": "You are a loan portfolio analyst. Call the ListLoans tool first, then output exactly:\n\nSUMMARY:\n<3-5 sentence summary: total count, status breakdown, risk rating distribution, any loans near maturity or with elevated risk that need attention>\n\nQUESTIONS:\n- <question 1>\nGenerate up to 5 questions to help the user prioritise. Frame as questions the user would ask you.\n\nRules: Use only tool-retrieved data. Be concise and factual."
    }
  },
  "ToolFilter": {
    "loan":  ["GetLoan"],
    "loans": ["ListLoans"]
  }
}
```

---

### Checklist

- [ ] `LoanSummary.cs` in `DataMcpServer/Models/`
- [ ] `ILoanDataService.cs` + `LocalLoanDataService.cs` in `DataMcpServer/Services/`
- [ ] `LoanTools.cs` with explicit `Name = "..."` in `DataMcpServer/Tools/`
- [ ] Service registered in `DataMcpServer/Program.cs`
- [ ] `LocalFiles:LoansPath` in `DataMcpServer/appsettings.json`
- [ ] List and detail pages in `ReviewPortal/Pages/` with correct `data-resource-type`
- [ ] `LoanService.cs` registered in `ReviewPortal/Program.cs`
- [ ] `LoanPath` in `ReviewPortal/appsettings.json`
- [ ] `ByResourceType` entries in `ReviewPortal/prompts.json`
- [ ] `ToolFilter` entries in `ReviewPortal/prompts.json`
- [ ] `loans/` directory created (add to `.gitignore` if it contains sensitive data)
- [ ] Both servers restarted

---

## Switching to Portal APIs

When your internal REST APIs are available, switch the data source without touching any other code:

**`DataMcpServer/appsettings.Development.json`** (gitignored):

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

The `PortalApiTransactionDataService` and `PortalApiKycDataService` stubs are already registered in `DataMcpServer/Program.cs` under the `PortalApi` branch. Implement their methods to call your portal endpoints. The web app and LLM are unaffected.

---

## Secrets

| File | Contains | Committed |
|------|----------|-----------|
| `ReviewPortal/appsettings.Development.json` | Azure OpenAI endpoint + key | Never |
| `DataMcpServer/appsettings.Development.json` | Portal API credentials | Never |
| `transactions/` | Transaction record data | Never |
| `kyc/` | KYC record data | Never |
| `DataMcpServer/tessdata/` | OCR model binary | Never |
