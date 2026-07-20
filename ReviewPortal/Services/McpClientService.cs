using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using OpenAI.Chat;

namespace ReviewPortal.Services;

public sealed class McpClientService(IConfiguration config) : IAsyncDisposable
{
    private readonly string _endpoint = config["McpServer:Url"] ?? "http://localhost:5001/mcp";
    private McpClient? _client;
    private IList<McpClientTool>? _tools;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private void ResetConnection()
    {
        if (_client is IAsyncDisposable d) _ = d.DisposeAsync().AsTask();
        _client = null;
        _tools = null;
    }

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

    public async Task<IReadOnlyList<ChatTool>> GetChatToolsAsync(
        IEnumerable<string>? allowedNames = null,
        CancellationToken ct = default)
    {
        await EnsureConnectedAsync(ct);
        IEnumerable<McpClientTool> source;
        if (allowedNames != null)
        {
            var filtered = _tools!.Where(t => allowedNames.Contains(t.Name)).ToList();
            // If the filter matched nothing but tools exist, the server may have been restarted
            // with new tools — reconnect once to refresh the cached list.
            if (filtered.Count == 0 && _tools!.Count > 0)
            {
                await _lock.WaitAsync(ct);
                try
                {
                    ResetConnection();  // sets _client=null, _tools=null inside the lock
                }
                finally { _lock.Release(); }
                await EnsureConnectedAsync(ct);  // re-acquires lock to reconnect safely
                filtered = _tools!.Where(t => allowedNames.Contains(t.Name)).ToList();
            }
            source = filtered;
        }
        else
        {
            source = _tools!;
        }
        return source.Select(t => ChatTool.CreateFunctionTool(
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
