using Azure;
using Azure.AI.OpenAI;
using OpenAI.Chat;

namespace TransactionApproval.Services;

public class AzureOpenAiService
{
    private readonly AzureOpenAIClient _client;
    private readonly string _deployment;

    public AzureOpenAiService(IConfiguration config)
    {
        var endpoint = config["AzureOpenAI:Endpoint"]
            ?? throw new InvalidOperationException("Missing AzureOpenAI:Endpoint in configuration.");
        var apiKey = config["AzureOpenAI:ApiKey"]
            ?? throw new InvalidOperationException("Missing AzureOpenAI:ApiKey in configuration.");
        _deployment = config["AzureOpenAI:Deployment"]
            ?? throw new InvalidOperationException("Missing AzureOpenAI:Deployment in configuration.");

        _client = new AzureOpenAIClient(new Uri(endpoint), new AzureKeyCredential(apiKey));
    }

    public ChatClient GetChatClient() => _client.GetChatClient(_deployment);
}
