using SupportTicketTriage.Eval.Pricing;

namespace SupportTicketTriage.UnitTests;

/// A malformed pricing file that loaded anyway would not fail - it would
/// quietly report a cost wrong by an unknown factor, and nothing downstream
/// could tell. These pin the refusals that keep that from happening.
public class ModelPricingTests
{
    private static ModelPricing Pricing(params ModelPrice[] models) =>
        new(
            Version: "1.0",
            Currency: "USD",
            RetrievedOn: new DateOnly(2026, 10, 16),
            Source: "test",
            Verified: true,
            Models: models);

    private static ModelPrice Price(string model, decimal input = 1m, decimal? output = 2m) =>
        new(model, input, output);

    [Fact]
    public void AValidFilePasses()
    {
        Pricing(Price("gpt-4.1-mini")).Validate();
    }

    /// Which price applied would otherwise depend on list order, and a JSON
    /// file is not where that decision belongs.
    [Fact]
    public void AModelPricedTwiceIsRejected()
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => Pricing(Price("gpt-4.1-mini", 1m), Price("gpt-4.1-mini", 9m)).Validate());

        Assert.Contains("more than once", error.Message);
    }

    /// Case-insensitively, because a deployment name that differs only in case
    /// is the same deployment and would produce two prices for one model.
    [Fact]
    public void DuplicateDetectionIgnoresCase()
    {
        Assert.Throws<InvalidOperationException>(
            () => Pricing(Price("GPT-4.1-Mini"), Price("gpt-4.1-mini")).Validate());
    }

    [Fact]
    public void AFileWithNoModelsIsRejected()
    {
        Assert.Throws<InvalidOperationException>(() => Pricing().Validate());
    }

    [Fact]
    public void ANegativePriceIsRejected()
    {
        Assert.Throws<InvalidOperationException>(
            () => Pricing(Price("gpt-4.1-mini", input: -1m)).Validate());

        Assert.Throws<InvalidOperationException>(
            () => Pricing(Price("gpt-4.1-mini", output: -1m)).Validate());
    }

    [Fact]
    public void AFileWithNoVersionIsRejectedBecauseTheReportStampsIt()
    {
        var unversioned = Pricing(Price("gpt-4.1-mini")) with { Version = "  " };

        Assert.Throws<InvalidOperationException>(unversioned.Validate);
    }

    [Fact]
    public void AnEntryWithNoModelNameIsRejected()
    {
        Assert.Throws<InvalidOperationException>(() => Pricing(Price("")).Validate());
    }

    /// A null output price is legitimate - an embedding deployment has no
    /// output tokens to charge for - and must not be mistaken for a missing one.
    [Fact]
    public void ANullOutputPriceIsAllowedForAModelThatProducesNoOutputTokens()
    {
        Pricing(Price("text-embedding-3-small", input: 0.02m, output: null)).Validate();
    }

    [Fact]
    public void LookupIsCaseInsensitiveAndMissesReturnNull()
    {
        var pricing = Pricing(Price("gpt-4.1-mini"));

        Assert.NotNull(pricing.For("GPT-4.1-MINI"));
        Assert.Null(pricing.For("something-else"));
    }

    /// Exercises the real JSON binding once - camelCase names, the DateOnly,
    /// and the explicit null output price - so a property rename cannot pass
    /// the validator tests while failing to load the committed file.
    [Fact]
    public async Task TheCommittedFileShapeLoadsAndValidates()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pricing-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(
            path,
            """
            {
              "version": "1.0",
              "currency": "USD",
              "retrievedOn": "2026-10-16",
              "source": "https://example.invalid/pricing",
              "verified": false,
              "models": [
                { "model": "gpt-4.1-mini", "inputPerMillionTokens": 0.40, "outputPerMillionTokens": 1.60 },
                { "model": "text-embedding-3-small", "inputPerMillionTokens": 0.02, "outputPerMillionTokens": null }
              ]
            }
            """);

        try
        {
            var pricing = await ModelPricing.LoadAsync(path);

            Assert.Equal("1.0", pricing.Version);
            Assert.Equal(new DateOnly(2026, 10, 16), pricing.RetrievedOn);
            Assert.False(pricing.Verified);
            Assert.Equal(1.60m, pricing.For("gpt-4.1-mini")!.OutputPerMillionTokens);
            Assert.Null(pricing.For("text-embedding-3-small")!.OutputPerMillionTokens);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
