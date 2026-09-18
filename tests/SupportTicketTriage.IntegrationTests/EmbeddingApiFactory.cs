using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace SupportTicketTriage.IntegrationTests;

/// An API configured as if an embedding deployment exists, with the model
/// boundary faked. Separate from <see cref="TicketApiFactory"/>, which
/// deliberately has no AI configuration at all.
public sealed class EmbeddingApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("pgvector/pgvector:pg16").Build();

    public FakeEmbeddingGenerator Generator { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Default", _postgres.GetConnectionString());

        // Names the deployment without endpoint or key, so the real client is
        // never constructed and the fake below is what gets resolved.
        builder.UseSetting("AzureOpenAi:EmbeddingDeployment", "test-embedding-model");

        builder.ConfigureServices(services =>
            services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(Generator));
    }

    public Task InitializeAsync() => _postgres.StartAsync();

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _postgres.DisposeAsync();
        await base.DisposeAsync();
    }
}

[CollectionDefinition(nameof(EmbeddingApiCollection))]
public sealed class EmbeddingApiCollection : ICollectionFixture<EmbeddingApiFactory>;
