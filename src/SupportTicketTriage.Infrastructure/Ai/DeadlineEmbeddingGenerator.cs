using Microsoft.Extensions.AI;

namespace SupportTicketTriage.Infrastructure.Ai;

/// The embedding counterpart of <see cref="DeadlineChatClient"/>.
public sealed class DeadlineEmbeddingGenerator(
    IEmbeddingGenerator<string, Embedding<float>> inner,
    TimeSpan deadline)
    : DelegatingEmbeddingGenerator<string, Embedding<float>>(inner)
{
    public TimeSpan Deadline { get; } = deadline;

    public override Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default) =>
        CallDeadline.RunAsync(
            Deadline,
            ct => base.GenerateAsync(values, options, ct),
            cancellationToken);
}
