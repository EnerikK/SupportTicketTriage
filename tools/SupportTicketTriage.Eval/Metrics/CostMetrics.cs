using SupportTicketTriage.Eval.Instrumentation;
using SupportTicketTriage.Eval.Pricing;

namespace SupportTicketTriage.Eval.Metrics;

/// What one stage cost in tokens and money.
///
/// `Cost` is null when the stage could not be priced in full, and
/// `UnpricedCalls` says how many calls caused that. **A partial sum is not
/// reported as a cost**: if 40 of 47 generation calls reported usage, adding
/// those 40 up and labelling the result "generation cost" produces a number
/// that is wrong by an unknown amount and looks exactly like a right one. The
/// rest of this harness refuses to score what it cannot measure, and money is
/// the worst place to make an exception.
/// Provenance travels with the row rather than with the file, so a reader can
/// see which meter and which day produced the figure beside it.
public sealed record StageCost(
    AiStage Stage,
    string? Model,
    long? InputTokens,
    long? OutputTokens,
    decimal? Cost,
    int UnpricedCalls,
    DateOnly? RetrievedOn,
    string? DeploymentType);

/// What a run cost, and what that implies per ticket.
///
/// Two per-ticket figures rather than one, because this pipeline has two paths
/// and a single mean over them is unstable for a reason that has nothing to do
/// with cost: every ticket pays for embedding and classification, but only a
/// ticket that clears both routing gates pays for generation - the expensive
/// call. Blend them and the "cost per ticket" moves whenever the gate ratio
/// moves, so tightening a threshold would look like getting cheaper. That is
/// the same defect the citation-validity denominator was designed to avoid.
public sealed record CostScore(
    string PricingVersion,
    string Currency,
    bool PricingVerified,
    DateOnly PricingRetrievedOn,
    IReadOnlyList<StageCost> PerStage,
    decimal? Total,
    /// Every ticket pays this: embedding plus classification.
    decimal? PerIngestedTicket,
    /// A ticket that also received a draft pays this - the figure above plus
    /// generation's share. The two differ by exactly the call the gates exist
    /// to avoid paying for.
    decimal? PerDraftedTicket,
    int TicketCount,
    int DraftCount);

public static class CostMetrics
{
    private static decimal? Money(long? tokens, decimal? perMillion) =>
        tokens is null || perMillion is null ? null : tokens.Value / 1_000_000m * perMillion.Value;

    /// Which deployment served a stage. Retrieval is pgvector, so it has none
    /// and costs nothing - reported as a row with a null model so that "the
    /// database is free" is visible rather than merely absent.
    /// Judging is its own deployment, never the chat one.
    ///
    /// Mapping it to chatModel would have priced the judge at the drafting
    /// model's rate - a plausible number produced by using the wrong price
    /// list, which is worse than the blank this returns when no judge model
    /// is known.
    private static string? ModelFor(
        AiStage stage, string embeddingModel, string? chatModel, string? judgeModel) =>
        stage switch
        {
            AiStage.Embedding => embeddingModel,
            AiStage.Classification or AiStage.Generation => chatModel,
            AiStage.Judging => judgeModel,
            _ => null,
        };

    public static CostScore Summarise(
        IReadOnlyCollection<AiCall> calls,
        ModelPricing pricing,
        string embeddingModel,
        string? chatModel,
        string? judgeModel,
        int ticketCount,
        int draftCount)
    {
        var perStage = calls
            .GroupBy(c => c.Stage)
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var model = ModelFor(g.Key, embeddingModel, chatModel, judgeModel);
                var price = model is null ? null : pricing.For(model);

                // Retrieval is not a model call: no tokens, no price, no cost.
                // It earns a row because the latency table has one, and a
                // reader comparing the two should not have to wonder whether a
                // missing cost row means free or unmeasured.
                if (g.Key == AiStage.Retrieval)
                {
                    return new StageCost(
                        g.Key, Model: null, null, null, Cost: 0m, UnpricedCalls: 0,
                        RetrievedOn: null, DeploymentType: null);
                }

                // A successful call that reported no input tokens cannot be
                // priced. Failures are not counted as unpriced - they are
                // genuinely unbilled from this harness's point of view, and
                // counting them would conflate "we do not know" with "there
                // was nothing to know".
                var unpriced = g.Count(c => c.Succeeded && c.InputTokens is null);
                var pricable = price is not null && unpriced == 0;

                var inputTokens = g.Sum(c => c.InputTokens ?? 0);
                var outputTokens = g.Sum(c => c.OutputTokens ?? 0);
                var anyOutput = g.Any(c => c.OutputTokens is not null);

                return new StageCost(
                    Stage: g.Key,
                    Model: model,
                    InputTokens: unpriced == 0 ? inputTokens : null,
                    OutputTokens: unpriced == 0 && anyOutput ? outputTokens : null,
                    Cost: pricable
                        ? (Money(inputTokens, price!.InputPerMillionTokens) ?? 0m)
                          + (anyOutput ? Money(outputTokens, price.OutputPerMillionTokens) ?? 0m : 0m)
                        : null,
                    UnpricedCalls: unpriced,
                    RetrievedOn: price?.RetrievedOn,
                    DeploymentType: price?.DeploymentType);
            })
            .ToList();

        decimal? CostOf(params AiStage[] stages)
        {
            var rows = perStage.Where(s => stages.Contains(s.Stage)).ToList();

            // Any unpriceable component makes the sum unreportable, for the
            // same reason as above - and a stage that never ran contributes
            // nothing rather than making the total null, since "generation was
            // skipped" is a known zero, not an unknown.
            return rows.Any(r => r.Cost is null) ? null : rows.Sum(r => r.Cost!.Value);
        }

        var ingest = CostOf(AiStage.Embedding, AiStage.Classification);
        var generation = CostOf(AiStage.Generation);

        return new CostScore(
            PricingVersion: pricing.Version,
            Currency: pricing.Currency,
            PricingVerified: pricing.Verified,
            // The oldest of the per-model dates. Rates are retrieved one at a
            // time, so the only honest single date is the staleness bound -
            // reporting the newest would let one fresh rate vouch for two old
            // ones, which is exactly the fiction that forced this schema change.
            PricingRetrievedOn: pricing.Models.Min(m => m.RetrievedOn),
            PerStage: perStage,
            Total: CostOf(Enum.GetValues<AiStage>()),
            PerIngestedTicket: ticketCount == 0 ? null : ingest / ticketCount,
            PerDraftedTicket: ticketCount == 0 || draftCount == 0 || ingest is null || generation is null
                ? null
                : ingest / ticketCount + generation / draftCount,
            TicketCount: ticketCount,
            DraftCount: draftCount);
    }
}
