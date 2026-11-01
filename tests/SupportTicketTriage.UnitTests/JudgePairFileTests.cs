using SupportTicketTriage.Eval.Judging;

namespace SupportTicketTriage.UnitTests;

/// The pair file is what a labeller reads and what every judge configuration
/// is scored against. Its top-1 similarity fixes which band a draft is
/// reported in, so it has to survive the round trip and it has to be present.
public sealed class JudgePairFileTests : IDisposable
{
    private readonly string _path = Path.Combine(
        Path.GetTempPath(), $"judge-pairs-{Guid.NewGuid():N}.json");

    private static JudgePair Pair(double? topSimilarity, string queryId = "q-ho-billing-swap-card-on-file") =>
        new(
            queryId,
            "Replacing the card you have on file",
            "New card, old one is cancelled.",
            "Thanks for getting in touch about your card.",
            [new JudgeSource(Guid.NewGuid(), "How do I change the card on file", "body", "resolution")],
            topSimilarity,
            Grounded: null,
            Abstained: null,
            Note: null);

    [Fact]
    public async Task Round_trip_preserves_the_top_similarity()
    {
        await JudgePairFile.WriteAsync(_path, [Pair(0.8643)]);

        var read = await JudgePairFile.ReadAsync(_path);

        Assert.Equal(0.8643, Assert.Single(read.Pairs).TopSimilarity);
    }

    [Fact]
    public async Task Writing_a_drafted_pair_without_a_similarity_throws()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => JudgePairFile.WriteAsync(_path, [Pair(topSimilarity: null)]));

        Assert.Contains("q-ho-billing-swap-card-on-file", ex.Message);
        Assert.False(File.Exists(_path), "the file must not be written when a pair is unmeasured");
    }

    [Fact]
    public async Task The_failure_names_every_unmeasured_pair()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => JudgePairFile.WriteAsync(
                _path,
                [Pair(0.71, "q-ho-fault-restore-deleted-records"), Pair(null, "q-ho-shipping-strike-delay"), Pair(null, "q-ho-product-how-loud")]));

        Assert.Contains("q-ho-shipping-strike-delay", ex.Message);
        Assert.Contains("q-ho-product-how-loud", ex.Message);
        Assert.DoesNotContain("q-ho-fault-restore-deleted-records", ex.Message);
    }

    public void Dispose()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }
}
