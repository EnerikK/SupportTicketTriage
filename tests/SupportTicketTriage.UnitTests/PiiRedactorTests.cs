using SupportTicketTriage.Application.Redaction;

namespace SupportTicketTriage.UnitTests;

public class PiiRedactorTests
{
    private readonly PiiRedactor _redactor = new();

    [Theory]
    [InlineData("Contact me at jane.doe@example.com please", "Contact me at [EMAIL] please")]
    [InlineData("two: a@b.co and c+tag@d.example.org", "two: [EMAIL] and [EMAIL]")]
    public void Redact_RemovesEmailAddresses(string input, string expected)
    {
        Assert.Equal(expected, _redactor.Redact(input));
    }

    [Theory]
    [InlineData("card 4111111111111111 declined", "card [CARD] declined")]
    [InlineData("card 4111 1111 1111 1111 declined", "card [CARD] declined")]
    [InlineData("card 4111-1111-1111-1111 declined", "card [CARD] declined")]
    public void Redact_RemovesCardNumbers(string input, string expected)
    {
        Assert.Equal(expected, _redactor.Redact(input));
    }

    [Theory]
    [InlineData("call 5551234567 now", "call [PHONE] now")]
    [InlineData("call +44 20 7946 0958 now", "call [PHONE] now")]
    [InlineData("call (555) 123-4567 now", "call [PHONE] now")]
    public void Redact_RemovesPhoneNumbers(string input, string expected)
    {
        Assert.Equal(expected, _redactor.Redact(input));
    }

    [Fact]
    public void Redact_RemovesIpAddresses()
    {
        Assert.Equal("from [IP] repeatedly", _redactor.Redact("from 192.168.1.100 repeatedly"));
    }

    [Fact]
    public void Redact_LeavesNonSensitiveTextUntouched()
    {
        const string input = "The checkout page returns a 500 error after I click Pay.";

        Assert.Equal(input, _redactor.Redact(input));
    }

    [Fact]
    public void Redact_IsIdempotent()
    {
        const string input = "mail jane@example.com or call 5551234567 from 10.0.0.1";

        var once = _redactor.Redact(input);
        var twice = _redactor.Redact(once);

        Assert.Equal(once, twice);
    }

    [Fact]
    public void Redact_RemovesEveryIdentifierWhenSeveralAppearTogether()
    {
        var redacted = _redactor.Redact(
            "jane@example.com paid with 4111111111111111 from 10.0.0.1, call 5551234567");

        Assert.DoesNotContain("jane@example.com", redacted);
        Assert.DoesNotContain("4111111111111111", redacted);
        Assert.DoesNotContain("10.0.0.1", redacted);
        Assert.DoesNotContain("5551234567", redacted);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Redact_HandlesEmptyInput(string input)
    {
        Assert.Equal(input, _redactor.Redact(input));
    }
}
