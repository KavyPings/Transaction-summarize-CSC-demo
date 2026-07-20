using Microsoft.Extensions.Options;
using ReviewPortal.Prompts;

namespace ReviewPortal.Services;

public class PromptsOptions
{
    public string Analyze { get; set; } = "";
    public string Chat { get; set; } = "";
    public Dictionary<string, string> ByResourceType { get; set; } = new();
}

public class PromptService(IOptionsMonitor<PromptsOptions> monitor)
{
    public string GetAnalyzePrompt(string resourceType)
    {
        var opts = monitor.CurrentValue;
        if (opts.ByResourceType.TryGetValue(resourceType, out var p) && p.Length > 0) return p;
        return opts.Analyze.Length > 0 ? opts.Analyze : AgentPrompts.AnalyzePrompt;
    }

    public string GetChatPrompt()
    {
        var opts = monitor.CurrentValue;
        return opts.Chat.Length > 0 ? opts.Chat : AgentPrompts.ChatPrompt;
    }
}
