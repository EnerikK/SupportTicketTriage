using SupportTicketTriage.Eval.Instrumentation;
using SupportTicketTriage.Eval.Metrics;
using SupportTicketTriage.Eval.Pricing;

namespace SupportTicketTriage.UnitTests;

public class CostMetricsTests
{
    private const string ChatModel = "test-chat";
    private const string EmbeddingModel = "test-embedding";

    /// Round numbers on purpose: at these prices a million input tokens costs
    /// exactly 1.00, so the arithmetic can be checked by eye rather than by
    /// re-running the same multiplication the code does.
    private static ModelPricing Pricing(bool verified = true) =>
        new(
            Version: "test-1",
            Currency: "USD",
            RetrievedOn: new DateOnly(2026, 10, 16),
            Source: "test",
            Verified: verified,
            Models:
            [
                new ModelPrice(ChatModel, InputPerMillionTokens: 1.00m, OutputPerMillionTokens: 10.00m),
                new ModelPrice(EmbeddingModel, InputPerMillionTokens: 0.10m, OutputPerMillionTokens: null),
            ]);

    private static AiCall Call(AiStage stage, long? input, long? output, bool succeeded = true) =>
        new(stage, TimeSpan.FromMilliseconds(10), input, output, succeeded);

    private static CostScore Summarise(
        IReadOnlyCollection<AiCall> calls,
        int ticketCount = 300,
        int draftCount = 47,
        bool verified = true) =>
        CostMetrics.Summarise(
            calls, Pricing(verified), EmbeddingModel, ChatModel, ticketCount, draftCount);

    [Fact]
    public void TokensArePricedPerMillion()
    {
        var score = Summarise([Call(AiStage.Classification, input: 1_000_000, output: 100_000)]);

        var stage = Assert.Single(score.PerStage);

        // 1.00 for the input million, plus a tenth of a million output at 10.00.
        Assert.Equal(2.00m, stage.Cost);
        Assert.Equal(1_000_000, stage.InputTokens);
        Assert.Equal(100_000, stage.OutputTokens);
    }

    /// An embedding has no output tokens, and its price has no output rate.
    /// Pricing a null at zero would be harmless here but wrong in principle,
    /// and the same code path prices chat output where it would not be.
    [Fact]
    public void AnEmbeddingIsPricedOnInputTokensAlone()
    {
        var score = Summarise([Call(AiStage.Embedding, input: 2_000_000, output: null)]);

        var stage = Assert.Single(score.PerStage);

        Assert.Equal(0.20m, stage.Cost);
        Assert.Null(stage.OutputTokens);
    }

    /// **The central refusal.** A stage where any successful call reported no
    /// usage cannot be priced, and the partial sum is withheld rather than
    /// published. Reporting the 1,000,000 tokens we do know about would be a
    /// cost that is wrong by exactly the amount nobody can see.
    [Fact]
    public void AStageWithAnyUnpricedCallReportsNoCostRatherThanAPartialSum()
    {
        var score = Summarise(
        [
            Call(AiStage.Classification, input: 1_000_000, output: 0),
            Call(AiStage.Classification, input: null, output: null),
        ]);

        var stage = Assert.Single(score.PerStage);

        Assert.Null(stage.Cost);
        Assert.Null(stage.InputTokens);
        Assert.Equal(1, stage.UnpricedCalls);
        Assert.Null(score.Total);
    }

    /// A failure is not an unpriced call. Nothing was billed, so there is
    /// nothing unknown about it - conflating the two would make every run with
    /// a transient error report no cost at all.
    [Fact]
    public void AFailedCallDoesNotMakeAStageUnpriceable()
    {
        var score = Summarise(
        [
            Call(AiStage.Classification, input: 1_000_000, output: 0),
            Call(AiStage.Classification, input: null, output: null, succeeded: false),
        ]);

        var stage = Assert.Single(score.PerStage);

        Assert.Equal(0, stage.UnpricedCalls);
        Assert.Equal(1.00m, stage.Cost);
    }

    [Fact]
    public void AModelMissingFromThePricingFileIsNotPriced()
    {
        var score = CostMetrics.Summarise(
            [Call(AiStage.Classification, input: 1_000_000, output: 0)],
            Pricing(),
            EmbeddingModel,
            chatModel: "a-deployment-nobody-priced",
            ticketCount: 1,
            draftCount: 1);

        Assert.Null(Assert.Single(score.PerStage).Cost);
        Assert.Null(score.Total);
    }

    /// Retrieval is a database query. It earns a row so that a reader comparing
    /// the cost table against the latency table does not have to wonder whether
    /// a missing row means free or unmeasured - it means free, and says so.
    [Fact]
    public void RetrievalIsReportedAsFreeRatherThanOmitted()
    {
        var score = Summarise([Call(AiStage.Retrieval, input: null, output: null)]);

        var stage = Assert.Single(score.PerStage);

        Assert.Equal(AiStage.Retrieval, stage.Stage);
        Assert.Equal(0m, stage.Cost);
        Assert.Null(stage.Model);
        Assert.Equal(0, stage.UnpricedCalls);
    }

    /// The two per-ticket figures differ by exactly the call the routing gates
    /// exist to avoid paying for. A single blended mean would move whenever the
    /// gate ratio moved, so tightening a threshold would look like a saving.
    [Fact]
    public void CostPerTicketIsSplitByWhetherTheTicketWasDrafted()
    {
        var score = Summarise(
        [
            Call(AiStage.Embedding, input: 1_000_000, output: null),      // 0.10
            Call(AiStage.Classification, input: 1_000_000, output: 0),    // 1.00
            Call(AiStage.Generation, input: 1_000_000, output: 100_000),  // 2.00
        ],
            ticketCount: 100,
            draftCount: 10);

        // Everything ingested pays 1.10 across 100 tickets.
        Assert.Equal(0.011m, score.PerIngestedTicket);

        // A drafted ticket adds generation's 2.00 spread over the 10 drafts.
        Assert.Equal(0.211m, score.PerDraftedTicket);
        Assert.Equal(3.10m, score.Total);
    }

    [Fact]
    public void NoDraftsMeansNoPerDraftedFigureRatherThanADivisionByZero()
    {
        var score = Summarise(
            [Call(AiStage.Embedding, input: 1_000_000, output: null)],
            ticketCount: 10,
            draftCount: 0);

        Assert.Null(score.PerDraftedTicket);
        Assert.Equal(0.01m, score.PerIngestedTicket);
    }

    /// The flag has to survive into the score, because the report keys its
    /// warning banner off it and a baseline is refused on it.
    [Fact]
    public void ThePricingProvenanceIsCarriedIntoTheScore()
    {
        var score = Summarise([Call(AiStage.Embedding, 1, null)], verified: false);

        Assert.False(score.PricingVerified);
        Assert.Equal("test-1", score.PricingVersion);
        Assert.Equal("USD", score.Currency);
        Assert.Equal(new DateOnly(2026, 10, 16), score.PricingRetrievedOn);
    }
}
