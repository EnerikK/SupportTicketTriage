using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SupportTicketTriage.Application.Redaction;
using SupportTicketTriage.Application.Routing;
using SupportTicketTriage.Infrastructure.Ai;
using SupportTicketTriage.Infrastructure.Persistence;
using SupportTicketTriage.Infrastructure.Retrieval;
using SupportTicketTriage.Infrastructure.Review;
using SupportTicketTriage.Infrastructure.Routing;

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
            ChatDeployment = section["ChatDeployment"],
        };

        // The stack must still start when Azure OpenAI is unavailable, so each
        // client is only registered when its own deployment is configured. The
        // two are independent: retrieval works with embeddings alone.
        var embeddingGenerator = AzureOpenAiEmbeddingFactory.Create(azureOpenAi);
        if (embeddingGenerator is not null)
        {
            services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(embeddingGenerator);
        }

        var chatClient = AzureOpenAiChatFactory.Create(azureOpenAi);
        if (chatClient is not null)
        {
            services.AddSingleton(chatClient);
        }

        services.AddSingleton(ReadRoutingThresholds(configuration));

        services.AddScoped<TicketRetrievalService>();
        services.AddScoped<TicketRoutingService>();

        services.AddScoped(sp => new TicketEmbeddingService(
            sp.GetService<IEmbeddingGenerator<string, Embedding<float>>>(),
            azureOpenAi.EmbeddingDeployment,
            sp.GetRequiredService<PiiRedactor>(),
            sp.GetRequiredService<SupportTicketTriageDbContext>(),
            sp.GetRequiredService<ILogger<TicketEmbeddingService>>()));

        services.AddScoped(sp => new TicketClassificationService(
            sp.GetService<IChatClient>(),
            azureOpenAi.ChatDeployment,
            sp.GetRequiredService<PiiRedactor>(),
            sp.GetRequiredService<SupportTicketTriageDbContext>(),
            sp.GetRequiredService<ILogger<TicketClassificationService>>()));

        services.AddScoped(sp => new TicketReviewService(
            sp.GetRequiredService<SupportTicketTriageDbContext>(),
            sp.GetRequiredService<TicketEmbeddingService>(),
            azureOpenAi.EmbeddingDeployment,
            sp.GetRequiredService<ILogger<TicketReviewService>>()));

        services.AddScoped(sp => new TicketDraftService(
            sp.GetService<IChatClient>(),
            azureOpenAi.ChatDeployment,
            sp.GetRequiredService<PiiRedactor>(),
            sp.GetRequiredService<SupportTicketTriageDbContext>(),
            sp.GetRequiredService<TicketRetrievalService>(),
            sp.GetRequiredService<ILogger<TicketDraftService>>()));

        return services;
    }

    /// Parsed with the invariant culture on purpose. Configuration values are
    /// text, and a machine whose locale uses a comma as the decimal separator
    /// would otherwise read "0.70" as 70 and gate away every ticket.
    private static RoutingThresholds ReadRoutingThresholds(IConfiguration configuration)
    {
        var section = configuration.GetSection("Routing");

        return new RoutingThresholds(
            Read("MinimumClassificationScore", RoutingThresholds.DefaultMinimumClassificationScore),
            Read("MinimumSimilarity", RoutingThresholds.DefaultMinimumSimilarity));

        double Read(string key, double fallback) =>
            double.TryParse(
                section[key],
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out var value)
                ? value
                : fallback;
    }

    public static void ApplyMigrations(this IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<SupportTicketTriageDbContext>();
        dbContext.Database.Migrate();
    }
}
