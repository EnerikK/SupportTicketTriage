using SupportTicketTriage.Eval.Baseline;
using SupportTicketTriage.Eval.Instrumentation;
using SupportTicketTriage.Eval.Metrics;
using SupportTicketTriage.Eval.Reporting;

namespace SupportTicketTriage.UnitTests;

/// The previous baseline was a serialised report and it rotted: properties were
/// added afterwards and the committed file can no longer be read back into the
/// record it came from. These pin the properties that stop that recurring.
public class EvaluationBaselineTests
{
    private static EvaluationReport Report(
        ClassificationScore? classification = null,
        CitationScore? citations = null,
        CostScore? cost = null) =>
        new(
            DatasetVersion: "1.0",
            EmbeddingModel: "text-embedding-3-small",
            EmbeddingModelIsReal: true,
            ChatModel: "gpt-4.1-mini",
            ClassificationPromptVersion: "classify-v1",
            GenerationPromptVersion: "draft-v1",
            RedactionVersion: "v1",
            MinimumClassificationScore: 0.50,
            MinimumSimilarity: 0.60,
            GeneratedAt: new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero),
            CorpusCount: 240,
            QueryCount: 60,
            ScoredQueryCount: 54,
            UnscoredQueryCount: 6,
            RecallAtK: new Dictionary<int, double> { [5] = 0.944, [1] = 0.759 },
            Classification: classification,
            Citations: citations,
            Groundedness: null,
            JudgeAgreement: null,
            JudgeModel: null,
            JudgeRubricVersion: null,
            JudgeReasoningEffort: null,
            LabelSha256: null,
            LabelCommit: null,
            Calibration: [],
            Latency:
            [
                new StageLatency(AiStage.Generation, 47, 0, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(99)),
            ],
            Cost: cost,
            TraitCoverage: new Dictionary<string, int> { ["pii"] = 19 });

    private static ClassificationScore Classified() =>
        new(Accuracy: 0.927, MacroF1: 0.933, Total: 300, Unclassified: 4, PerLabel: []);

    private static CostScore Costed() =>
        new(
            PricingVersion: "1.0",
            Currency: "USD",
            PricingVerified: true,
            PricingRetrievedOn: new DateOnly(2026, 10, 16),
            PerStage: [],
            Total: 0.0750m,
            PerIngestedTicket: 0.000149m,
            PerDraftedTicket: 0.000796m,
            TicketCount: 300,
            DraftCount: 47);

    [Fact]
    public void TheProvenanceIsCarriedFromTheReport()
    {
        var p = EvaluationBaseline.From(Report(cost: Costed())).Provenance;

        Assert.Equal("1.0", p.DatasetVersion);
        Assert.Equal("text-embedding-3-small", p.EmbeddingModel);
        Assert.Equal("gpt-4.1-mini", p.ChatModel);
        Assert.Equal("classify-v1", p.ClassificationPromptVersion);
        Assert.Equal("draft-v1", p.GenerationPromptVersion);
        Assert.Equal("v1", p.RedactionVersion);
        Assert.Equal(0.50, p.MinimumClassificationScore);
        Assert.Equal(0.60, p.MinimumSimilarity);
        Assert.Equal("1.0", p.PricingVersion);
    }

    [Fact]
    public void TheGatedMetricsAreCarried()
    {
        var m = EvaluationBaseline.From(
            Report(Classified(), new CitationScore(1.0, 47, 47, 0, 13, 0, 75, 0), Costed())).Metrics;

        Assert.Equal(0.927, m.ClassificationAccuracy);
        Assert.Equal(0.933, m.ClassificationMacroF1);
        Assert.Equal(1.0, m.CitationValidityRate);
        Assert.Equal(0.0750m, m.CostTotal);
        Assert.Equal(0.000796m, m.CostPerDraftedTicket);
    }

    /// Recall is keyed by k and ordered, so two baselines are diffable line by
    /// line rather than only equal as a set.
    [Fact]
    public void RecallIsOrderedByK()
    {
        var recall = EvaluationBaseline.From(Report()).Metrics.RecallAtK;

        Assert.Equal([1, 5], recall.Keys);
    }

    /// A run that skipped generation still produces a valid baseline - the
    /// absent metrics read as not covered rather than as zero, which would
    /// otherwise be a regression the moment a real run came along.
    [Fact]
    public void AMetricThatWasNotScoredIsNullRatherThanZero()
    {
        var m = EvaluationBaseline.From(Report()).Metrics;

        Assert.Null(m.ClassificationAccuracy);
        Assert.Null(m.CitationValidityRate);
        Assert.Null(m.CostTotal);
        Assert.NotEmpty(m.RecallAtK);
    }

    /// Latency is excluded by ADR-020 - it tracks Azure's load rather than this
    /// code - so a report carrying it must not leak it into the baseline.
    [Fact]
    public async Task LatencyIsNotPartOfTheBaselineEvenWhenTheReportHasIt()
    {
        var json = await RoundTripJsonAsync(EvaluationBaseline.From(Report()));

        Assert.DoesNotContain("Latency", json);
        Assert.DoesNotContain("P95", json);
    }

    [Fact]
    public async Task ABaselineRoundTrips()
    {
        var original = EvaluationBaseline.From(
            Report(Classified(), new CitationScore(1.0, 47, 47, 0, 13, 0, 75, 0), Costed()));

        var path = Path.Combine(Path.GetTempPath(), $"baseline-{Guid.NewGuid():N}.json");

        try
        {
            await original.WriteAsync(path);
            var loaded = await EvaluationBaseline.LoadAsync(path);

            // Compared part by part rather than as whole records. A record
            // containing a dictionary does not get value equality - the
            // compiler-generated Equals falls back to reference equality for
            // the dictionary - so Assert.Equal on the records fails even when
            // every value matches. Worth knowing rather than working around
            // silently: it also means the gate must compare metrics field by
            // field, which it does anyway because it applies tolerances.
            Assert.Equal(original.Version, loaded.Version);
            Assert.Equal(original.Provenance, loaded.Provenance);
            Assert.Equal(original.Metrics.RecallAtK, loaded.Metrics.RecallAtK);
            Assert.Equal(
                original.Metrics with { RecallAtK = loaded.Metrics.RecallAtK },
                loaded.Metrics);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// The failure the previous baseline actually suffered: the shape moved and
    /// nothing noticed. A version mismatch has to be loud.
    [Fact]
    public async Task ABaselineFromAnotherVersionIsRefusedRatherThanReadAsNulls()
    {
        var path = Path.Combine(Path.GetTempPath(), $"baseline-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(
            path,
            """
            { "version": "0", "provenance": null, "metrics": null }
            """);

        try
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(
                () => EvaluationBaseline.LoadAsync(path));

            Assert.Contains("version '0'", error.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ABaselineWithNoRecallIsRefused()
    {
        var empty = EvaluationBaseline.From(Report()) with
        {
            Metrics = EvaluationBaseline.From(Report()).Metrics with
            {
                RecallAtK = new Dictionary<int, double>(),
            },
        };

        var error = Assert.Throws<InvalidOperationException>(empty.Validate);

        Assert.Contains("guarding nothing", error.Message);
    }

    private static async Task<string> RoundTripJsonAsync(EvaluationBaseline baseline)
    {
        var path = Path.Combine(Path.GetTempPath(), $"baseline-{Guid.NewGuid():N}.json");

        try
        {
            await baseline.WriteAsync(path);
            return await File.ReadAllTextAsync(path);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
