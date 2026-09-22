using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SupportTicketTriage.Application.Redaction;
using SupportTicketTriage.Infrastructure.Ai;
using SupportTicketTriage.Infrastructure.Persistence;
using SupportTicketTriage.Infrastructure.Retrieval;

namespace SupportTicketTriage.Infrastructure;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        string connectionString,
        IConfiguration configuration)
    {
        services.AddDbContext<SupportTicketTriageDbContext>(
            options => options.UseNpgsql(connectionString, npgsql => npgsql.UseVector()));

        services.AddSingleton<PiiRedactor>();

        var section = configuration.GetSection(AzureOpenAiOptions.SectionName);
        var azureOpenAi = new AzureOpenAiOptions
        {
            Endpoint = section["Endpoint"],
            ApiKey = section["ApiKey"],
            EmbeddingDeployment = section["EmbeddingDeployment"],
        };

        // The stack must still start when Azure OpenAI is unavailable, so the
        // embedding generator is only registered when it is fully configured.
        var embeddingGenerator = AzureOpenAiEmbeddingFactory.Create(azureOpenAi);
        if (embeddingGenerator is not null)
        {
            services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(embeddingGenerator);
        }

        services.AddScoped<TicketRetrievalService>();

        services.AddScoped(sp => new TicketEmbeddingService(
            sp.GetService<IEmbeddingGenerator<string, Embedding<float>>>(),
            azureOpenAi.EmbeddingDeployment,
            sp.GetRequiredService<PiiRedactor>(),
            sp.GetRequiredService<SupportTicketTriageDbContext>(),
            sp.GetRequiredService<ILogger<TicketEmbeddingService>>()));

        return services;
    }

    public static void ApplyMigrations(this IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<SupportTicketTriageDbContext>();
        dbContext.Database.Migrate();
    }
}
