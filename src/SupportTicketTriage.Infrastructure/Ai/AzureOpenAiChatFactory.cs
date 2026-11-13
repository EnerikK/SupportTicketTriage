using System.ClientModel;
using Azure.AI.OpenAI;
using Microsoft.Extensions.AI;

namespace SupportTicketTriage.Infrastructure.Ai;

/// Builds the chat client for a configured Azure OpenAI deployment, or returns
/// null when no chat deployment is configured.
///
/// Separate from <see cref="AzureOpenAiEmbeddingFactory"/> rather than merged
/// with it: the two deployments are configured and fail independently, and one
/// factory answering "which client did you mean" would need a discriminator
/// that says nothing the two method names do not.
public static class AzureOpenAiChatFactory
{
    /// <param name="limits">Defaults to <see cref="AiCallLimits.Chat"/>, which
    /// the API and the harness's drafting stages share so the harness measures
    /// what the application does. Only the judge passes anything else.</param>
    public static IChatClient? Create(AzureOpenAiOptions options, AiCallLimits? limits = null)
    {
        if (!options.IsChatConfigured)
        {
            return null;
        }

        limits ??= AiCallLimits.Chat;

        var client = new AzureOpenAIClient(
                new Uri(options.Endpoint!),
                new ApiKeyCredential(options.ApiKey!),
                AzureOpenAiClientOptions.From(limits))
            .GetChatClient(options.ChatDeployment!)
            .AsIChatClient();

        return limits.Deadline is { } deadline
            ? client.AsBuilder().Use(inner => new DeadlineChatClient(inner, deadline)).Build()
            : client;
    }
}
