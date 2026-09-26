using SupportTicketTriage.Application.Classification;
using SupportTicketTriage.Domain;

namespace SupportTicketTriage.UnitTests;

/// Every rejection here is a routing decision: a ticket whose classification
/// cannot be parsed goes to manual triage instead of being stored wrong.
public class ClassificationResultTests
{
    [Fact]
    public void WellFormedResponse_Parses()
    {
        var json = """{"category": "Billing", "priority": "High", "score": 0.82}""";

        Assert.True(ClassificationResult.TryParse(json, out var result, out var failure));
        Assert.Equal(string.Empty, failure);
        Assert.Equal(TicketCategory.Billing, result!.Category);
        Assert.Equal(TicketPriority.High, result.Priority);
        Assert.Equal(0.82, result.SelfReportedScore);
    }

    [Theory]
    [InlineData("Billing", TicketCategory.Billing)]
    [InlineData("Account & Login", TicketCategory.AccountAndLogin)]
    [InlineData("Technical Fault", TicketCategory.TechnicalFault)]
    [InlineData("Shipping & Delivery", TicketCategory.ShippingAndDelivery)]
    [InlineData("Returns & Refunds", TicketCategory.ReturnsAndRefunds)]
    [InlineData("Product Question", TicketCategory.ProductQuestion)]
    public void EveryDatasetLabel_Parses(string label, TicketCategory expected)
    {
        var json = $$"""{"category": "{{label}}", "priority": "Normal", "score": 0.5}""";

        Assert.True(ClassificationResult.TryParse(json, out var result, out _));
        Assert.Equal(expected, result!.Category);
    }

    [Theory]
    [InlineData("billing")]
    [InlineData("  Billing  ")]
    public void LabelMatching_IgnoresCaseAndSurroundingWhitespace(string label)
    {
        var json = $$"""{"category": "{{label}}", "priority": "low", "score": 0.4}""";

        Assert.True(ClassificationResult.TryParse(json, out var result, out _));
        Assert.Equal(TicketCategory.Billing, result!.Category);
        Assert.Equal(TicketPriority.Low, result.Priority);
    }

    /// The enum member name is not an accepted spelling. Allowing it would let
    /// two different strings mean the same category, and the evaluation set
    /// only ever uses the label form.
    [Fact]
    public void EnumMemberName_IsNotAcceptedAsALabel()
    {
        var json = """{"category": "AccountAndLogin", "priority": "Normal", "score": 0.9}""";

        Assert.False(ClassificationResult.TryParse(json, out _, out var failure));
        Assert.Contains("known categories", failure);
    }

    [Fact]
    public void UnknownCategory_IsRejected()
    {
        var json = """{"category": "Warranty Claim", "priority": "Normal", "score": 0.9}""";

        Assert.False(ClassificationResult.TryParse(json, out var result, out var failure));
        Assert.Null(result);
        Assert.Contains("known categories", failure);
    }

    [Fact]
    public void UnknownPriority_IsRejected()
    {
        var json = """{"category": "Billing", "priority": "Urgent", "score": 0.9}""";

        Assert.False(ClassificationResult.TryParse(json, out _, out var failure));
        Assert.Contains("known priorities", failure);
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    public void ScoreOutsideZeroToOne_IsRejectedRatherThanClamped(double score)
    {
        var json = $$"""{"category": "Billing", "priority": "Normal", "score": {{score}}}""";

        Assert.False(ClassificationResult.TryParse(json, out _, out var failure));
        Assert.Contains("outside the range", failure);
    }

    [Theory]
    [InlineData("""{"category": "Billing", "priority": "Normal"}""")]
    [InlineData("""{"category": "Billing", "priority": "Normal", "score": "high"}""")]
    [InlineData("""{"category": "Billing", "priority": "Normal", "score": null}""")]
    public void MissingOrNonNumericScore_IsRejected(string json)
    {
        Assert.False(ClassificationResult.TryParse(json, out _, out var failure));
        Assert.Contains("numeric score", failure);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void EmptyResponse_IsRejected(string? json)
    {
        Assert.False(ClassificationResult.TryParse(json, out _, out var failure));
        Assert.Contains("empty response", failure);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("""{"category": "Billing", """)]
    public void MalformedJson_IsRejected(string json)
    {
        Assert.False(ClassificationResult.TryParse(json, out _, out var failure));
        Assert.Contains("not valid JSON", failure);
    }

    [Theory]
    [InlineData("""["Billing"]""")]
    [InlineData("\"Billing\"")]
    [InlineData("42")]
    public void JsonThatIsNotAnObject_IsRejected(string json)
    {
        Assert.False(ClassificationResult.TryParse(json, out _, out var failure));
        Assert.Contains("Expected a JSON object", failure);
    }

    [Fact]
    public void UnexpectedExtraProperties_AreIgnored()
    {
        var json = """
            {"category": "Billing", "priority": "Normal", "score": 0.7,
             "reasoning": "the customer mentions a charge", "action": "issue_refund"}
            """;

        // An extra property is not a reason to reject a valid classification,
        // and "action" in particular has nowhere to go: the result type has no
        // field capable of expressing one.
        Assert.True(ClassificationResult.TryParse(json, out var result, out _));
        Assert.Equal(TicketCategory.Billing, result!.Category);
    }
}
