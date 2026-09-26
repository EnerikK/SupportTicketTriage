using System.Text.Json;
using SupportTicketTriage.Domain;

namespace SupportTicketTriage.Application.Classification;

/// A classification the application is willing to act on.
///
/// Constructing one is only possible through <see cref="TryParse"/>, so an
/// unvalidated model response can never be mistaken for a usable result.
///
/// SelfReportedScore is named for what it is: the model's own estimate, not a
/// calibrated probability. ADR-003 makes it one of two independent gates
/// rather than a confidence figure standing on its own.
public sealed record ClassificationResult(
    TicketCategory Category,
    TicketPriority Priority,
    double SelfReportedScore)
{
    /// Parses the model's JSON response.
    ///
    /// Every rejection path here is a routing decision: a ticket whose
    /// classification cannot be parsed goes to manual triage. That is why this
    /// lives in Application and is unit-tested directly, rather than being
    /// buried in the Infrastructure service that makes the call.
    public static bool TryParse(string? json, out ClassificationResult? result, out string failure)
    {
        result = null;

        if (string.IsNullOrWhiteSpace(json))
        {
            failure = "The model returned an empty response.";
            return false;
        }

        JsonElement root;
        try
        {
            using var document = JsonDocument.Parse(json);
            root = document.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            failure = $"The model response was not valid JSON: {ex.Message}";
            return false;
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            failure = $"Expected a JSON object, got {root.ValueKind}.";
            return false;
        }

        if (!root.TryGetProperty("category", out var categoryElement)
            || categoryElement.ValueKind != JsonValueKind.String
            || !TicketCategories.TryFromLabel(categoryElement.GetString(), out var category))
        {
            failure = "The model did not return one of the known categories.";
            return false;
        }

        if (!root.TryGetProperty("priority", out var priorityElement)
            || priorityElement.ValueKind != JsonValueKind.String
            || !TicketPriorities.TryFromLabel(priorityElement.GetString(), out var priority))
        {
            failure = "The model did not return one of the known priorities.";
            return false;
        }

        if (!root.TryGetProperty("score", out var scoreElement)
            || scoreElement.ValueKind != JsonValueKind.Number
            || !scoreElement.TryGetDouble(out var score))
        {
            failure = "The model did not return a numeric score.";
            return false;
        }

        // Out-of-range is rejected rather than clamped. Clamping would turn a
        // model that misunderstood the scale into a confident-looking result,
        // and the gate in ADR-003 compares this value against a floor.
        if (double.IsNaN(score) || score < 0 || score > 1)
        {
            failure = $"Score {score} is outside the range 0 to 1.";
            return false;
        }

        result = new ClassificationResult(category, priority, score);
        failure = string.Empty;
        return true;
    }
}
