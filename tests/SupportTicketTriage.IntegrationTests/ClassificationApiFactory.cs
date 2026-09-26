using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace SupportTicketTriage.IntegrationTests;

/// An API configured as if a chat deployment exists, with the model boundary
/// faked. Kept separate from <see cref="EmbeddingApiFactory"/> so each factory
/// configures exactly one AI capability and a test cannot pass because the
/// other one happened to be wired up.
public sealed class ClassificationApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("pgvector/pgvector:pg16").Build();

    public FakeChatClient ChatClient { get; } = new();

    public const string ChatDeployment = "test-chat-model";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Default", _postgres.GetConnectionString());

        // Names the deployment without endpoint or key, so the real client is
        // never constructed and the fake below is what gets resolved.
        builder.UseSetting("AzureOpenAi:ChatDeployment", ChatDeployment);

        builder.ConfigureServices(services => services.AddSingleton<IChatClient>(ChatClient));
    }

    public Task InitializeAsync() => _postgres.StartAsync();

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _postgres.DisposeAsync();
        await base.DisposeAsync();
    }
}

[CollectionDefinition(nameof(ClassificationApiCollection))]
public sealed class ClassificationApiCollection : ICollectionFixture<ClassificationApiFactory>;
