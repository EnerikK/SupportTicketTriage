using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SupportTicketTriage.Application.Routing;
using SupportTicketTriage.Infrastructure;

namespace SupportTicketTriage.UnitTests;

/// ADR-044. Contradictory settings stop startup with every problem listed;
/// absent AI is still a valid way to run.
public class ConfigurationValidationTests
{
    private const string Endpoint = "https://example.openai.azure.com/";
    private const string Key = "a-key-that-must-never-appear-in-a-message";

    private static ServiceProvider Start(params (string Key, string? Value)[] settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value)))
            .Build();

        return new ServiceCollection()
            .AddInfrastructure("Host=unused", configuration)
            .BuildServiceProvider();
    }

    private static string Rejection(params (string Key, string? Value)[] settings) =>
        Assert.Throws<InvalidOperationException>(() => Start(settings)).Message;

    [Fact]
    public void NoAiAtAll_IsAValidWayToRun()
    {
        using var provider = Start();

        Assert.Equal(RoutingThresholds.Default, provider.GetRequiredService<RoutingThresholds>());
    }

    [Fact]
    public void AFullyConfiguredDeployment_Starts()
    {
        using var provider = Start(
            ("AzureOpenAi:Endpoint", Endpoint),
            ("AzureOpenAi:ApiKey", Key),
            ("AzureOpenAi:ChatDeployment", "chat"),
            ("AzureOpenAi:EmbeddingDeployment", "embedding"));
    }

    /// The integration tests name a deployment with no credentials and inject
    /// a fake model. That stays legal; `/health` reports it instead.
    [Fact]
    public void ADeploymentWithNoCredentials_IsReportedNotRejected()
    {
        using var provider = Start(("AzureOpenAi:ChatDeployment", "chat"));
    }

    [Theory]
    [InlineData(Endpoint, null, "AzureOpenAi:ApiKey is not")]
    [InlineData(null, Key, "AzureOpenAi:Endpoint is not")]
    public void HalfTheCredentials_IsRejected(string? endpoint, string? key, string expected)
    {
        var message = Rejection(
            ("AzureOpenAi:Endpoint", endpoint),
            ("AzureOpenAi:ApiKey", key),
            ("AzureOpenAi:ChatDeployment", "chat"));

        Assert.Contains(expected, message);
    }

    [Theory]
    [InlineData("http://example.openai.azure.com/")]
    [InlineData("htps://example.openai.azure.com/")]
    [InlineData("example.openai.azure.com")]
    public void AnEndpointThatIsNotHttps_IsRejected(string endpoint)
    {
        var message = Rejection(
            ("AzureOpenAi:Endpoint", endpoint),
            ("AzureOpenAi:ApiKey", Key),
            ("AzureOpenAi:ChatDeployment", "chat"));

        Assert.Contains("AzureOpenAi:Endpoint is not an absolute https:// URL", message);
    }

    [Fact]
    public void CredentialsThatNothingUses_AreRejected()
    {
        var message = Rejection(("AzureOpenAi:Endpoint", Endpoint), ("AzureOpenAi:ApiKey", Key));

        Assert.Contains("neither AzureOpenAi:EmbeddingDeployment nor AzureOpenAi:ChatDeployment", message);
    }

    /// The two mistakes that put a secret where a log would print it: the key
    /// pasted into the endpoint field, and the endpoint into the key field.
    [Fact]
    public void NeitherSwappedFieldIsEchoed()
    {
        var keyInEndpoint = Rejection(
            ("AzureOpenAi:Endpoint", Key),
            ("AzureOpenAi:ApiKey", Key),
            ("AzureOpenAi:ChatDeployment", "chat"));
        var urlInKey = Rejection(
            ("AzureOpenAi:Endpoint", Endpoint),
            ("AzureOpenAi:ApiKey", Endpoint + Key),
            ("AzureOpenAi:ChatDeployment", "chat"));

        Assert.DoesNotContain(Key, keyInEndpoint);
        Assert.DoesNotContain(Key, urlInKey);
        Assert.Contains("AzureOpenAi:ApiKey is a URL", urlInKey);
    }

    [Theory]
    [InlineData("0,7")]
    [InlineData("seventy")]
    [InlineData("70")]
    [InlineData("-0.1")]
    [InlineData("NaN")]
    public void AnUnusableRoutingFloor_IsRejectedRatherThanDefaulted(string configured)
    {
        var message = Rejection(("Routing:MinimumSimilarity", configured));

        Assert.Contains("Routing:MinimumSimilarity must be a number from 0 to 1", message);
    }

    [Fact]
    public void AReadableRoutingFloor_IsUsed()
    {
        using var provider = Start(("Routing:MinimumSimilarity", "0.7"));

        Assert.Equal(0.7, provider.GetRequiredService<RoutingThresholds>().MinimumSimilarity);
    }

    /// One restart per misconfiguration, not one per mistake.
    [Fact]
    public void EveryProblemIsReportedAtOnce()
    {
        var message = Rejection(
            ("AzureOpenAi:Endpoint", Endpoint),
            ("AzureOpenAi:ChatDeployment", "chat"),
            ("Routing:MinimumClassificationScore", "0,5"));

        Assert.Contains("AzureOpenAi:ApiKey is not", message);
        Assert.Contains("Routing:MinimumClassificationScore", message);
    }
}
