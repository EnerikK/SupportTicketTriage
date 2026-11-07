using SupportTicketTriage.Infrastructure.Ai;
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

    /// An export stamps what wrote the drafts, so a judge that does not exist
    /// yet can say what it is scoring. The drafting model retires before these
    /// pairs stop being useful.
    [Fact]
    public async Task An_export_records_what_generated_the_drafts()
    {
        await JudgePairFile.WriteAsync(_path, [Pair(0.8643)], generationModel: "gpt-4.1-mini");

        var read = await JudgePairFile.ReadAsync(_path);

        Assert.Equal(GenerationPrompt.Version, read.GenerationPromptVersion);
        Assert.Equal(GenerationPrompt.Sha256, read.GenerationPromptSha256);
        Assert.Equal("gpt-4.1-mini", read.GenerationModel);
        Assert.NotNull(read.ExportedAt);

        // Captured at generation time, so there is nothing to disclose. The
        // field is only non-null where provenance was reconstructed afterwards.
        Assert.Null(read.ProvenanceReconstructedFrom);
    }

    /// The committed held-out pairs must have been written by the prompt that
    /// is in this tree.
    ///
    /// Their provenance was reconstructed rather than captured, so this is the
    /// check that makes the reconstruction verifiable instead of asserted: the
    /// recorded hash either matches the prompt here or it does not. It also
    /// catches the drift it was reconstructed because of - reword the
    /// generation prompt without re-exporting and the 93 labelled drafts
    /// quietly stop describing the prompt they came from.
    [Fact]
    public async Task The_committed_holdout_pairs_match_the_generation_prompt_in_this_tree()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SupportTicketTriage.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);

        var path = Path.Combine(directory.FullName, "eval", "judge", "holdout-pairs.json");
        Assert.True(File.Exists(path), $"No committed held-out pair file at {path}.");

        var file = await JudgePairFile.ReadAsync(path);

        Assert.Equal(93, file.Pairs.Count);
        Assert.Equal(GenerationPrompt.Version, file.GenerationPromptVersion);
        Assert.Equal(GenerationPrompt.Sha256, file.GenerationPromptSha256);
        Assert.Equal("8211969", file.ProvenanceReconstructedFrom);
    }

    public void Dispose()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }
}
