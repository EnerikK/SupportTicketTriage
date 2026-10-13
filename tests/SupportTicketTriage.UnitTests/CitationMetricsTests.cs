using SupportTicketTriage.Eval.Metrics;

namespace SupportTicketTriage.UnitTests;

public class CitationMetricsTests
{
    private static CitationCase Case(DraftOutcomeKind outcome, int cited = 0, int invalid = 0) =>
        new(Guid.NewGuid().ToString(), outcome, cited, invalid);

    /// The denominator is drafts the model actually produced. Tickets the
    /// gates refused never reached it, so including them would make this
    /// number improve every time the gates got stricter - measuring the gates
    /// rather than the model.
    [Fact]
    public void GatedTicketsAreExcludedFromTheDenominator()
    {
        var cases = new[]
        {
            Case(DraftOutcomeKind.Valid, cited: 2),
            Case(DraftOutcomeKind.InvalidCitations, cited: 2, invalid: 1),
            Case(DraftOutcomeKind.NotEligible),
            Case(DraftOutcomeKind.NotEligible),
            Case(DraftOutcomeKind.NotEligible),
        };

        var score = CitationMetrics.Summarise(cases);

        Assert.Equal(2, score.Attempted);
        Assert.Equal(0.5, score.ValidityRate, 10);
        Assert.Equal(3, score.NotEligible);
    }

    /// Stricter gates must not be able to flatter this metric.
    [Fact]
    public void GatingMoreTicketsDoesNotImproveTheValidityRate()
    {
        var baseline = new[]
        {
            Case(DraftOutcomeKind.Valid),
            Case(DraftOutcomeKind.InvalidCitations, invalid: 1),
        };

        var withMoreGating = baseline.Concat(Enumerable.Range(0, 20)
            .Select(_ => Case(DraftOutcomeKind.NotEligible))).ToList();

        Assert.Equal(
            CitationMetrics.Summarise(baseline).ValidityRate,
            CitationMetrics.Summarise(withMoreGating).ValidityRate,
            10);
    }

    /// A generation failure produced no citations to validate. Counting it as
    /// an invalid-citation case would blend two different defects into one
    /// number, so it is excluded and reported on its own.
    [Fact]
    public void GenerationFailuresAreCountedSeparatelyAndNotAsInvalidCitations()
    {
        var cases = new[]
        {
            Case(DraftOutcomeKind.Valid, cited: 3),
            Case(DraftOutcomeKind.Failed),
        };

        var score = CitationMetrics.Summarise(cases);

        Assert.Equal(1, score.Attempted);
        Assert.Equal(1.0, score.ValidityRate, 10);
        Assert.Equal(1, score.Failed);
        Assert.Equal(0, score.InvalidCitations);
    }

    [Fact]
    public void TotalCitationCountsAreCarriedForDiagnostics()
    {
        var cases = new[]
        {
            Case(DraftOutcomeKind.Valid, cited: 3),
            Case(DraftOutcomeKind.InvalidCitations, cited: 4, invalid: 2),
        };

        var score = CitationMetrics.Summarise(cases);

        Assert.Equal(7, score.TotalCitations);
        Assert.Equal(2, score.TotalInvalidCitations);
    }

    [Fact]
    public void ARunWhereEveryTicketWasGatedReportsZeroRatherThanThrowing()
    {
        var score = CitationMetrics.Summarise([Case(DraftOutcomeKind.NotEligible)]);

        Assert.Equal(0, score.Attempted);
        Assert.Equal(0, score.ValidityRate);
    }
}
