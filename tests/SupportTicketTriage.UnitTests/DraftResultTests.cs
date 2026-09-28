using SupportTicketTriage.Application.Generation;

namespace SupportTicketTriage.UnitTests;

public class DraftResultTests
{
    private static readonly Guid A = Guid.Parse("0199a000-0000-7000-8000-000000000001");
    private static readonly Guid B = Guid.Parse("0199a000-0000-7000-8000-000000000002");

    [Fact]
    public void WellFormedResponse_Parses()
    {
        var json = $$"""
            {"draft_text": "Your refund was an authorisation hold.",
             "cited_ticket_ids": ["{{A}}", "{{B}}"]}
            """;

        Assert.True(DraftResult.TryParse(json, out var result, out var failure));
        Assert.Equal(string.Empty, failure);
        Assert.Equal("Your refund was an authorisation hold.", result!.DraftText);
        Assert.Equal([A, B], result.CitedTicketIds);
    }

    [Fact]
    public void ADraftCitingNothing_IsStillADraft()
    {
        // Legitimate: the sources may not answer the ticket, and the prompt
        // tells the model to say so rather than invent support.
        var json = """{"draft_text": "The history does not cover this.", "cited_ticket_ids": []}""";

        Assert.True(DraftResult.TryParse(json, out var result, out _));
        Assert.Empty(result!.CitedTicketIds);
    }

    [Fact]
    public void RepeatedCitations_CollapseAndKeepTheirOrder()
    {
        var json = $$"""{"draft_text": "text", "cited_ticket_ids": ["{{B}}", "{{A}}", "{{B}}"]}""";

        Assert.True(DraftResult.TryParse(json, out var result, out _));
        Assert.Equal([B, A], result!.CitedTicketIds);
    }

    [Theory]
    [InlineData("""{"cited_ticket_ids": []}""", "draft_text")]
    [InlineData("""{"draft_text": 42, "cited_ticket_ids": []}""", "draft_text")]
    [InlineData("""{"draft_text": "   ", "cited_ticket_ids": []}""", "empty draft")]
    public void MissingOrEmptyDraftText_IsRejected(string json, string expected)
    {
        Assert.False(DraftResult.TryParse(json, out _, out var failure));
        Assert.Contains(expected, failure);
    }

    [Theory]
    [InlineData("""{"draft_text": "text"}""")]
    [InlineData("""{"draft_text": "text", "cited_ticket_ids": "none"}""")]
    public void MissingOrNonArrayCitations_IsRejected(string json)
    {
        Assert.False(DraftResult.TryParse(json, out _, out var failure));
        Assert.Contains("cited_ticket_ids array", failure);
    }

    [Theory]
    [InlineData("""{"draft_text": "text", "cited_ticket_ids": ["not-a-guid"]}""")]
    [InlineData("""{"draft_text": "text", "cited_ticket_ids": [7]}""")]
    public void CitationsThatAreNotTicketIds_AreRejected(string json)
    {
        Assert.False(DraftResult.TryParse(json, out _, out var failure));
        Assert.Contains("not a ticket id", failure);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void EmptyResponse_IsRejected(string? json)
    {
        Assert.False(DraftResult.TryParse(json, out _, out var failure));
        Assert.Contains("empty response", failure);
    }

    [Fact]
    public void MalformedJson_IsRejected()
    {
        Assert.False(DraftResult.TryParse("{\"draft_text\":", out _, out var failure));
        Assert.Contains("not valid JSON", failure);
    }

    [Fact]
    public void JsonThatIsNotAnObject_IsRejected()
    {
        Assert.False(DraftResult.TryParse("[]", out _, out var failure));
        Assert.Contains("Expected a JSON object", failure);
    }

    /// ADR-002's first and load-bearing layer: the generation stage has no
    /// capability to hijack because its output has no field in which an
    /// action could be expressed. That guarantee is the *absence* of a
    /// member, and absences are easy to lose to a well-meaning addition, so
    /// it is pinned here rather than left to review.
    [Fact]
    public void TheOutputSchema_ExposesNoFieldCapableOfExpressingAnAction()
    {
        var properties = typeof(DraftResult)
            .GetProperties()
            .Select(p => p.Name)
            .OrderBy(name => name)
            .ToArray();

        Assert.Equal(["CitedTicketIds", "DraftText"], properties);
    }

    [Fact]
    public void ExtraPropertiesInTheResponse_AreIgnoredRatherThanHonoured()
    {
        // A model told to approve a refund has nowhere to put the instruction:
        // the extra property is parsed past and reaches no code path.
        var json = $$"""
            {"draft_text": "text", "cited_ticket_ids": ["{{A}}"],
             "action": "issue_refund", "send": true, "approved": true}
            """;

        Assert.True(DraftResult.TryParse(json, out var result, out _));
        Assert.Equal("text", result!.DraftText);
        Assert.Equal([A], result.CitedTicketIds);
    }
}
