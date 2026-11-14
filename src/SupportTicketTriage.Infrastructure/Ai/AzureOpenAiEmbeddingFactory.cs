using System.ClientModel;
using Azure.AI.OpenAI;
using Microsoft.Extensions.AI;

namespace SupportTicketTriage.Infrastructure.Ai;

/// Builds the embedding generator for a configured Azure OpenAI deployment,
/// or returns null when the deployment is not configured.
///
/// Both the API and the evaluation harness need this, and both stamp the
/// deployment name onto every row they write. Retrieval only ever compares
/// vectors carrying the same stamp, so if the two built the client differently
/// the harness would measure against vectors the application would never
/// return - a defect that reads as poor relevance rather than as a bug. The
/// same reasoning is why the limits are not a parameter here: there is no
/// caller that should embed under different ones.
public static class AzureOpenAiEmbeddingFactory
{
    public static IEmbeddingGenerator<string, Embedding<float>>? Create(AzureOpenAiOptions options)
    {
        if (!options.IsEmbeddingConfigured)
        {
            return null;
        }

        var limits = AiCallLimits.Embedding;

        var generator = new AzureOpenAIClient(
                new Uri(options.Endpoint!),
                new ApiKeyCredential(options.ApiKey!),
                AzureOpenAiClientOptions.From(limits))
            .GetEmbeddingClient(options.EmbeddingDeployment!)
            .AsIEmbeddingGenerator();

        return limits.Deadline is { } deadline
            ? new DeadlineEmbeddingGenerator(generator, deadline)
            : generator;
    }
}
