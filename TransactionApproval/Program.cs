using System.Text.Json;
using System.Text.Json.Nodes;
using OpenAI.Chat;
using TransactionApproval.Models;
using TransactionApproval.Prompts;
using TransactionApproval.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();
builder.Services.AddSingleton<AzureOpenAiService>();
builder.Services.AddSingleton<TransactionService>();
builder.Services.AddSingleton<EvidenceExtractorService>();
builder.Services.AddSingleton<AgentContextStore>();

var app = builder.Build();

app.UseStaticFiles();
app.UseRouting();
app.MapRazorPages();

// POST /agent/analyze
// Accepts structured page context from the browser, calls the LLM, returns summary + questions.
app.MapPost("/agent/analyze", async (
    HttpRequest request,
    AzureOpenAiService openAi,
    TransactionService txnSvc,
    EvidenceExtractorService evidenceSvc,
    AgentContextStore contextStore) =>
{
    JsonElement body;
    try
    {
        using var doc = await JsonDocument.ParseAsync(request.Body);
        body = doc.RootElement.Clone();
    }
    catch
    {
        return Results.BadRequest(new { error = "Invalid JSON" });
    }

    if (!body.TryGetProperty("context", out var contextEl))
        return Results.BadRequest(new { error = "Missing context" });

    var contextJson = contextEl.GetRawText();
    var pageType = contextEl.TryGetProperty("pageType", out var pt) ? pt.GetString() ?? "" : "";
    var txnId = "";
    if (contextEl.TryGetProperty("data", out var dataEl) &&
        dataEl.TryGetProperty("transaction_id", out var tid))
        txnId = tid.GetString() ?? "";

    var contextId = Guid.NewGuid().ToString();
    var evidenceFiles = new List<string>();
    string systemPrompt;
    UserChatMessage userMessage;

    if (pageType == "transaction_detail" && txnId.Length > 0)
    {
        systemPrompt = AgentPrompts.TransactionPrompt;
        try
        {
            using var txnDoc = txnSvc.LoadTransaction(txnId);
            if (txnDoc != null)
            {
                evidenceFiles = txnSvc.GetEvidenceFiles(txnDoc);

                // Enrich context with evidence_labels so the LLM knows what files are attached.
                var node = JsonNode.Parse(contextJson)!;
                var data = node["data"] as JsonObject ?? new JsonObject();
                node["data"] = data;
                var labels = new JsonArray();
                for (int i = 0; i < evidenceFiles.Count; i++)
                {
                    var ext = Path.GetExtension(evidenceFiles[i]).TrimStart('.').ToUpperInvariant();
                    labels.Add($"Evidence {i + 1} ({ext})");
                }
                data["evidence_labels"] = labels;
                contextJson = node.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
            }
        }
        catch { }

        userMessage = evidenceSvc.BuildUserMessage(contextJson, evidenceFiles);
    }
    else
    {
        systemPrompt = AgentPrompts.AnalyzePrompt;
        userMessage = new UserChatMessage($"Page data:\n{contextJson}");
    }

    try
    {
        var chatClient = openAi.GetChatClient();
        List<ChatMessage> messages =
        [
            new SystemChatMessage(systemPrompt),
            userMessage,
        ];

        var response = await chatClient.CompleteChatAsync(
            messages, new ChatCompletionOptions { Temperature = 0.2f });
        var raw = response.Value.Content[0].Text ?? "";

        var (summary, questions) = AgentService.ParseAnalyzeResponse(raw);

        contextStore.Set(contextId, new AgentContext
        {
            PageContextJson = contextJson,
            Summary = summary,
            PageType = pageType,
            TxnId = txnId,
        });

        return Results.Ok(new { summary, questions, context_id = contextId });
    }
    catch (Exception ex)
    {
        return Results.Json(new { error = $"Could not analyze: {ex.Message}" }, statusCode: 500);
    }
});

