using SupportTicketTriage.Eval.Embedding;
using SupportTicketTriage.Eval.Reporting;
using SupportTicketTriage.Infrastructure.Ai;

namespace SupportTicketTriage.UnitTests;

public class EmbeddingSelectionTests
{
    [Fact]
    public void FullyConfiguredOptions_UseTheDeploymentAndStampItsName()
    {
        var selection = EmbeddingSelection.From(new AzureOpenAiOptions
        {
            Endpoint = "https://example.openai.azure.com/",
            ApiKey = "not-a-real-key",
            EmbeddingDeployment = "text-embedding-3-small",
        });

        Assert.Equal("text-embedding-3-small", selection.ModelName);
        Assert.IsNotType<LexicalEmbeddingGenerator>(selection.Generator);
    }

    // The stamped name is what unlocks --write-baseline. Asserting the gate
    // directly, rather than just the string, keeps the two from drifting apart.
    [Fact]
    public void AConfiguredDeployment_SatisfiesTheBaselineGate()
    {
        var selection = EmbeddingSelection.From(new AzureOpenAiOptions
        {
            Endpoint = "https://example.openai.azure.com/",
            ApiKey = "not-a-real-key",
            EmbeddingDeployment = "text-embedding-3-small",
        });

        Assert.True(RetrievalReport.IsRealEmbeddingModel(selection.ModelName));
    }

    [Theory]
    [InlineData(null, "key", "deployment")]
    [InlineData("https://example.openai.azure.com/", null, "deployment")]
    [InlineData("https://example.openai.azure.com/", "key", null)]
    [InlineData("", "key", "deployment")]
    [InlineData(null, null, null)]
    public void PartiallyConfiguredOptions_FallBackToTheStandIn(
        string? endpoint,
        string? apiKey,
        string? deployment)
    {
        var selection = EmbeddingSelection.From(new AzureOpenAiOptions
        {
            Endpoint = endpoint,
            ApiKey = apiKey,
            EmbeddingDeployment = deployment,
        });

        Assert.Equal(LexicalEmbeddingGenerator.ModelName, selection.ModelName);
        Assert.IsType<LexicalEmbeddingGenerator>(selection.Generator);
        Assert.False(RetrievalReport.IsRealEmbeddingModel(selection.ModelName));
    }
}
