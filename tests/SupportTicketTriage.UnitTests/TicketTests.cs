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
        Assert.Null(ticket.ResolutionSource);
    }

    [Theory]
    [InlineData(ResolutionSource.Imported)]
    [InlineData(ResolutionSource.ApprovedDraft)]
    public void Resolve_RecordsTheResolutionAndWhereItCameFrom(ResolutionSource source)
    {
        var ticket = Ticket.Create("some subject", "some body");
        var before = DateTimeOffset.UtcNow;

        ticket.Resolve("Reset the password from the account page.", source);

        Assert.True(ticket.IsResolved);
        Assert.Equal("Reset the password from the account page.", ticket.Resolution);
        Assert.InRange(ticket.ResolvedAt!.Value, before.AddTicks(-10), DateTimeOffset.UtcNow);
        Assert.Equal(source, ticket.ResolutionSource);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Resolve_WithMissingResolution_Throws(string? resolution)
    {
        var ticket = Ticket.Create("some subject", "some body");

        Assert.Throws<ArgumentException>(() => ticket.Resolve(resolution!, ResolutionSource.Imported));
        Assert.Null(ticket.ResolutionSource);
    }

    // Zero is the default of an unset field, which is why the enum starts at 1.
    [Theory]
    [InlineData(0)]
    [InlineData(99)]
    public void Resolve_WithUndefinedSource_Throws(int source)
    {
        var ticket = Ticket.Create("some subject", "some body");

        Assert.Throws<ArgumentOutOfRangeException>(() => ticket.Resolve("some resolution", (ResolutionSource)source));
        Assert.False(ticket.IsResolved);
    }

    [Fact]
    public void Resolve_WhenAlreadyResolved_Throws()
    {
        var ticket = Ticket.Create("some subject", "some body");
        ticket.Resolve("first", ResolutionSource.Imported);

        // The migration backfill relies on this: an imported ticket can never
        // later be relabelled as an approved draft.
        Assert.Throws<InvalidOperationException>(() => ticket.Resolve("second", ResolutionSource.ApprovedDraft));
        Assert.Equal("first", ticket.Resolution);
        Assert.Equal(ResolutionSource.Imported, ticket.ResolutionSource);
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
