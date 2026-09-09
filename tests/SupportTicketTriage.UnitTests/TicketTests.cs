using SupportTicketTriage.Domain;

namespace SupportTicketTriage.UnitTests;

public class TicketTests
{
    [Fact]
    public void Create_WithValidInput_SetsAllProperties()
    {
        var before = DateTimeOffset.UtcNow;

        var ticket = Ticket.Create("Can't log in", "I get an error on the login page.");

        var after = DateTimeOffset.UtcNow;

        Assert.NotEqual(Guid.Empty, ticket.Id);
        Assert.Equal("Can't log in", ticket.Subject);
        Assert.Equal("I get an error on the login page.", ticket.Body);
        Assert.InRange(ticket.CreatedAt, before, after);
    }

    [Fact]
    public void Create_TruncatesCreatedAtToMicrosecondPrecision()
    {
        var ticket = Ticket.Create("some subject", "some body");

        // Postgres timestamptz only stores microsecond precision (1000ns);
        // a DateTimeOffset tick is 100ns, so the last digit must be zero.
        Assert.Equal(0, ticket.CreatedAt.Ticks % 10);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithMissingSubject_Throws(string? subject)
    {
        Assert.Throws<ArgumentException>(() => Ticket.Create(subject!, "some body"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithMissingBody_Throws(string? body)
    {
        Assert.Throws<ArgumentException>(() => Ticket.Create("some subject", body!));
    }
}
