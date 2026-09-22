using Microsoft.Extensions.AI;
using SupportTicketTriage.Infrastructure.Ai;
using SupportTicketTriage.Infrastructure.Persistence;

namespace SupportTicketTriage.Eval.Embedding;

/// The embedding generator a run uses, together with the name stamped onto
/// every row and every report it produces.
public sealed record EmbeddingSelection(
    IEmbeddingGenerator<string, Embedding<float>> Generator,
    string ModelName)
{
    /// Deliberately the same rule the API applies in
    /// InfrastructureServiceCollectionExtensions: a real deployment when the
    /// options are fully configured, the lexical stand-in otherwise. Holding
    /// the two in step is what lets a harness number describe the application
    /// rather than the harness.
    public static EmbeddingSelection From(AzureOpenAiOptions options)
    {
        var deployment = AzureOpenAiEmbeddingFactory.Create(options);

        return deployment is not null
            ? new EmbeddingSelection(deployment, options.EmbeddingDeployment!)
            : new EmbeddingSelection(
                new LexicalEmbeddingGenerator(TicketEmbedding.Dimensions),
                LexicalEmbeddingGenerator.ModelName);
    }

    /// Read from the environment rather than a command-line argument so the API
    /// key never reaches the shell history. The double-underscore names are the
    /// ones docker-compose already binds to the AzureOpenAi section, so one set
    /// of variables serves both the stack and this tool.
    public static AzureOpenAiOptions OptionsFromEnvironment() => new()
    {
        Endpoint = Environment.GetEnvironmentVariable("AzureOpenAi__Endpoint"),
        ApiKey = Environment.GetEnvironmentVariable("AzureOpenAi__ApiKey"),
        EmbeddingDeployment = Environment.GetEnvironmentVariable("AzureOpenAi__EmbeddingDeployment"),
    };
}
