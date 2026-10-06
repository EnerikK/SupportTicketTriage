using SupportTicketTriage.Application.Review;

namespace SupportTicketTriage.UnitTests;

/// Every rejection here is a review-workflow rule rather than input
/// formatting: each one is a submission the system refuses to record at all,
/// which is why they are decided in Application and tested directly.
public class ReviewSubmissionTests
{
    private const string Draft = "Both charges relate to a single order.";

    private static bool TryCreate(
        ReviewDecision decision,
        out ReviewSubmission? submission,
        out string failure,
        string? editedText = null,
        string? rejectionReason = null) =>
        ReviewSubmission.TryCreate(decision, Draft, editedText, rejectionReason, out submission, out failure);

    [Fact]
    public void ApprovingAsWritten_KeepsTheDraftTextAndRecordsNoEdit()
    {
        Assert.True(TryCreate(ReviewDecision.Approved, out var submission, out _));

        Assert.Equal(Draft, submission!.FinalText);
        Assert.False(submission.WasEdited);
        Assert.Null(submission.RejectionReason);
    }

    [Fact]
    public void ApprovingWithIdenticalText_IsNotAnEdit()
    {
        Assert.True(TryCreate(ReviewDecision.Approved, out var submission, out _, editedText: Draft));

        Assert.Equal(Draft, submission!.FinalText);
        Assert.False(submission.WasEdited);
    }

    /// Trimming before comparing is the reason this is not an edit. A stray
    /// trailing newline from a textarea is not a reviewer changing anything.
    [Fact]
    public void ApprovingWithOnlySurroundingWhitespaceAdded_IsNotAnEdit()
    {
        Assert.True(TryCreate(ReviewDecision.Approved, out var submission, out _, editedText: $"  {Draft}\n"));

        Assert.Equal(Draft, submission!.FinalText);
        Assert.False(submission.WasEdited);
    }

    [Fact]
    public void ApprovingWithChangedText_StoresTheReviewersTextAndRecordsTheEdit()
    {
        const string edited = "Both charges relate to a single order. Nothing further is owed.";

        Assert.True(TryCreate(ReviewDecision.Approved, out var submission, out _, editedText: edited));

        Assert.Equal(edited, submission!.FinalText);
        Assert.True(submission.WasEdited);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n")]
    public void ApprovingWithEmptyReplacementText_IsRejected(string editedText)
    {
        Assert.False(TryCreate(ReviewDecision.Approved, out var submission, out var failure, editedText: editedText));

        Assert.Null(submission);
        Assert.Contains("cannot be empty", failure);
    }

    /// A reason on an approval would record a decision that reads as a
    /// rejection to anyone querying the table later.
    [Fact]
    public void ApprovingWithARejectionReason_IsRejected()
    {
        Assert.False(TryCreate(
            ReviewDecision.Approved, out var submission, out var failure, rejectionReason: "Not accurate."));

        Assert.Null(submission);
        Assert.Contains("cannot accompany an approval", failure);
    }

    [Fact]
    public void RejectingWithAReason_RecordsTheReasonAndNoFinalText()
    {
        Assert.True(TryCreate(
            ReviewDecision.Rejected, out var submission, out _, rejectionReason: "  Cites the wrong precedent.  "));

        Assert.Equal(ReviewDecision.Rejected, submission!.Decision);
        Assert.Equal("Cites the wrong precedent.", submission.RejectionReason);
        Assert.Null(submission.FinalText);
        Assert.False(submission.WasEdited);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void RejectingWithoutAReason_IsRejected(string? rejectionReason)
    {
        Assert.False(TryCreate(
            ReviewDecision.Rejected, out var submission, out var failure, rejectionReason: rejectionReason));

        Assert.Null(submission);
        Assert.Contains("must record a reason", failure);
    }

    /// Replacement text on a rejection has nowhere to go: a rejected draft
    /// resolves nothing and contributes nothing to the corpus.
    [Fact]
    public void RejectingWithReplacementText_IsRejected()
    {
        Assert.False(TryCreate(
            ReviewDecision.Rejected,
            out var submission,
            out var failure,
            editedText: "Here is a better reply.",
            rejectionReason: "The draft was wrong."));

        Assert.Null(submission);
        Assert.Contains("cannot carry replacement text", failure);
    }

    /// The enum starts at 1, so a field nobody set is not silently an
    /// approval.
    [Theory]
    [InlineData(0)]
    [InlineData(99)]
    public void ADecisionOutsideTheClosedSet_IsRejected(int value)
    {
        Assert.False(TryCreate((ReviewDecision)value, out var submission, out var failure));

        Assert.Null(submission);
        Assert.Contains("not one of Approved or Rejected", failure);
    }
}
