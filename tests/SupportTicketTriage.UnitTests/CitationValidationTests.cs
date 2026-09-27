using SupportTicketTriage.Application.Generation;

namespace SupportTicketTriage.UnitTests;

public class CitationValidationTests
{
    private static readonly Guid InSet1 = Guid.Parse("0199a000-0000-7000-8000-00000000a001");
    private static readonly Guid InSet2 = Guid.Parse("0199a000-0000-7000-8000-00000000a002");
    private static readonly Guid Elsewhere = Guid.Parse("0199a000-0000-7000-8000-00000000b001");

    private static IReadOnlySet<Guid> RetrievalSet => new HashSet<Guid> { InSet1, InSet2 };

    [Fact]
    public void CitationsDrawnFromTheRetrievalSet_AreValid()
    {
        var validation = CitationValidation.Validate([InSet1, InSet2], RetrievalSet);

        Assert.True(validation.AllValid);
        Assert.Equal([InSet1, InSet2], validation.Valid);
        Assert.Empty(validation.Invalid);
    }

    /// The failure this check exists for. A cited ticket may be perfectly
    /// real and still be evidence the model was never shown — citing it is
    /// claiming support that this generation does not have.
    [Fact]
    public void ARealTicketOutsideTheRetrievalSet_IsStillInvalid()
    {
        var validation = CitationValidation.Validate([InSet1, Elsewhere], RetrievalSet);

        Assert.False(validation.AllValid);
        Assert.Equal([InSet1], validation.Valid);
        Assert.Equal([Elsewhere], validation.Invalid);
    }

    [Fact]
    public void AnInventedId_IsInvalid()
    {
        var invented = Guid.CreateVersion7();

        var validation = CitationValidation.Validate([invented], RetrievalSet);

        Assert.False(validation.AllValid);
        Assert.Equal([invented], validation.Invalid);
    }

    [Fact]
    public void NoCitations_IsValid()
    {
        var validation = CitationValidation.Validate([], RetrievalSet);

        Assert.True(validation.AllValid);
        Assert.Empty(validation.Valid);
        Assert.Empty(validation.Invalid);
    }

    [Fact]
    public void AnEmptyRetrievalSet_MakesEveryCitationInvalid()
    {
        var validation = CitationValidation.Validate([InSet1], new HashSet<Guid>());

        Assert.False(validation.AllValid);
        Assert.Equal([InSet1], validation.Invalid);
    }

    [Fact]
    public void CitingOnlySomeOfTheSources_IsValid()
    {
        // Retrieved-but-uncited is ordinary: five sources offered, one relied on.
        var validation = CitationValidation.Validate([InSet2], RetrievalSet);

        Assert.True(validation.AllValid);
        Assert.Equal([InSet2], validation.Valid);
    }
}
