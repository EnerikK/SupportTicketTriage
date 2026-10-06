namespace SupportTicketTriage.Application.Review;

/// What a reviewer decided about a draft.
///
/// Numbered from 1 so that an unset field is never a valid decision. A zero
/// default that happened to mean "Approved" is the kind of bug that only
/// shows up once, in the one place it matters.
public enum ReviewDecision
{
    Approved = 1,
    Rejected = 2,
}

/// A review the application is willing to record.
///
/// Constructing one is only possible through <see cref="TryCreate"/>, so an
/// unvalidated reviewer submission can never reach the database. Every
/// rejection path here is a rule about the review workflow rather than input
/// formatting, which is why this lives in Application and is unit-tested
/// directly — the same argument that placed classification parsing here.
public sealed record ReviewSubmission(
    ReviewDecision Decision,
    string? FinalText,
    bool WasEdited,
    string? RejectionReason)
{
    public static bool TryCreate(
        ReviewDecision decision,
        string draftText,
        string? editedText,
        string? rejectionReason,
        out ReviewSubmission? submission,
        out string failure)
    {
        submission = null;

        if (!Enum.IsDefined(decision))
        {
            failure = "The review decision is not one of Approved or Rejected.";
            return false;
        }

        return decision == ReviewDecision.Approved
            ? TryApprove(draftText, editedText, rejectionReason, out submission, out failure)
            : TryReject(editedText, rejectionReason, out submission, out failure);
    }

    private static bool TryApprove(
        string draftText,
        string? editedText,
        string? rejectionReason,
        out ReviewSubmission? submission,
        out string failure)
    {
        submission = null;

        // A reason belongs to a rejection. Accepting one here would record an
        // approval that reads as a rejection to anyone querying the table.
        if (!string.IsNullOrWhiteSpace(rejectionReason))
        {
            failure = "A rejection reason cannot accompany an approval.";
            return false;
        }

        if (editedText is null)
        {
            // Approved as written. The draft's text becomes the resolution:
            // ADR-011 stores the reviewer's final text in every case, and when
            // they changed nothing their final text is the draft's.
            submission = new ReviewSubmission(ReviewDecision.Approved, draftText.Trim(), WasEdited: false, null);
            failure = string.Empty;
            return true;
        }

        if (string.IsNullOrWhiteSpace(editedText))
        {
            failure = "An edited reply cannot be empty.";
            return false;
        }

        // Compared after trimming, and compared once, here, at the moment of
        // the decision. Trimming means a stray trailing newline is not an
        // edit; ordinal comparison means a genuine rewording is, even when it
        // says the same thing. Both inputs are immutable after this point, so
        // the stored flag can never drift from the text it describes.
        var finalText = editedText.Trim();

        submission = new ReviewSubmission(
            ReviewDecision.Approved,
            finalText,
            WasEdited: !string.Equals(finalText, draftText.Trim(), StringComparison.Ordinal),
            null);

        failure = string.Empty;
        return true;
    }

    private static bool TryReject(
        string? editedText,
        string? rejectionReason,
        out ReviewSubmission? submission,
        out string failure)
    {
        submission = null;

        // Replacement text on a rejection is a contradiction: the text would
        // have nowhere to go, because a rejected draft resolves nothing and
        // contributes nothing to the corpus.
        if (editedText is not null)
        {
            failure = "A rejected draft cannot carry replacement text.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(rejectionReason))
        {
            failure = "A rejection must record a reason.";
            return false;
        }

        submission = new ReviewSubmission(ReviewDecision.Rejected, null, WasEdited: false, rejectionReason.Trim());
        failure = string.Empty;
        return true;
    }
}
