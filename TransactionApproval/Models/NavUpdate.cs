using System.Text.Json;
using System.Text.Json.Serialization;

namespace TransactionApproval.Models;

public class NavUpdate
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("context")]
    public JsonElement Context { get; set; }
}
