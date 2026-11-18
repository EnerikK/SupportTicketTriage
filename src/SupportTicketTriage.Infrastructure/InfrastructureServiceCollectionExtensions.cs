using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SupportTicketTriage.Application.Redaction;
using SupportTicketTriage.Application.Routing;
using SupportTicketTriage.Infrastructure.Ai;
using SupportTicketTriage.Infrastructure.Dashboard;
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
            ChatTemperature = AzureOpenAiOptions.ParseTemperature(section["ChatTemperature"]),
        };

        // Every contradiction is collected before any is reported, so a
        // misconfigured deployment takes one restart to fix rather than one
        // per mistake.
        var problems = azureOpenAi.Validate().ToList();
        var routingThresholds = ReadRoutingThresholds(configuration, problems);

        if (problems.Count > 0)
        {
            throw new InvalidOperationException(
                "The configuration is invalid, so the API will not start:"
                + string.Concat(problems.Select(problem => $"{Environment.NewLine} - {problem}")));
        }

        // The startup probe and the health check read both (ADR-044).
        services.AddSingleton(azureOpenAi);
        services.AddSingleton(new AzureOpenAiStatus(azureOpenAi));


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

        services.AddSingleton(routingThresholds);

        services.AddScoped<TicketRetrievalService>();
        services.AddScoped<DashboardService>();
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
            azureOpenAi.ChatTemperature,
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
            azureOpenAi.ChatTemperature,
            sp.GetRequiredService<PiiRedactor>(),
            sp.GetRequiredService<SupportTicketTriageDbContext>(),
            sp.GetRequiredService<TicketRetrievalService>(),
            sp.GetRequiredService<ILogger<TicketDraftService>>()));

        return services;
    }

    /// Parsed with the invariant culture on purpose. Configuration values are
    /// text, and a machine whose locale uses a comma as the decimal separator
    /// would otherwise read "0.70" as 70 and gate away every ticket.
    ///
    /// An absent floor keeps its default. A floor that is present but unreadable
    /// or outside [0, 1] is a problem, not a fallback: falling back used to turn
    /// "0,7" into the default without a word, and a value like 70 was accepted
    /// and refused every ticket (ADR-044).
    private static RoutingThresholds ReadRoutingThresholds(IConfiguration configuration, List<string> problems)
    {
        var section = configuration.GetSection("Routing");

        return new RoutingThresholds(
            Read("MinimumClassificationScore", RoutingThresholds.DefaultMinimumClassificationScore),
            Read("MinimumSimilarity", RoutingThresholds.DefaultMinimumSimilarity));

        double Read(string key, double fallback)
        {
            var configured = section[key];
            if (string.IsNullOrWhiteSpace(configured))
            {
                return fallback;
            }

            if (double.TryParse(
                    configured,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var value)
                && value is >= 0 and <= 1)
            {
                return value;
            }

            problems.Add($"Routing:{key} must be a number from 0 to 1, written with a '.' decimal point. Got '{configured}'.");
            return fallback;
        }
    }

    public static void ApplyMigrations(this IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<SupportTicketTriageDbContext>();
        dbContext.Database.Migrate();
    }
}
