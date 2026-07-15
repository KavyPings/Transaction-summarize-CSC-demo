using Azure;
using Azure.AI.OpenAI;
using OpenAI.Chat;

namespace TransactionApproval.Services;

public class AzureOpenAiService
{
    private readonly IConfiguration _config;
    private AzureOpenAIClient? _client;
    private string _deployment = "";

    public AzureOpenAiService(IConfiguration config) => _config = config;

    public ChatClient GetChatClient()
    {
        // Build the client lazily on first use (rather than in the constructor) so that a
        // missing/blank configuration surfaces inside the request handler's try/catch as a
        // readable JSON error, instead of throwing during DI resolution and returning an
        // empty-body 500 (which the browser reports as "Unexpected end of JSON input").
        if (_client is null)
        {
            var endpoint = _config["AzureOpenAI:Endpoint"];
            var apiKey = _config["AzureOpenAI:ApiKey"];
            _deployment = _config["AzureOpenAI:Deployment"] ?? "";

            if (string.IsNullOrWhiteSpace(endpoint) ||
                string.IsNullOrWhiteSpace(apiKey) ||
                string.IsNullOrWhiteSpace(_deployment))
            {
                throw new InvalidOperationException(
                    "Azure OpenAI is not configured. Set AzureOpenAI:Endpoint, AzureOpenAI:ApiKey and " +
                    "AzureOpenAI:Deployment (in appsettings.Development.json or environment variables). " +
                    "If those values live only in appsettings.Development.json, run the app in the " +
                    "Development environment (ASPNETCORE_ENVIRONMENT=Development).");
            }

            _client = new AzureOpenAIClient(new Uri(endpoint), new AzureKeyCredential(apiKey));
        }

        return _client.GetChatClient(_deployment);
    }
}
