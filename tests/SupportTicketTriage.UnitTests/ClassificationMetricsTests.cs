using SupportTicketTriage.Eval.Metrics;

namespace SupportTicketTriage.UnitTests;

public class ClassificationMetricsTests
{
    private static ClassificationCase Case(string expected, string? actual) =>
        new(Guid.NewGuid().ToString(), expected, actual);

    /// A confusion matrix small enough to score by hand, so this test checks
    /// the implementation rather than restating it.
    ///
    ///   Billing   : 3 expected, 2 predicted correctly, 1 called Technical
    ///   Technical : 2 expected, 1 predicted correctly, 1 called Billing
    ///   Shipping  : 1 expected, 0 predicted correctly, 1 unclassified
    ///
    /// Billing   precision 2/3, recall 2/3  -> F1 = 0.666…
    /// Technical precision 1/2, recall 1/2  -> F1 = 0.5
    /// Shipping  precision 0,   recall 0    -> F1 = 0
    /// Macro-F1 = (0.666… + 0.5 + 0) / 3    = 0.3888…
    /// Accuracy = 3 correct of 6            = 0.5
    private static List<ClassificationCase> HandScoredMatrix() =>
    [
        Case("Billing", "Billing"),
        Case("Billing", "Billing"),
        Case("Billing", "Technical"),
        Case("Technical", "Technical"),
        Case("Technical", "Billing"),
        Case("Shipping", null),
    ];

    [Fact]
    public void AccuracyAndMacroF1MatchAHandScoredMatrix()
    {
        var cases = HandScoredMatrix();

        Assert.Equal(0.5, ClassificationMetrics.Accuracy(cases), 10);
        Assert.Equal((2.0 / 3 + 0.5 + 0) / 3, ClassificationMetrics.MacroF1(cases), 10);
    }

    /// The distinction that matters most. An unclassified ticket has ground
    /// truth - the dataset says what it is - so it is wrong, not absent.
    /// Excluding these would let a classifier that refuses half its inputs
    /// report perfect accuracy on the half it managed.
    [Fact]
    public void AnUnclassifiedTicketCountsAsWrongRatherThanBeingExcluded()
    {
        var refusedEverything = new[] { Case("Billing", null), Case("Technical", null) };

        var score = ClassificationMetrics.Summarise(refusedEverything);

        Assert.Equal(0, score.Accuracy);
        Assert.Equal(2, score.Total);
        Assert.Equal(2, score.Unclassified);
    }

    [Fact]
    public void UnclassifiedCountIsReportedSeparatelyFromWrongAnswers()
    {
        var mixed = new[]
        {
            Case("Billing", "Billing"),
            Case("Billing", "Technical"),
            Case("Billing", null),
        };

        var score = ClassificationMetrics.Summarise(mixed);

        Assert.Equal(1.0 / 3, score.Accuracy, 10);
        Assert.Equal(1, score.Unclassified);
    }

    /// A category that never appears had no opportunity to be predicted.
    /// Scoring it zero would report a failure that never had a chance to
    /// happen and would drag the macro mean down for nothing.
    [Fact]
    public void ALabelWithNoSupportIsExcludedFromTheMacroMean()
    {
        var perfectOnOneLabel = new[] { Case("Billing", "Billing"), Case("Billing", "Billing") };

        Assert.Equal(1.0, ClassificationMetrics.MacroF1(perfectOnOneLabel), 10);
    }

    /// A label the model invented is still worth seeing, even though it is
    /// excluded from the mean - it is a false positive with zero support.
    [Fact]
    public void APredictedLabelThatNeverOccursIsReportedWithZeroSupport()
    {
        var cases = new[] { Case("Billing", "Billing"), Case("Billing", "Warranty") };

        var warranty = Assert.Single(
            ClassificationMetrics.PerLabel(cases), s => s.Label == "Warranty");

        Assert.Equal(0, warranty.Support);
        Assert.Equal(0, warranty.Precision);

        // Warranty is excluded from the mean, so macro-F1 is Billing's F1 alone.
        // Billing is not perfect here: one of its two tickets was called
        // Warranty, so precision is 1 but recall is 0.5, giving F1 = 2/3.
        Assert.Equal(2.0 / 3, ClassificationMetrics.MacroF1(cases), 10);
    }

    /// Macro-F1 exists to stop a majority class hiding an ignored one, so this
    /// pins that it actually does. Accuracy looks respectable; macro-F1 does
    /// not.
    [Fact]
    public void MacroF1PunishesAnIgnoredCategoryThatAccuracyHides()
    {
        var cases = Enumerable.Range(0, 9).Select(_ => Case("Billing", "Billing"))
            .Append(Case("Shipping", "Billing"))
            .ToList();

        var score = ClassificationMetrics.Summarise(cases);

        Assert.Equal(0.9, score.Accuracy, 10);
        Assert.True(
            score.MacroF1 < 0.55,
            $"Macro-F1 should collapse when a category is never predicted, but was {score.MacroF1}.");
    }

    [Fact]
    public void PerLabelScoresAreOrderedSoTwoRunsAreDiffable()
    {
        var labels = ClassificationMetrics.PerLabel(HandScoredMatrix()).Select(s => s.Label).ToList();

        Assert.Equal(labels.OrderBy(l => l, StringComparer.Ordinal), labels);
    }

    [Fact]
    public void AnEmptyRunScoresZeroRatherThanThrowing()
    {
        var score = ClassificationMetrics.Summarise([]);

        Assert.Equal(0, score.Accuracy);
        Assert.Equal(0, score.MacroF1);
        Assert.Empty(score.PerLabel);
    }
}
