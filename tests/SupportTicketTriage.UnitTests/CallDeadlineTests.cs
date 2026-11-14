using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using SupportTicketTriage.Application.Redaction;
using SupportTicketTriage.Domain;
using SupportTicketTriage.Infrastructure.Ai;

namespace SupportTicketTriage.UnitTests;

/// ADR-039. The deadline's exception type is the contract: a timeout must look
/// like a model failure to the services, and a caller's cancellation must not.
public class CallDeadlineTests
{
    private static readonly TimeSpan Short = TimeSpan.FromMilliseconds(50);

    /// Waits until cancelled. Optionally ignores the token it is given and
    /// throws its own cancellation, the way the SDK reports a per-attempt
    /// network timeout without the caller having cancelled anything.
    private sealed class HangingChatClient : IChatClient
    {
        public async Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException("unreachable");
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant, "first");
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class SelfCancellingChatClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            throw new TaskCanceledException("The operation was cancelled because it exceeded the network timeout.");

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class HangingEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
    {
        public async Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values,
            EmbeddingGenerationOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException("unreachable");
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class FixedChatClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    [Fact]
    public async Task AFastCall_PassesThroughUntouched()
    {
        using var client = new DeadlineChatClient(new FixedChatClient(), Short);

        var response = await client.GetResponseAsync("hello");

        Assert.Equal("ok", response.Text);
    }

    [Fact]
    public async Task OverrunningTheDeadline_IsATimeout_NotACancellation()
    {
        using var client = new DeadlineChatClient(new HangingChatClient(), Short);

        var ex = await Assert.ThrowsAsync<TimeoutException>(() => client.GetResponseAsync("hello"));

        Assert.IsAssignableFrom<OperationCanceledException>(ex.InnerException);
    }

    [Fact]
    public async Task TheCallersCancellation_StillPropagatesAsCancellation()
    {
        using var client = new DeadlineChatClient(new HangingChatClient(), TimeSpan.FromMinutes(5));
        using var caller = new CancellationTokenSource(Short);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.GetResponseAsync("hello", cancellationToken: caller.Token));
    }

    /// The SDK's own per-attempt timeout surfaces as a TaskCanceledException
    /// the caller never asked for. Before this decorator existed it slipped
    /// past every service's `when (ex is not OperationCanceledException)`.
    [Fact]
    public async Task ACancellationTheCallerDidNotRequest_IsReportedAsATimeout()
    {
        using var client = new DeadlineChatClient(new SelfCancellingChatClient(), TimeSpan.FromMinutes(5));

        await Assert.ThrowsAsync<TimeoutException>(() => client.GetResponseAsync("hello"));
    }

    [Fact]
    public async Task AStreamThatStalls_TimesOutAfterWhatItAlreadyYielded()
    {
        using var client = new DeadlineChatClient(new HangingChatClient(), Short);
        var received = new List<string>();

        await Assert.ThrowsAsync<TimeoutException>(async () =>
        {
            await foreach (var update in client.GetStreamingResponseAsync("hello"))
            {
                received.Add(update.Text);
            }
        });

        Assert.Equal(["first"], received);
    }

    [Fact]
    public async Task AnEmbeddingThatOverruns_IsATimeout()
    {
        using var generator = new DeadlineEmbeddingGenerator(new HangingEmbeddingGenerator(), Short);

        await Assert.ThrowsAsync<TimeoutException>(() => generator.GenerateVectorAsync("hello"));
    }

    /// The end-to-end reason the exception type matters: the real service,
    /// given a model that never answers, reports "not classified" and lets
    /// ingest continue, instead of throwing. The database is never reached on
    /// this path, which is why it can be null here.
    [Fact]
    public async Task AHungClassification_DegradesToUnclassified_RatherThanFailingIngest()
    {
        using var client = new DeadlineChatClient(new HangingChatClient(), Short);
        var service = new TicketClassificationService(
            client,
            "test-deployment",
            AzureOpenAiOptions.DefaultChatTemperature,
            new PiiRedactor(),
            db: null!,
            NullLogger<TicketClassificationService>.Instance);

        var classified = await service.TryClassifyAsync(Ticket.Create("Subject", "Body"));

        Assert.False(classified);
    }

    private static readonly AzureOpenAiOptions Configured = new()
    {
        Endpoint = "https://example.invalid/",
        ApiKey = "not-a-key",
        ChatDeployment = "chat",
        EmbeddingDeployment = "embedding",
    };

    [Fact]
    public void TheApplicationsClients_CarryTheirDeadlines()
    {
        using var chat = AzureOpenAiChatFactory.Create(Configured)!;
        using var embedding = AzureOpenAiEmbeddingFactory.Create(Configured)!;

        Assert.Equal(AiCallLimits.Chat.Deadline, chat.GetService<DeadlineChatClient>()?.Deadline);
        Assert.Equal(AiCallLimits.Embedding.Deadline, embedding.GetService<DeadlineEmbeddingGenerator>()?.Deadline);
    }

    /// The judge keeps the limits its committed verdicts were produced under.
    [Fact]
    public void TheJudgesClient_HasNoOverallDeadline()
    {
        using var judge = AzureOpenAiChatFactory.Create(Configured, AiCallLimits.Judge)!;

        Assert.Null(judge.GetService<DeadlineChatClient>());
    }

    /// Every limit is positive, so a deadline cannot be "removed" by zeroing it
    /// - CancelAfter(0) would fail every call instantly rather than disable it.
    [Fact]
    public void EveryLimit_IsPositive()
    {
        foreach (var limits in new[] { AiCallLimits.Chat, AiCallLimits.Embedding, AiCallLimits.Judge })
        {
            Assert.True(limits.AttemptTimeout > TimeSpan.Zero);
            Assert.True(limits.MaxRetries >= 0);
            Assert.True(limits.Deadline is null || limits.Deadline > limits.AttemptTimeout);
        }
    }
}
