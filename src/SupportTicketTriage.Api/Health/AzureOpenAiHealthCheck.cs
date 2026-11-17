using Microsoft.Extensions.Diagnostics.HealthChecks;
using SupportTicketTriage.Infrastructure.Ai;

namespace SupportTicketTriage.Api.Health;

/// Reports what the startup probe found (ADR-044).
///
/// Never Unhealthy. The specification requires the API to stay healthy when
/// Azure OpenAI is unreachable, because tickets still ingest and list and
/// reviewers still read what is stored. Unhealthy means HTTP 503, and an
/// orchestrator restarting a container cannot fix a deployment in Azure.
/// Degraded is still HTTP 200, and carries the reason.
public sealed class AzureOpenAiHealthCheck(AzureOpenAiStatus status) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var embedding = status.Embedding;
        var chat = status.Chat;

        var data = new Dictionary<string, object>
        {
            ["embedding"] = Describe(embedding),
            ["chat"] = Describe(chat),
        };

        return Task.FromResult(
            embedding.State == DeploymentState.Reachable && chat.State == DeploymentState.Reachable
                ? HealthCheckResult.Healthy("Both deployments answered at startup.", data)
                : HealthCheckResult.Degraded(
                    "AI is partly or wholly unavailable. Tickets still ingest, list and can be reviewed.",
                    data: data));
    }

    private static string Describe(DeploymentStatus deployment) => deployment.State switch
    {
        DeploymentState.NotConfigured => "not configured",
        DeploymentState.NoCredentials => $"'{deployment.Deployment}' is named, but no endpoint and key are set",
        DeploymentState.Unchecked => $"'{deployment.Deployment}' not yet checked",
        DeploymentState.Reachable => $"'{deployment.Deployment}' answered at startup",
        DeploymentState.Unreachable => $"'{deployment.Deployment}' did not answer at startup: {deployment.Detail}",
        _ => throw new ArgumentOutOfRangeException(nameof(deployment)),
    };
}
