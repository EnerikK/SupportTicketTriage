using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace SupportTicketTriage.IntegrationTests;

/// ADR-044, through the real host: what `/health` says, and that it keeps
/// saying it with HTTP 200 when AI is unavailable.
[Collection(nameof(TicketApiCollection))]
public class HealthEndpointTests(TicketApiFactory factory)
{
    [Fact]
    public async Task WithNoAiConfigured_TheApiIsDegradedButStillAnswers200()
    {
        var response = await factory.CreateClient().GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Degraded", body.RootElement.GetProperty("status").GetString());
        Assert.Equal("not configured", AzureOpenAiData(body, "chat"));
        Assert.Equal("not configured", AzureOpenAiData(body, "embedding"));
    }

    internal static string? AzureOpenAiData(JsonDocument body, string deployment) =>
        body.RootElement
            .GetProperty("checks")
            .GetProperty("azure-openai")
            .GetProperty("data")
            .GetProperty(deployment)
            .GetString();
}

/// The 2026-10-08 incident, end to end: a chat deployment that does not answer
/// starts cleanly, and the startup probe names it in `/health`.
public class UnreachableDeploymentHealthTests(UnreachableChatApiFactory factory)
    : IClassFixture<UnreachableChatApiFactory>
{
    [Fact]
    public async Task ADeploymentThatDoesNotAnswer_IsNamedInTheHealthResponse()
    {
        var client = factory.CreateClient();

        // The probe runs in the background after startup, so poll briefly
        // rather than assume it has finished by the first request.
        string? chat = null;
        var giveUpAt = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < giveUpAt)
        {
            var response = await client.GetAsync("/health");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            chat = HealthEndpointTests.AzureOpenAiData(body, "chat");
            if (chat?.Contains("did not answer") == true)
            {
                break;
            }

            await Task.Delay(100);
        }

        Assert.Equal(
            "'misspelt-deployment' did not answer at startup: InvalidOperationException: Simulated Azure OpenAI failure.",
            chat);
    }
}

public sealed class UnreachableChatApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("pgvector/pgvector:pg16").Build();

    private readonly FakeChatClient _chatClient = new() { ShouldThrow = true };

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Default", _postgres.GetConnectionString());

        // Credentials make the deployment count as configured, so the probe
        // runs. No request reaches the endpoint: the fake registered below is
        // the client every consumer, the probe included, resolves.
        builder.UseSetting("AzureOpenAi:Endpoint", "https://example.invalid/");
        builder.UseSetting("AzureOpenAi:ApiKey", "not-a-key");
        builder.UseSetting("AzureOpenAi:ChatDeployment", "misspelt-deployment");

        builder.ConfigureServices(services => services.AddSingleton<IChatClient>(_chatClient));
    }

    public Task InitializeAsync() => _postgres.StartAsync();

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _postgres.DisposeAsync();
        await base.DisposeAsync();
    }
}
