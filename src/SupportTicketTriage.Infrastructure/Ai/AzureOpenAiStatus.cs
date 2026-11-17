using Microsoft.Extensions.AI;

namespace SupportTicketTriage.Infrastructure.Ai;

public enum DeploymentState
{
    /// Not named in configuration: the deliberate no-AI mode.
    NotConfigured,

    /// Named, but with no endpoint and key, so no client was built from
    /// configuration and the stage it serves is off.
    NoCredentials,

    /// Configured; the startup probe has not finished.
    Unchecked,

    Reachable,
    Unreachable,
}

public sealed record DeploymentStatus(string? Deployment, DeploymentState State, string? Detail = null);

/// Whether each Azure OpenAI deployment is usable, as far as startup can tell
/// (ADR-044).
///
/// Validation can check that the settings agree with each other. It cannot
/// tell that a deployment name exists, which is the mistake that actually
/// happened: a misspelt chat deployment started cleanly and failed only on
/// the first ticket. So each configured deployment is called once, and the
/// answer is kept here for `/health`.
///
/// One call, at startup. A deployment that later stops answering is not
/// reflected here; that needs the per-call outcomes Phase 6's observability
/// work will record.
public sealed class AzureOpenAiStatus
{
    // Replaced whole by the probe and read by health checks on other threads;
    // volatile so a reader never sees a stale reference.
    private volatile DeploymentStatus _embedding;
    private volatile DeploymentStatus _chat;

    public AzureOpenAiStatus(AzureOpenAiOptions options)
    {
        _embedding = Initial(options.EmbeddingDeployment, options.IsEmbeddingConfigured);
        _chat = Initial(options.ChatDeployment, options.IsChatConfigured);
    }

    public DeploymentStatus Embedding => _embedding;
    public DeploymentStatus Chat => _chat;

    public async Task ProbeEmbeddingAsync(
        IEmbeddingGenerator<string, Embedding<float>> generator,
        CancellationToken ct)
    {
        if (_embedding.State != DeploymentState.Unchecked)
        {
            return;
        }

        _embedding = await ProbeAsync(
            _embedding.Deployment,
            () => generator.GenerateAsync(["probe"], cancellationToken: ct),
            ct);
    }

    /// Sends the options a real call sends, the configured temperature and a
    /// JSON response format, so a setting the model rejects fails here rather
    /// than on the first ticket. A temperature of 0 sent to a model that
    /// accepts only the default is the case this was written for.
    public async Task ProbeChatAsync(IChatClient client, float? temperature, CancellationToken ct)
    {
        if (_chat.State != DeploymentState.Unchecked)
        {
            return;
        }

        _chat = await ProbeAsync(
            _chat.Deployment,
            () => client.GetResponseAsync(
                [new ChatMessage(ChatRole.User, """Reply with the JSON object {"ok": true}.""")],
                JsonChatOptions.For(temperature),
                ct),
            ct);
    }

    private static DeploymentStatus Initial(string? deployment, bool configured) =>
        string.IsNullOrWhiteSpace(deployment) ? new DeploymentStatus(null, DeploymentState.NotConfigured)
        : configured ? new DeploymentStatus(deployment, DeploymentState.Unchecked)
        : new DeploymentStatus(deployment, DeploymentState.NoCredentials);

    /// Any failure counts, including a timeout, except the host shutting down:
    /// a probe abandoned at shutdown has learned nothing about the deployment.
    private static async Task<DeploymentStatus> ProbeAsync(
        string? deployment,
        Func<Task> call,
        CancellationToken ct)
    {
        try
        {
            await call();
            return new DeploymentStatus(deployment, DeploymentState.Reachable);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            return new DeploymentStatus(deployment, DeploymentState.Unreachable, Describe(ex));
        }
    }

    /// The provider's own message carries the useful part, such as
    /// `DeploymentNotFound`. Its first line only, capped, because it ends up in
    /// a health response.
    private static string Describe(Exception ex)
    {
        var firstLine = ex.Message.Split('\n', 2)[0].Trim();
        const int limit = 300;
        if (firstLine.Length > limit)
        {
            firstLine = firstLine[..limit] + "…";
        }

        return $"{ex.GetType().Name}: {firstLine}";
    }
}
