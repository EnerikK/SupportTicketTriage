using Microsoft.Extensions.AI;

namespace SupportTicketTriage.Eval.Instrumentation;

/// Which pipeline stage a recorded call belongs to.
///
/// `Retrieval` is in this list even though it is a pgvector query rather than a
/// model call. A reviewer waiting for a draft waits for both, so leaving the
/// database out would report a latency figure smaller than anything anyone
/// experiences - but it is labelled separately so the two are never summed as
/// though they cost the same kind of money. Retrieval costs none.
public enum AiStage
{
    Embedding,
    Classification,
    Retrieval,
    Generation,

    /// Kept apart from Generation so the judge's latency and tokens never
    /// blend into the drafting figures. It runs on a different deployment at
    /// a different rate, so averaging the two would misprice both.
    Judging,
}

/// One call, with what it cost in time and tokens.
///
/// Tokens are nullable rather than zero, following the same rule the rest of
/// the harness applies to unmeasurable quantities: the lexical stand-in and the
/// test fakes report no usage at all, and a zero there would price a real run
/// at nothing. Embeddings legitimately have no output tokens - they return a
/// vector, not text - so that stays null on a successful embedding too.
///
/// `Succeeded` exists because a failed call still consumed wall-clock time.
/// Averaging a timeout into the latency distribution would make the system look
/// slow for a reason that is not slowness, and dropping it silently would hide
/// that the call happened at all. Recorded, flagged, and excluded from the
/// percentiles by the metric rather than by the recorder.
public sealed record AiCall(
    AiStage Stage,
    TimeSpan Elapsed,
    long? InputTokens,
    long? OutputTokens,
    bool Succeeded);

/// Collects what each call cost, for the report to summarise.
///
/// A plain `List` and not a concurrent collection, deliberately: every stage in
/// `EvalRunner` is a sequential `foreach` with an `await` per iteration and no
/// parallelism anywhere, so there is no second writer to guard against. A
/// thread-safe collection here would be machinery defending against a race the
/// harness cannot have, and it would suggest to a reader that the harness runs
/// calls in parallel - which would itself invalidate the latency numbers, since
/// concurrent calls contend and the percentiles would measure the contention.
///
/// If the harness is ever parallelised to cut the 300-call runtime, this is the
/// type that has to change first, and the percentiles have to be reinterpreted
/// rather than merely re-collected.
public sealed class AiCallRecorder
{
    private readonly List<AiCall> _calls = [];

    public IReadOnlyList<AiCall> Calls => _calls;

    public void Record(AiCall call) => _calls.Add(call);

    /// Records a call that returned, taking whatever the provider reported.
    ///
    /// `usage` is null on every offline run, because neither the lexical
    /// stand-in nor the chat fakes populate it. That is the ordinary case
    /// rather than an error, so it is mapped to null tokens and left for the
    /// cost metric to refuse to price.
    public void RecordSuccess(AiStage stage, TimeSpan elapsed, UsageDetails? usage) =>
        Record(new AiCall(
            stage,
            elapsed,
            usage?.InputTokenCount,
            usage?.OutputTokenCount,
            Succeeded: true));

    public void RecordFailure(AiStage stage, TimeSpan elapsed) =>
        Record(new AiCall(stage, elapsed, InputTokens: null, OutputTokens: null, Succeeded: false));
}
