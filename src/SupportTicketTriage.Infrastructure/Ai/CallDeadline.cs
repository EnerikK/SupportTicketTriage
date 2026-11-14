namespace SupportTicketTriage.Infrastructure.Ai;

/// The rule both deadline decorators share: an operation cancelled by anything
/// other than the caller is a timeout, and is reported as one.
///
/// This is load-bearing, not cosmetic. Every AI service catches
/// <c>Exception when ex is not OperationCanceledException</c>, so that a
/// caller's cancellation propagates while a model failure degrades to "not
/// classified" or "not embedded". A deadline that surfaced as cancellation
/// would slip past that filter and turn a slow model into a failed ingest -
/// the outage ADR-003 exists to prevent. The SDK's own per-attempt network
/// timeout surfaces as cancellation too, so it is translated here as well.
internal static class CallDeadline
{
    public static async Task<T> RunAsync<T>(
        TimeSpan deadline,
        Func<CancellationToken, Task<T>> call,
        CancellationToken callerToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(callerToken);
        linked.CancelAfter(deadline);

        try
        {
            return await call(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (!callerToken.IsCancellationRequested)
        {
            throw Timeout(deadline, ex);
        }
    }

    public static TimeoutException Timeout(TimeSpan deadline, Exception inner) =>
        new($"The model call did not complete within its {deadline.TotalSeconds:0.#} s deadline.", inner);
}
