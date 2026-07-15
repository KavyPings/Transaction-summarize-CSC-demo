using System.Collections.Concurrent;
using TransactionApproval.Models;

namespace TransactionApproval.Services;

public class AgentContextStore
{
    private readonly ConcurrentDictionary<string, AgentContext> _store = new();

    public void Set(string id, AgentContext ctx) => _store[id] = ctx;

    public AgentContext? Get(string id) => _store.TryGetValue(id, out var ctx) ? ctx : null;
}
