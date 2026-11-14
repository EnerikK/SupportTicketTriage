namespace SupportTicketTriage.Infrastructure.Ai;

/// How long one model call may take, and how often it is retried.
///
/// Two layers, deliberately, and they do different jobs (ADR-039):
///
/// - <see cref="AttemptTimeout"/> and <see cref="MaxRetries"/> configure the
///   Azure SDK's own pipeline. Retry lives there rather than in middleware
///   because the SDK sees the HTTP response - it knows which status codes are
///   transient and honours <c>Retry-After</c> on a 429 - while middleware above
///   <c>IChatClient</c> sees only an exception. A second retry layer on top
///   would multiply attempts rather than add to them.
/// - <see cref="Deadline"/> bounds the whole call, every attempt and every
///   backoff included, and is enforced in M.E.AI middleware. It is the only
///   number a caller on a synchronous path can actually reason about.
public sealed record AiCallLimits(TimeSpan AttemptTimeout, int MaxRetries, TimeSpan? Deadline)
{
    /// Classification and generation share one chat client, so they share
    /// limits. Measured p95 on 2026-10-16: classification 1.7 s, generation
    /// 3.9 s. No maximum was recorded, so these are multiples of p95 rather
    /// than a fit to the tail: about 4x generation p95 per attempt, and a
    /// deadline that admits all four attempts at p95 plus the SDK's backoff.
    public static AiCallLimits Chat { get; } = new(
        AttemptTimeout: TimeSpan.FromSeconds(15),
        MaxRetries: 3,
        Deadline: TimeSpan.FromSeconds(40));

    /// Embedding p95 was 615 ms over 300 calls, with no failures.
    public static AiCallLimits Embedding { get; } = new(
        AttemptTimeout: TimeSpan.FromSeconds(5),
        MaxRetries: 3,
        Deadline: TimeSpan.FromSeconds(15));

    /// The evaluation judge keeps exactly the SDK defaults it ran under when
    /// its committed verdicts were produced: 100 s per attempt, three retries,
    /// no overall deadline. Stated rather than inherited, so a change to the
    /// SDK's defaults cannot move the judge underneath its own results.
    public static AiCallLimits Judge { get; } = new(
        AttemptTimeout: TimeSpan.FromSeconds(100),
        MaxRetries: 3,
        Deadline: null);
}
