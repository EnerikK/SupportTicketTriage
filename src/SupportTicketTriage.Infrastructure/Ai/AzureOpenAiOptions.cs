namespace SupportTicketTriage.Infrastructure.Ai;

public sealed class AzureOpenAiOptions
{
    public const string SectionName = "AzureOpenAi";

    public string? Endpoint { get; set; }
    public string? ApiKey { get; set; }
    public string? EmbeddingDeployment { get; set; }
    public string? ChatDeployment { get; set; }

    private bool HasCredentials =>
        !string.IsNullOrWhiteSpace(Endpoint) && !string.IsNullOrWhiteSpace(ApiKey);

    // The two deployments are configured independently because they fail
    // independently: an embedding deployment without a chat one still gives
    // working retrieval, which is the state this project was in until now.
    public bool IsEmbeddingConfigured =>
        HasCredentials && !string.IsNullOrWhiteSpace(EmbeddingDeployment);

    public bool IsChatConfigured =>
        HasCredentials && !string.IsNullOrWhiteSpace(ChatDeployment);
}
