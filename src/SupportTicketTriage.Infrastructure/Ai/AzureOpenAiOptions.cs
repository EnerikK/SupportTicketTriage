namespace SupportTicketTriage.Infrastructure.Ai;

public sealed class AzureOpenAiOptions
{
    public const string SectionName = "AzureOpenAi";

    public string? Endpoint { get; set; }
    public string? ApiKey { get; set; }
    public string? EmbeddingDeployment { get; set; }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Endpoint)
        && !string.IsNullOrWhiteSpace(ApiKey)
        && !string.IsNullOrWhiteSpace(EmbeddingDeployment);
}
