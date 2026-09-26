using SupportTicketTriage.Domain;

namespace SupportTicketTriage.UnitTests;

public class TicketCategoryTests
{
    /// The labels are the contract between three things: the prompt the model
    /// is given, the evaluation dataset's `category` field, and the API's wire
    /// format. If a round trip broke, Phase 5 would score a mismatch as a
    /// classification error rather than as the bug it is.
    [Fact]
    public void EveryCategory_RoundTripsThroughItsLabel()
    {
        foreach (var category in Enum.GetValues<TicketCategory>())
        {
            var label = category.ToLabel();

            Assert.True(TicketCategories.TryFromLabel(label, out var parsed), label);
            Assert.Equal(category, parsed);
        }
    }

    [Fact]
    public void LabelsCoverEveryCategory_AndNothingElse()
    {
        Assert.Equal(Enum.GetValues<TicketCategory>().Length, TicketCategories.Labels.Count);
        Assert.Equal(TicketCategories.Labels.Distinct().Count(), TicketCategories.Labels.Count);
    }

    [Fact]
    public void EveryPriority_RoundTripsThroughItsLabel()
    {
        foreach (var priority in Enum.GetValues<TicketPriority>())
        {
            Assert.True(TicketPriorities.TryFromLabel(priority.ToString(), out var parsed));
            Assert.Equal(priority, parsed);
        }
    }

    /// Enum.TryParse accepts the underlying numeric value by default, which
    /// would let "7" through as a priority that does not exist.
    [Theory]
    [InlineData("7")]
    [InlineData("-1")]
    [InlineData("Critical")]
    [InlineData("")]
    public void UnknownPriority_IsRejected(string label)
    {
        Assert.False(TicketPriorities.TryFromLabel(label, out _));
    }
}
