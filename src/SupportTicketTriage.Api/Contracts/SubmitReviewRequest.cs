namespace SupportTicketTriage.Api.Contracts;

/// A reviewer's decision, as it arrives on the wire.
///
/// `Decision` is a string parsed strictly against the closed set rather than a
/// bound enum: an unrecognised value must be a 400 naming what was expected,
/// not a silent fall back to whichever member happens to be zero.
///
/// There is no field here that could express sending the reply anywhere. That
/// is not an omission to be fixed later — no send path exists in the system.
public sealed record SubmitReviewRequest(string Decision, string? FinalText, string? RejectionReason);
