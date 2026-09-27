using System.Text.Json;

namespace SupportTicketTriage.Application.Generation;

/// What the generation stage is allowed to produce.
///
/// Two fields, and deliberately no third. There is no status, no action, no
/// recipient and no send flag, so there is no field in which the model can
/// express an intent to do anything — which is the first and load-bearing
/// layer of ADR-002. A unit test asserts this shape by reflection, because
/// the guarantee is the absence of a member and absences are easy to lose.
public sealed record DraftResult(string DraftText, IReadOnlyList<Guid> CitedTicketIds)
{
    /// Parses the model's JSON response.
    ///
    /// Strict, for the same reason classification is: an unparseable response
    /// means no draft, not a guessed one.
    public static bool TryParse(string? json, out DraftResult? result, out string failure)
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

        if (!root.TryGetProperty("draft_text", out var textElement)
            || textElement.ValueKind != JsonValueKind.String)
        {
            failure = "The model did not return draft_text.";
            return false;
        }

        var draftText = textElement.GetString();
        if (string.IsNullOrWhiteSpace(draftText))
        {
            failure = "The model returned an empty draft.";
            return false;
        }

        if (!root.TryGetProperty("cited_ticket_ids", out var citationsElement)
            || citationsElement.ValueKind != JsonValueKind.Array)
        {
            failure = "The model did not return a cited_ticket_ids array.";
            return false;
        }

        // Order is preserved and repeats collapse. A duplicate citation is
        // untidy rather than dangerous, so it is not worth failing a draft over.
        var cited = new List<Guid>();
        foreach (var element in citationsElement.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.String || !Guid.TryParse(element.GetString(), out var id))
            {
                failure = "cited_ticket_ids contained a value that is not a ticket id.";
                return false;
            }

            if (!cited.Contains(id))
            {
                cited.Add(id);
            }
        }

        result = new DraftResult(draftText, cited);
        failure = string.Empty;
        return true;
    }
}
