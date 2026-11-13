using System.ClientModel.Primitives;
using Azure.AI.OpenAI;

namespace SupportTicketTriage.Infrastructure.Ai;

internal static class AzureOpenAiClientOptions
{
    /// The SDK already retried before this existed - three retries, 100 s per
    /// attempt - which is where "Retry failed after 4 tries" in ADR-029's
    /// diagnostic came from. Nothing here adds retry; it makes the policy a
    /// stated value instead of an inherited one.
    public static AzureOpenAIClientOptions From(AiCallLimits limits) => new()
    {
        NetworkTimeout = limits.AttemptTimeout,
        RetryPolicy = new ClientRetryPolicy(limits.MaxRetries),
    };
}
