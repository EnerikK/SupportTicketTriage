using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace SupportTicketTriage.Infrastructure.Ai;

/// Bounds a chat call, every retry included, and reports overrunning it as a
/// <see cref="TimeoutException"/>. See <see cref="CallDeadline"/> for why the
/// exception type matters.
public sealed class DeadlineChatClient(IChatClient inner, TimeSpan deadline) : DelegatingChatClient(inner)
{
    public TimeSpan Deadline { get; } = deadline;

    public override Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        CallDeadline.RunAsync(
            Deadline,
            ct => base.GetResponseAsync(messages, options, ct),
            cancellationToken);

    /// Nothing streams today. Implemented rather than left to the base class so
    /// that a streaming call added later is not silently unbounded. The
    /// deadline covers the whole stream, not the time to first token.
    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(Deadline);

        await using var updates = base
            .GetStreamingResponseAsync(messages, options, linked.Token)
            .GetAsyncEnumerator(linked.Token);

        while (true)
        {
            // C# forbids yield inside a try with a catch, so the enumerator is
            // advanced by hand and only the advance is guarded.
            bool hasNext;
            try
            {
                hasNext = await updates.MoveNextAsync().ConfigureAwait(false);
            }
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                throw CallDeadline.Timeout(Deadline, ex);
            }

            if (!hasNext)
            {
                yield break;
            }

            yield return updates.Current;
        }
    }
}
