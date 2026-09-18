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

    [Fact]
    public void Create_LeavesTheTicketUnresolved()
    {
        var ticket = Ticket.Create("some subject", "some body");

        Assert.False(ticket.IsResolved);
        Assert.Null(ticket.Resolution);
        Assert.Null(ticket.ResolvedAt);
    }

    [Fact]
    public void Resolve_RecordsTheApprovedResolution()
    {
        var ticket = Ticket.Create("some subject", "some body");
        var before = DateTimeOffset.UtcNow;

        ticket.Resolve("Reset the password from the account page.");

        Assert.True(ticket.IsResolved);
        Assert.Equal("Reset the password from the account page.", ticket.Resolution);
        Assert.InRange(ticket.ResolvedAt!.Value, before.AddTicks(-10), DateTimeOffset.UtcNow);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Resolve_WithMissingResolution_Throws(string? resolution)
    {
        var ticket = Ticket.Create("some subject", "some body");

        Assert.Throws<ArgumentException>(() => ticket.Resolve(resolution!));
    }

    [Fact]
    public void Resolve_WhenAlreadyResolved_Throws()
    {
        var ticket = Ticket.Create("some subject", "some body");
        ticket.Resolve("first");

        Assert.Throws<InvalidOperationException>(() => ticket.Resolve("second"));
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
