using Microsoft.Extensions.AI;
using SupportTicketTriage.Eval.Instrumentation;

namespace SupportTicketTriage.UnitTests;

/// The decorators are the whole mechanism by which slice 5b gets token counts
/// without touching production code, so what they capture is worth pinning.
public class AiCallRecordingTests
{
    private const int Dimensions = 4;

    /// Returns a fixed response, optionally with usage, or throws.
    ///
    /// The fakes already in the repo deliberately set no usage, which is the
    /// right default for them - they model a deployment that reports none. This
    /// one sets it, because a decorator that reads usage cannot be tested by a
    /// fake that never provides any.
    private sealed class StubChatClient(UsageDetails? usage, Exception? throws = null) : IChatClient
    {
        public int Calls { get; private set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Calls++;

            if (throws is not null)
            {
                throw throws;
            }

            return Task.FromResult(
                new ChatResponse(new ChatMessage(ChatRole.Assistant, "{}")) { Usage = usage });
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The pipeline never streams.");

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class StubEmbeddingGenerator(UsageDetails? usage)
        : IEmbeddingGenerator<string, Embedding<float>>
    {
        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values,
            EmbeddingGenerationOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(
                new GeneratedEmbeddings<Embedding<float>>(
                    values.Select(_ => new Embedding<float>(new float[Dimensions])))
                {
                    Usage = usage,
                });

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private static async Task<AiCall> SingleChatCallAsync(UsageDetails? usage, AiStage stage)
    {
        var recorder = new AiCallRecorder();
        var client = new RecordingChatClient(new StubChatClient(usage), stage, recorder);

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hello")]);

        return Assert.Single(recorder.Calls);
    }

    [Fact]
    public async Task AChatCallRecordsTheTokensTheProviderReported()
    {
        var call = await SingleChatCallAsync(
            new UsageDetails { InputTokenCount = 412, OutputTokenCount = 37 },
            AiStage.Classification);

        Assert.Equal(AiStage.Classification, call.Stage);
        Assert.Equal(412, call.InputTokens);
        Assert.Equal(37, call.OutputTokens);
        Assert.True(call.Succeeded);
    }

    /// **The central claim of this slice, and the reason the instrumentation is
    /// a decorator rather than a change to the services.**
    ///
    /// `TicketEmbeddingService` calls `GenerateVectorAsync`, an extension method
    /// that calls `GenerateAsync`, takes `[0].Vector`, and discards the
    /// `GeneratedEmbeddings` - usage included. Nothing at or above the service
    /// can recover that. A decorator underneath it still sees the collection,
    /// and this test drives the real extension method to prove it, rather than
    /// calling `GenerateAsync` directly and proving something easier.
    [Fact]
    public async Task EmbeddingUsageSurvivesTheExtensionMethodThatDiscardsIt()
    {
        var recorder = new AiCallRecorder();
        var generator = new RecordingEmbeddingGenerator(
            new StubEmbeddingGenerator(new UsageDetails { InputTokenCount = 19 }),
            recorder);

        var vector = await generator.GenerateVectorAsync("some ticket text");

        Assert.Equal(Dimensions, vector.Length);

        var call = Assert.Single(recorder.Calls);
        Assert.Equal(AiStage.Embedding, call.Stage);
        Assert.Equal(19, call.InputTokens);

        // Not zero. An embedding returns a vector, so "how many tokens did it
        // write" has no answer rather than the answer nothing, and a zero here
        // would be priced as real output at the output rate.
        Assert.Null(call.OutputTokens);
    }

    /// The ordinary offline case: the lexical stand-in and the repo's chat
    /// fakes report no usage at all. Tokens must come back null so the cost
    /// metric refuses to price the run, rather than zero so it prices it free.
    [Fact]
    public async Task AProviderThatReportsNoUsageYieldsNullTokensRatherThanZero()
    {
        var call = await SingleChatCallAsync(usage: null, AiStage.Generation);

        Assert.Null(call.InputTokens);
        Assert.Null(call.OutputTokens);
        Assert.True(call.Succeeded);
    }

    /// A failed call still spent wall-clock time, and the services above swallow
    /// the exception - so without the decorator recording it, that time would
    /// vanish from the run entirely.
    [Fact]
    public async Task AFailedCallIsRecordedAndTheExceptionStillPropagates()
    {
        var recorder = new AiCallRecorder();
        var inner = new StubChatClient(usage: null, throws: new HttpRequestException("429"));
        var client = new RecordingChatClient(inner, AiStage.Generation, recorder);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => client.GetResponseAsync([new ChatMessage(ChatRole.User, "hello")]));

        var call = Assert.Single(recorder.Calls);
        Assert.False(call.Succeeded);
        Assert.Equal(AiStage.Generation, call.Stage);
        Assert.Null(call.InputTokens);
    }

    /// One IChatClient serves classification and generation, so the stage has to
    /// come from the wrapping rather than from the call. Blending them would
    /// hide the expensive stage behind 300 cheap ones.
    [Fact]
    public async Task ClassificationAndGenerationAreRecordedAsDifferentStages()
    {
        var recorder = new AiCallRecorder();
        var usage = new UsageDetails { InputTokenCount = 10, OutputTokenCount = 1 };
        var message = new[] { new ChatMessage(ChatRole.User, "hello") };

        await new RecordingChatClient(new StubChatClient(usage), AiStage.Classification, recorder)
            .GetResponseAsync(message);
        await new RecordingChatClient(new StubChatClient(usage), AiStage.Generation, recorder)
            .GetResponseAsync(message);

        Assert.Equal(
            [AiStage.Classification, AiStage.Generation],
            recorder.Calls.Select(c => c.Stage));
    }

    /// The decorator must not change what the caller receives, or every
    /// behavioural test in the suite would be exercising a different pipeline
    /// from the one that runs.
    [Fact]
    public async Task TheDecoratorPassesTheResponseThroughUnchanged()
    {
        var inner = new StubChatClient(new UsageDetails { InputTokenCount = 1 });
        var client = new RecordingChatClient(inner, AiStage.Classification, new AiCallRecorder());

        var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hello")]);

        Assert.Equal("{}", response.Text);
        Assert.Equal(1, inner.Calls);
    }

    [Fact]
    public void ARecorderWithNoCallsIsEmptyRatherThanNull()
    {
        Assert.Empty(new AiCallRecorder().Calls);
    }
}
