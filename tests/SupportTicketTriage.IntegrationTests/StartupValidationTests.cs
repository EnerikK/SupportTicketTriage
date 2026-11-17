using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace SupportTicketTriage.IntegrationTests;

/// Contradictory settings stop the API before it serves anything, and say why
/// without printing the key.
public class StartupValidationTests
{
    private const string Key = "a-key-that-must-never-appear-in-a-message";

    private sealed class HalfConfiguredApiFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("ConnectionStrings:Default", "Host=unused");
            builder.UseSetting("AzureOpenAi:ApiKey", Key);
            builder.UseSetting("AzureOpenAi:ChatDeployment", "chat");
        }
    }

    [Fact]
    public void HalfTheCredentials_StopsTheApiStarting_WithoutEchoingTheKey()
    {
        using var factory = new HalfConfiguredApiFactory();

        var failure = Assert.ThrowsAny<Exception>(() => factory.CreateClient()).ToString();

        Assert.Contains("AzureOpenAi:ApiKey is set but AzureOpenAi:Endpoint is not", failure);
        Assert.DoesNotContain(Key, failure);
    }
}
