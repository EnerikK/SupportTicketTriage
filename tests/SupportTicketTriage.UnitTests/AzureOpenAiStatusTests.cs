using Microsoft.Extensions.AI;
using SupportTicketTriage.Infrastructure.Ai;

namespace SupportTicketTriage.UnitTests;

/// ADR-044. The startup probe's verdict on each deployment, which `/health`
/// reports.
public class AzureOpenAiStatusTests
{
    private sealed class RecordingChatClient(Exception? failure = null) : IChatClient
    {
        public List<ChatOptions?> Calls { get; } = [];

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Calls.Add(options);
            cancellationToken.ThrowIfCancellationRequested();
            return failure is null
                ? Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, """{"ok": true}""")))
                : Task.FromException<ChatResponse>(failure);
        }

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

    private sealed class StubEmbeddingGenerator(Exception? failure = null)
        : IEmbeddingGenerator<string, Embedding<float>>
    {
        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values,
            EmbeddingGenerationOptions? options = null,
            CancellationToken cancellationToken = default) =>
            failure is null
                ? Task.FromResult(new GeneratedEmbeddings<Embedding<float>>([new Embedding<float>(new float[] { 1f })]))
                : Task.FromException<GeneratedEmbeddings<Embedding<float>>>(failure);

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private static AzureOpenAiStatus Configured(float? temperature = 0f) => new(new AzureOpenAiOptions
    {
        Endpoint = "https://example.invalid/",
        ApiKey = "not-a-key",
        ChatDeployment = "chat",
        EmbeddingDeployment = "embedding",
        ChatTemperature = temperature,
    });

    [Fact]
    public void BeforeAnyProbe_TheStateComesFromConfigurationAlone()
    {
        var none = new AzureOpenAiStatus(new AzureOpenAiOptions());
        var named = new AzureOpenAiStatus(new AzureOpenAiOptions { ChatDeployment = "chat" });

        Assert.Equal(DeploymentState.NotConfigured, none.Chat.State);
        Assert.Equal(DeploymentState.NoCredentials, named.Chat.State);
        Assert.Equal(DeploymentState.Unchecked, Configured().Chat.State);
    }

    [Fact]
    public async Task ADeploymentThatAnswers_IsReachable()
    {
        var status = Configured();

        await status.ProbeChatAsync(new RecordingChatClient(), 0f, CancellationToken.None);
        await status.ProbeEmbeddingAsync(new StubEmbeddingGenerator(), CancellationToken.None);

        Assert.Equal(new DeploymentStatus("chat", DeploymentState.Reachable), status.Chat);
        Assert.Equal(new DeploymentStatus("embedding", DeploymentState.Reachable), status.Embedding);
    }

    /// The 2026-10-08 incident: a misspelt deployment started cleanly. The
    /// provider's reason is what makes the health response worth reading.
    [Fact]
    public async Task ADeploymentThatDoesNotExist_IsUnreachable_WithTheProvidersReason()
    {
        var status = Configured();
        var missing = new InvalidOperationException(
            "HTTP 404 (DeploymentNotFound)\nThe API deployment for this resource does not exist.");

        await status.ProbeChatAsync(new RecordingChatClient(missing), 0f, CancellationToken.None);

        Assert.Equal(DeploymentState.Unreachable, status.Chat.State);
        Assert.Equal("InvalidOperationException: HTTP 404 (DeploymentNotFound)", status.Chat.Detail);
    }

    [Fact]
    public async Task AnEmbeddingDeploymentThatFails_IsUnreachable()
    {
        var status = Configured();

        await status.ProbeEmbeddingAsync(
            new StubEmbeddingGenerator(new TimeoutException("No answer within 15 s.")),
            CancellationToken.None);

        Assert.Equal(DeploymentState.Unreachable, status.Embedding.State);
        Assert.Contains("TimeoutException", status.Embedding.Detail);
    }

    /// The probe sends what a ticket would send, so a temperature the model
    /// rejects fails here and not on the first ticket.
    [Theory]
    [InlineData(null)]
    [InlineData(0f)]
    public async Task TheProbeSendsTheOptionsARealCallSends(float? temperature)
    {
        var client = new RecordingChatClient();

        await Configured(temperature).ProbeChatAsync(client, temperature, CancellationToken.None);

        var sent = Assert.Single(client.Calls);
        Assert.Equal(temperature, sent!.Temperature);
        Assert.Equal(ChatResponseFormat.Json, sent.ResponseFormat);
    }

    /// Nothing to probe without a client built from configuration. In the
    /// integration tests this is what keeps the probe away from their fakes.
    [Fact]
    public async Task ADeploymentWithNoCredentials_IsNeverCalled()
    {
        var status = new AzureOpenAiStatus(new AzureOpenAiOptions { ChatDeployment = "chat" });
        var client = new RecordingChatClient();

        await status.ProbeChatAsync(client, 0f, CancellationToken.None);

        Assert.Empty(client.Calls);
        Assert.Equal(DeploymentState.NoCredentials, status.Chat.State);
    }

    /// A probe abandoned at shutdown learned nothing about the deployment.
    [Fact]
    public async Task ShutdownDuringTheProbe_IsNotRecordedAsAFailure()
    {
        var status = Configured();
        using var stopping = new CancellationTokenSource();
        await stopping.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => status.ProbeChatAsync(new RecordingChatClient(), 0f, stopping.Token));

        Assert.Equal(DeploymentState.Unchecked, status.Chat.State);
    }
}
