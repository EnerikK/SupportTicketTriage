using System.Diagnostics;
using Microsoft.Extensions.AI;

namespace SupportTicketTriage.Eval.Instrumentation;

/// Times every embedding call and records its token usage.
///
/// **This is the decorator that justifies the whole approach.**
/// `TicketEmbeddingService` calls `GenerateVectorAsync`, which is an extension
/// method: it calls `GenerateAsync` underneath, takes `[0].Vector`, and throws
/// the `GeneratedEmbeddings<Embedding<float>>` away - and with it the `Usage`
/// the provider populated. No amount of work at or above the service recovers
/// that, short of rewriting it to call `GenerateAsync` itself.
///
/// A decorator sits *below* the extension method, so it sees the collection the
/// extension method is about to discard. Embedding token counts therefore cost
/// no production change at all, which is why the same shape was used for chat
/// rather than two different mechanisms.
///
/// Only one stage, so no stage parameter: embeddings are generated at exactly
/// one point in the pipeline. If the review backfill is ever measured, it needs
/// its own instance with its own stage rather than being blended in here - a
/// backfill is a different operation from ingest-time embedding even though it
/// is the same call.
public sealed class RecordingEmbeddingGenerator(
    IEmbeddingGenerator<string, Embedding<float>> inner,
    AiCallRecorder recorder)
    : DelegatingEmbeddingGenerator<string, Embedding<float>>(inner)
{
    public override async Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.GetTimestamp();

        try
        {
            var embeddings = await base.GenerateAsync(values, options, cancellationToken);

            // Output tokens stay null rather than zero: an embedding returns a
            // vector, so "how many tokens did it write" has no answer rather
            // than the answer nothing. The cost metric prices input tokens only.
            recorder.RecordSuccess(AiStage.Embedding, Stopwatch.GetElapsedTime(started), embeddings.Usage);
            return embeddings;
        }
        catch
        {
            recorder.RecordFailure(AiStage.Embedding, Stopwatch.GetElapsedTime(started));
            throw;
        }
    }
}
