using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using OpenAI.Chat;

namespace TransactionApproval.Services;

public sealed class McpClientService(IConfiguration config) : IAsyncDisposable
{
    private readonly string _endpoint = config["McpServer:Url"] ?? "http://localhost:5001/mcp";
    private McpClient? _client;
    private IList<McpClientTool>? _tools;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private async Task EnsureConnectedAsync(CancellationToken ct = default)
    {
        if (_client != null) return;
        await _lock.WaitAsync(ct);
        try
        {
            if (_client != null) return;
            var transport = new HttpClientTransport(new HttpClientTransportOptions
            {
                Endpoint = new Uri(_endpoint),
                TransportMode = HttpTransportMode.AutoDetect,
            });
            _client = await McpClient.CreateAsync(transport, null, null, ct);
            _tools = await _client.ListToolsAsync((ModelContextProtocol.RequestOptions?)null, ct);
        }
        finally { _lock.Release(); }
    }

    public async Task<IReadOnlyList<ChatTool>> GetChatToolsAsync(CancellationToken ct = default)
    {
        await EnsureConnectedAsync(ct);
        return _tools!.Select(t => ChatTool.CreateFunctionTool(
            t.Name,
            t.Description,
            BinaryData.FromString(t.JsonSchema.GetRawText())
        )).ToList();
    }

    public async Task<string> CallToolAsync(string name, BinaryData argsData, CancellationToken ct = default)
    {
        await EnsureConnectedAsync(ct);

        // Parse the LLM's argument JSON into the dict the MCP client expects
        var args = new Dictionary<string, object?>();
        using (var doc = System.Text.Json.JsonDocument.Parse(argsData.ToMemory()))
        {
            foreach (var prop in doc.RootElement.EnumerateObject())
                args[prop.Name] = prop.Value.Clone();
        }

        var result = await _client!.CallToolAsync(name, args, null, null, ct);

        if (result.IsError == true)
        {
            var errText = string.Concat(result.Content.OfType<TextContentBlock>().Select(b => b.Text ?? ""));
            return $"[Tool error: {errText}]";
        }

        return string.Concat(result.Content.OfType<TextContentBlock>().Select(b => b.Text ?? ""));
    }

    public async ValueTask DisposeAsync()
    {
        if (_client is IAsyncDisposable d) await d.DisposeAsync();
    }
}
