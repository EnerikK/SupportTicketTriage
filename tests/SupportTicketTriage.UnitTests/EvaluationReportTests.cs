using System.Globalization;
using SupportTicketTriage.Eval.Metrics;
using SupportTicketTriage.Eval.Reporting;

namespace SupportTicketTriage.UnitTests;

/// The Markdown report is an artifact: it gets compared between runs on
/// different machines and pasted into the README. It must therefore render
/// identically regardless of who ran it, which is a property of the report
/// rather than of the process that happened to produce it.
public class EvaluationReportTests
{
    private static EvaluationReport Report() =>
        new(
            DatasetVersion: "1.0",
            EmbeddingModel: "text-embedding-3-small",
            EmbeddingModelIsReal: true,
            ChatModel: "gpt-4.1-mini",
            GeneratedAt: new DateTimeOffset(2026, 10, 8, 17, 6, 52, TimeSpan.Zero),
            CorpusCount: 240,
            QueryCount: 60,
            ScoredQueryCount: 54,
            UnscoredQueryCount: 6,
            RecallAtK: new Dictionary<int, double> { [1] = 0.7592592592592593, [3] = 0.8981481481481481 },
            Classification: null,
            Citations: null,
            Calibration: [],
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

    /// The calibration section carries the evidence a threshold decision is
    /// made from, including the scores of observations whose outcome is
    /// undefined - those are not evidence of where the floor belongs, but a
    /// floor above all of them refuses every unanswerable query, which is the
    /// clearest signal the table has.
    [Fact]
    public void TheCalibrationSectionNamesTheFloorThatRefusesEveryUndefinedCase()
    {
        var gate = new GateCalibration(
            Gate: "B - retrieval similarity",
            ScoreName: "top-1 cosine similarity",
            SuccessName: "a relevant ticket in the top 5",
            Separation: 0.141,
            AbsentScore: 0,
            Floors: CalibrationMetrics.AtEachFloor(
                [new CalibrationCase("q1", 0.72, true), new CalibrationCase("q2", 0.40, false)],
                [0.60]),
            UndefinedOutcomeScores: [0.258, 0.531, 0.598]);

        var markdown = (Report() with { Calibration = [gate] }).ToMarkdown();

        Assert.Contains("Gate calibration", markdown);
        Assert.Contains("Separation: 0.141", markdown);
        Assert.Contains("a floor above 0.598 refuses all of them", markdown);
        // Invariant formatting here too - this table is diffed between runs.
        Assert.DoesNotContain("0,598", markdown);
    }

    [Fact]
    public void TheCalibrationSectionIsOmittedWhenThereIsNothingToCalibrate()
    {
        Assert.DoesNotContain("Gate calibration", Report().ToMarkdown());
    }

    /// A report must not state something false about the run that produced it.
    ///
    /// `Citations` is null for two different reasons - no chat deployment, or
    /// `--skip-generation` with one present - and the renderer used to claim
    /// the first unconditionally. That made the report contradict its own
    /// header, which names the chat deployment it had just said did not exist.
    ///
    /// The report built here is the exact shape `--skip-generation` produces
    /// against a real deployment: classification scored, citations not.
    [Fact]
    public void SkippedGenerationIsReportedAsSkippedRatherThanAsAMissingDeployment()
    {
        var markdown = (Report() with { Classification = Classified() }).ToMarkdown();

        Assert.Contains("`gpt-4.1-mini`", markdown);
        Assert.Contains("generation was skipped", markdown);
        Assert.DoesNotContain("this run had no chat deployment", markdown);
    }

    /// The other half of the same branch: with no chat deployment at all, the
    /// original message is the true one and must survive.
    [Fact]
    public void AMissingChatDeploymentIsStillReportedAsSuch()
    {
        var markdown = (Report() with { ChatModel = null }).ToMarkdown();

        Assert.Contains("this run had no chat deployment", markdown);
        Assert.DoesNotContain("generation was skipped", markdown);
    }

    private static ClassificationScore Classified() =>
        new(
            Accuracy: 0.92,
            MacroF1: 0.928,
            Total: 300,
            Unclassified: 5,
            PerLabel: [new LabelScore("Billing", 0.94, 0.94, 0.94, 50)]);
}
