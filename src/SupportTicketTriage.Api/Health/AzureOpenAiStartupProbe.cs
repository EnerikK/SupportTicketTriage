using Microsoft.Extensions.AI;
using SupportTicketTriage.Infrastructure.Ai;

namespace SupportTicketTriage.Api.Health;

/// Calls each configured Azure OpenAI deployment once at startup (ADR-044).
///
/// A background service rather than a step before `app.Run()`, because the
/// API must start, ingest and list with Azure unreachable. A probe that held
/// up startup would turn an Azure outage into an API outage. The cost is a few
/// seconds after startup in which `/health` says "not yet checked".
///
/// It goes through the same clients the pipeline uses, with their retry and
/// deadline, so it tests what a ticket would actually meet.
public sealed class AzureOpenAiStartupProbe(
    AzureOpenAiStatus status,
    AzureOpenAiOptions options,
    IServiceProvider services,
    ILogger<AzureOpenAiStartupProbe> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (services.GetService<IEmbeddingGenerator<string, Embedding<float>>>() is { } generator)
        {
            await status.ProbeEmbeddingAsync(generator, stoppingToken);
        }

        if (services.GetService<IChatClient>() is { } chat)
        {
            await status.ProbeChatAsync(chat, options.ChatTemperature, stoppingToken);
        }

        Report("embedding", status.Embedding);
        Report("chat", status.Chat);
    }

    private void Report(string role, DeploymentStatus deployment)
    {
        switch (deployment.State)
        {
            case DeploymentState.Reachable:
                logger.LogInformation(
                    "Azure OpenAI {Role} deployment {Deployment} answered at startup.", role, deployment.Deployment);
                break;
            case DeploymentState.Unreachable:
                logger.LogError(
                    "Azure OpenAI {Role} deployment {Deployment} did not answer at startup: {Detail}",
                    role, deployment.Deployment, deployment.Detail);
                break;
            case DeploymentState.NoCredentials:
                logger.LogWarning(
                    "Azure OpenAI {Role} deployment {Deployment} is named, but no endpoint and key are set, so no client was built for it.",
                    role, deployment.Deployment);
                break;
            case DeploymentState.NotConfigured:
                logger.LogInformation("Azure OpenAI {Role} deployment is not configured; running without it.", role);
                break;
        }
    }
}
