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
    public static IChatClient? Create(AzureOpenAiOptions options)
    {
        if (!options.IsChatConfigured)
        {
            return null;
        }

        return new AzureOpenAIClient(
                new Uri(options.Endpoint!),
                new ApiKeyCredential(options.ApiKey!))
            .GetChatClient(options.ChatDeployment!)
            .AsIChatClient();
    }
}
