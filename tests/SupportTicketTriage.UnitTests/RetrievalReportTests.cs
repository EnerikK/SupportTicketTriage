using System.Globalization;
using SupportTicketTriage.Eval.Reporting;

namespace SupportTicketTriage.UnitTests;

/// The Markdown report is an artifact: it gets compared between runs on
/// different machines and pasted into the README. It must therefore render
/// identically regardless of who ran it, which is a property of the report
/// rather than of the process that happened to produce it.
public class RetrievalReportTests
{
    private static RetrievalReport Report() =>
        new(
            DatasetVersion: "1.0",
            EmbeddingModel: "text-embedding-3-small",
            EmbeddingModelIsReal: true,
            GeneratedAt: new DateTimeOffset(2026, 10, 8, 17, 6, 52, TimeSpan.Zero),
            CorpusCount: 240,
            QueryCount: 60,
            ScoredQueryCount: 54,
            UnscoredQueryCount: 6,
            RecallAtK: new Dictionary<int, double> { [1] = 0.7592592592592593, [3] = 0.8981481481481481 },
            TraitCoverage: new Dictionary<string, int> { ["hard-negative"] = 75 });

    /// Runs the renderer under a comma-decimal culture, which is what this was
    /// developed on and what produced "75,9%" in a real report. Asserting the
    /// output rather than setting a process-wide culture is deliberate: a
    /// global `DefaultThreadCurrentCulture` is ambient state any caller can
    /// undo, and a test that set its own culture would silently defeat it.
    /// Invariant formatting at the point of use is a property nothing can
    /// override, and this test proves it by trying to.
    [Theory]
    [InlineData("el-GR")]
    [InlineData("de-DE")]
    [InlineData("fr-FR")]
    public void RecallRendersWithADecimalPoint_WhateverTheAmbientCulture(string culture)
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);

            var markdown = Report().ToMarkdown();

            Assert.Contains("75.9", markdown);
            Assert.DoesNotContain("75,9", markdown);
            Assert.Contains("89.8", markdown);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    /// Pins the whole table, so a later change to the format specifier is a
    /// visible decision rather than a silent one.
    [Fact]
    public void TheRecallTableIsOrderedByKAndFormattedToOneDecimal()
    {
        var markdown = Report().ToMarkdown();

        Assert.Contains("| 1 | 75.9% |", markdown);
        Assert.Contains("| 3 | 89.8% |", markdown);
        Assert.True(
            markdown.IndexOf("| 1 | 75.9% |", StringComparison.Ordinal)
                < markdown.IndexOf("| 3 | 89.8% |", StringComparison.Ordinal),
            "Recall rows should be ordered by k.");
    }

    /// The stand-in embedder cannot produce a baseline, and the report says so
    /// in the document itself rather than only refusing at write time.
    [Fact]
    public void AStandInEmbedderIsCalledOutInTheReportItself()
    {
        var standIn = Report() with { EmbeddingModel = "fake-lexical-v1", EmbeddingModelIsReal = false };

        Assert.Contains("not a baseline", standIn.ToMarkdown());
        Assert.DoesNotContain("not a baseline", Report().ToMarkdown());
    }
}
