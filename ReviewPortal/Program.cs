using System.Text.Json;
using OpenAI.Chat;
using ReviewPortal.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddJsonFile("prompts.json", optional: true, reloadOnChange: true);

builder.Services.AddRazorPages();
builder.Services.AddSingleton<AzureOpenAiService>();
builder.Services.AddSingleton<TransactionService>();
builder.Services.AddSingleton<KycService>();
builder.Services.AddSingleton<McpClientService>();
builder.Services.Configure<PromptsOptions>(builder.Configuration.GetSection("Prompts"));
builder.Services.AddSingleton<PromptService>();

var app = builder.Build();
var maxChatHistory = app.Configuration.GetValue<int>("ChatHistoryLimit", 10);
var appConfig = app.Configuration;

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
    McpClientService mcpClient,
    PromptService prompts) =>
{
    JsonElement body;
    try
    {
        using var doc = await JsonDocument.ParseAsync(request.Body);
        body = doc.RootElement.Clone();
    }
    catch { return Results.BadRequest(new { error = "Invalid JSON" }); }

    var resource = body.TryGetProperty("resource", out var res) ? res : default;
    var resourceType = resource.ValueKind != JsonValueKind.Undefined &&
                       resource.TryGetProperty("resourceType", out var rt)
        ? rt.GetString() ?? "unknown" : "unknown";
    var resourceId = resource.ValueKind != JsonValueKind.Undefined &&
                     resource.TryGetProperty("resourceId", out var ri) &&
                     ri.ValueKind != JsonValueKind.Null
        ? ri.GetString() ?? "" : "";

    var systemPrompt = prompts.GetAnalyzePrompt(resourceType);
    // NOTE: keep wording neutral — Azure Prompt Shield blocks imperative phrasing
    // like "fetch details using the X tool" (treated as a prompt-injection pattern).
    var userPrompt = resourceId.Length > 0
        ? $"Please analyse {resourceType} {resourceId} and produce your structured report."
        : $"Please analyse the available {resourceType} data and produce your structured report.";

    try
    {
        var messages = new List<ChatMessage>
        {
            new SystemChatMessage(systemPrompt),
            new UserChatMessage(userPrompt),
        };

        var allowedTools = GetAllowedTools(appConfig, resourceType);
        var raw = await RunAgentLoopAsync(openAi.GetChatClient(), mcpClient, messages, allowedTools);
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
    McpClientService mcpClient,
    PromptService prompts) =>
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

    // Extract page context so the LLM knows which record the user is currently viewing
    var chatResource = body.TryGetProperty("resource", out var cr) ? cr : default;
    var chatResourceType = chatResource.ValueKind != JsonValueKind.Undefined &&
                           chatResource.TryGetProperty("resourceType", out var crt)
        ? crt.GetString() ?? "unknown" : "unknown";
    var chatResourceId = chatResource.ValueKind != JsonValueKind.Undefined &&
                         chatResource.TryGetProperty("resourceId", out var cri) &&
                         cri.ValueKind != JsonValueKind.Null
        ? cri.GetString() ?? "" : "";

    var pageContext = chatResourceId.Length > 0
        ? $"Page context: the user is currently viewing {chatResourceType} record \"{chatResourceId}\"."
        : $"Page context: the user is currently on the {chatResourceType} list page.";

    // Reconstruct conversation history from what the browser sent, capped to last N messages
    var messages = new List<ChatMessage> { new SystemChatMessage($"{pageContext}\n\n{prompts.GetChatPrompt()}") };

    if (body.TryGetProperty("chat_history", out var ch) && ch.ValueKind == JsonValueKind.Array)
    {
        var allHistory = ch.EnumerateArray().ToList();
        var skip = Math.Max(0, allHistory.Count - maxChatHistory);
        if (skip > 0)
            messages.Add(new SystemChatMessage("[Note: earlier conversation has been trimmed for length.]"));
        foreach (var msg in allHistory.Skip(skip))
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
        var allowedTools = GetAllowedTools(appConfig, chatResourceType);
        var raw = await RunAgentLoopAsync(openAi.GetChatClient(), mcpClient, messages, allowedTools);
        var (answer, followUps) = AgentService.ParseChatResponse(raw);
        return Results.Ok(new { answer, questions = followUps });
    }
    catch (Exception ex)
    {
        return Results.Json(new { error = $"Could not answer: {ex.Message}" }, statusCode: 500);
    }
});

app.Run();

// Returns the allowed tool names for a resource type, or null for no filter (all tools).
// Reads from ToolFilter:<resourceType> in prompts.json (hot-reloadable).
static IEnumerable<string>? GetAllowedTools(IConfiguration config, string resourceType)
{
    var tools = config.GetSection($"ToolFilter:{resourceType}").Get<List<string>>();
    return tools?.Count > 0 ? tools : null;
}

// ── Shared agentic loop ───────────────────────────────────────────────────────
// Runs the LLM in a tool-calling loop until it produces a final text response
// or the iteration limit is reached.
static async Task<string> RunAgentLoopAsync(
    ChatClient chatClient,
    McpClientService mcpClient,
    List<ChatMessage> messages,
    IEnumerable<string>? allowedTools = null,
    CancellationToken ct = default)
{
    var chatTools = await mcpClient.GetChatToolsAsync(allowedTools, ct);
    if (chatTools.Count == 0 && allowedTools != null)
        return $"(Agent error: none of the required tools [{string.Join(", ", allowedTools)}] were found on the MCP server. Restart DataMcpServer and try again.)";

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
            return response.Value.Content.FirstOrDefault()?.Text ?? "";
        }
    }

    return "(Max iterations reached — the agent could not complete the request.)";
}