// POST /agent/chat
// Answers a follow-up question using stored context + client-side chat history.
app.MapPost("/agent/chat", async (
    HttpRequest request,
    AzureOpenAiService openAi,
    TransactionService txnSvc,
    AgentContextStore contextStore) =>
{
    JsonElement body;
    try
    {
        using var doc = await JsonDocument.ParseAsync(request.Body);
        body = doc.RootElement.Clone();
    }
    catch
    {
        return Results.BadRequest(new { error = "Invalid JSON" });
    }

    var question = body.TryGetProperty("question", out var q) ? (q.GetString() ?? "").Trim() : "";
    var contextId = body.TryGetProperty("context_id", out var cid) ? (cid.GetString() ?? "").Trim() : "";

    if (question.Length == 0 || contextId.Length == 0)
        return Results.BadRequest(new { error = "Missing question or context_id" });

    var stored = contextStore.Get(contextId);
    if (stored == null)
        return Results.NotFound(new { error = "Session expired — please reopen the Agent panel." });

    // Parse chat_history: [{role, text}] sent by the frontend
    var chatHistory = new List<(string Role, string Text)>();
    if (body.TryGetProperty("chat_history", out var ch) && ch.ValueKind == JsonValueKind.Array)
    {
        foreach (var msg in ch.EnumerateArray())
        {
            var role = msg.TryGetProperty("role", out var r) ? r.GetString() ?? "" : "";
            var text = msg.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
            if (role.Length > 0 && text.Length > 0)
                chatHistory.Add((role, text));
        }
    }

    // Parse nav_history: [{id, context}] maintained in sessionStorage
    var navHistory = new List<NavHistoryItem>();
    if (body.TryGetProperty("nav_history", out var nh) && nh.ValueKind == JsonValueKind.Array)
    {
        foreach (var item in nh.EnumerateArray())
        {
            var id = item.TryGetProperty("id", out var navId) ? navId.GetString() ?? "" : "";
            var ctx = item.TryGetProperty("context", out var navCtx) ? navCtx.Clone() : default;
            if (id.Length > 0)
                navHistory.Add(new NavHistoryItem(id, ctx));
        }
    }

    // Navigation agent: detect if the question references a different transaction
    NavUpdate? navUpdate = null;
    var ctxText = stored.PageContextJson;
    var effectiveSummary = stored.Summary;

    try
    {
        navUpdate = txnSvc.DetectNavTarget(question, stored.PageContextJson, navHistory);
    }
    catch { }

    if (navUpdate != null)
    {
        var navCtxJson = JsonSerializer.Serialize(navUpdate.Context, new JsonSerializerOptions { WriteIndented = true });
        ctxText = navCtxJson;
        effectiveSummary = $"[Data for transaction {navUpdate.Id}]\n{navCtxJson}";
    }

    try
    {
        var messages = new List<ChatMessage> { new SystemChatMessage(AgentPrompts.ChatPrompt) };

        if (chatHistory.Count == 0)
        {
            messages.Add(new UserChatMessage(
                $"Page context:\n{ctxText}\n\nSummary:\n{effectiveSummary}\n\nQuestion: {question}"));
        }
        else
        {
            // Prepend page context to the first user message so the LLM has full context
            for (int i = 0; i < chatHistory.Count; i++)
            {
                var (role, text) = chatHistory[i];
                var content = (i == 0 && role == "user")
                    ? $"Page context:\n{ctxText}\n\nSummary:\n{effectiveSummary}\n\nQuestion: {text}"
                    : text;

                messages.Add(role == "user"
                    ? new UserChatMessage(content)
                    : new AssistantChatMessage(content));
            }
            messages.Add(new UserChatMessage($"Question: {question}"));
        }

        var chatClient = openAi.GetChatClient();
        var response = await chatClient.CompleteChatAsync(
            messages, new ChatCompletionOptions { Temperature = 0.2f });
        var raw = response.Value.Content[0].Text ?? "";

        var (answer, followUps) = AgentService.ParseChatResponse(raw);

        return Results.Ok(new { answer, questions = followUps, nav_update = navUpdate });
    }
    catch (Exception ex)
    {
        return Results.Json(new { error = $"Could not answer: {ex.Message}" }, statusCode: 500);
    }
});

app.Run();
