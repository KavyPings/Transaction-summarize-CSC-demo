using System.Text.Json;
using OpenAI.Chat;
using TransactionApproval.Prompts;
using TransactionApproval.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();
builder.Services.AddSingleton<AzureOpenAiService>();
builder.Services.AddSingleton<TransactionService>();
builder.Services.AddSingleton<McpClientService>();

var app = builder.Build();

app.UseStaticFiles();
app.UseRouting();
app.MapRazorPages();

// ── POST /agent/start ────────────────────────────────────────────────────────
// Opens the agent panel. Receives the page identity, runs an agentic loop
// where the LLM fetches the data it needs via MCP tools, and returns a
// summary + suggested questions.
app.MapPost("/agent/start", async (
    HttpRequest request,
    AzureOpenAiService openAi,
    McpClientService mcpClient) =>
{
    JsonElement body;
    try
    {
        using var doc = await JsonDocument.ParseAsync(request.Body);
        body = doc.RootElement.Clone();
    }
    catch { return Results.BadRequest(new { error = "Invalid JSON" }); }

    var pageType = body.TryGetProperty("page", out var pg) &&
                   pg.TryGetProperty("pageType", out var pt)
        ? pt.GetString() ?? "unknown"
        : "unknown";

    var txnId = body.TryGetProperty("page", out var pg2) &&
                pg2.TryGetProperty("txnId", out var ti) &&
                ti.ValueKind != JsonValueKind.Null
        ? ti.GetString() ?? ""
        : "";

    string systemPrompt;
    string userPrompt;
    if (pageType == "transaction_detail" && txnId.Length > 0)
    {
        systemPrompt = AgentPrompts.StartPrompt;
        // NOTE: phrasing matters here — wording like "Fetch its full details and evidence
        // using the get_transaction tool" reads as a prompt-injection pattern to Azure's
        // jailbreak Prompt Shield and gets the request blocked (HTTP 400 content_filter).
        // Keep this as a natural analysis request; the system prompt already instructs
        // the model to call tools first.
        userPrompt = $"Please analyse transaction {txnId} in full, including its attached evidence files, and produce your structured report.";
    }
    else if (pageType == "transaction_list")
    {
        systemPrompt = AgentPrompts.ListPrompt;
        userPrompt = "Analyse the transaction list.";
    }
    else
    {
        systemPrompt = AgentPrompts.ListPrompt;
        userPrompt = "Please summarise the information available on this page.";
    }

    try
    {
        var messages = new List<ChatMessage>
        {
            new SystemChatMessage(systemPrompt),
            new UserChatMessage(userPrompt),
        };

        var raw = await RunAgentLoopAsync(openAi.GetChatClient(), mcpClient, messages);
        var (summary, questions) = AgentService.ParseAnalyzeResponse(raw);
        return Results.Ok(new { summary, questions });
    }
    catch (Exception ex)
    {
        return Results.Json(new { error = $"Could not start agent: {ex.Message}" }, statusCode: 500);
    }
});

// ── POST /agent/chat ─────────────────────────────────────────────────────────
// Answers a follow-up question. The LLM calls MCP tools whenever it needs
// data. Chat history is maintained by the browser and sent with each request.
app.MapPost("/agent/chat", async (
    HttpRequest request,
    AzureOpenAiService openAi,
    McpClientService mcpClient) =>
{
    JsonElement body;
    try
    {
        using var doc = await JsonDocument.ParseAsync(request.Body);
        body = doc.RootElement.Clone();
    }
    catch { return Results.BadRequest(new { error = "Invalid JSON" }); }

    var question = body.TryGetProperty("question", out var q) ? (q.GetString() ?? "").Trim() : "";
    if (question.Length == 0)
        return Results.BadRequest(new { error = "Missing question" });

    // Reconstruct conversation history from what the browser sent
    var messages = new List<ChatMessage> { new SystemChatMessage(AgentPrompts.ChatPrompt) };

    if (body.TryGetProperty("chat_history", out var ch) && ch.ValueKind == JsonValueKind.Array)
    {
        foreach (var msg in ch.EnumerateArray())
        {
            var role = msg.TryGetProperty("role", out var r) ? r.GetString() ?? "" : "";
            var text = msg.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
            if (role.Length == 0 || text.Length == 0) continue;
            messages.Add(role == "user" ? new UserChatMessage(text) : new AssistantChatMessage(text));
        }
    }
    messages.Add(new UserChatMessage(question));

    try
    {
        var raw = await RunAgentLoopAsync(openAi.GetChatClient(), mcpClient, messages);
        var (answer, followUps) = AgentService.ParseChatResponse(raw);
        return Results.Ok(new { answer, questions = followUps });
    }
    catch (Exception ex)
    {
        return Results.Json(new { error = $"Could not answer: {ex.Message}" }, statusCode: 500);
    }
});

app.Run();

// ── Shared agentic loop ───────────────────────────────────────────────────────
// Runs the LLM in a tool-calling loop until it produces a final text response
// or the iteration limit is reached.
static async Task<string> RunAgentLoopAsync(
    ChatClient chatClient,
    McpClientService mcpClient,
    List<ChatMessage> messages,
    CancellationToken ct = default)
{
    var chatTools = await mcpClient.GetChatToolsAsync(ct);
    var options = new ChatCompletionOptions { Temperature = 0.2f };
    foreach (var tool in chatTools)
        options.Tools.Add(tool);

    const int maxIterations = 8;
    for (int i = 0; i < maxIterations; i++)
    {
        var response = await chatClient.CompleteChatAsync(messages, options, ct);

        if (response.Value.FinishReason == ChatFinishReason.ToolCalls)
        {
            messages.Add(new AssistantChatMessage(response.Value));
            foreach (var call in response.Value.ToolCalls)
            {
                var result = await mcpClient.CallToolAsync(call.FunctionName, call.FunctionArguments, ct);
                messages.Add(new ToolChatMessage(call.Id, result));
            }
        }
        else
        {
            return response.Value.Content[0].Text ?? "";
        }
    }

    return "(Max iterations reached — the agent could not complete the request.)";
}
