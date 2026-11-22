using SupportTicketTriage.Application.Dashboard;

namespace SupportTicketTriage.UnitTests;

public class DashboardPeriodTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 18, 14, 22, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("7d", 7)]
    [InlineData("30d", 30)]
    [InlineData("90d", 90)]
    [InlineData(" 30D ", 30)]
    public void TheThreeRanges_AreAccepted(string range, int days)
    {
        Assert.True(DashboardPeriod.TryCreate(range, Now, out var period));

        Assert.Equal(days, period!.Days);
        Assert.Equal($"{days}d", period.Range);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("30")]
    [InlineData("1d")]
    [InlineData("365d")]
    public void AnythingElse_IsRefused(string? range)
    {
        Assert.False(DashboardPeriod.TryCreate(range, Now, out var period));
        Assert.Null(period);
    }

    /// Whole UTC days ending at the start of tomorrow, so today is included as
    /// far as it has got; the previous period is the same length, just before.
    [Fact]
    public void APeriod_EndsAtTheStartOfTomorrow()
    {
        DashboardPeriod.TryCreate("30d", Now, out var period);

        Assert.Equal(new DateTimeOffset(2026, 11, 19, 0, 0, 0, TimeSpan.Zero), period!.To);
        Assert.Equal(new DateTimeOffset(2026, 10, 20, 0, 0, 0, TimeSpan.Zero), period.From);
        Assert.Equal(new DateTimeOffset(2026, 9, 20, 0, 0, 0, TimeSpan.Zero), period.PreviousFrom);
    }

    /// 01:30 in Athens is still the previous evening in UTC, and the period
    /// follows UTC whatever offset the caller's clock carries.
    [Fact]
    public void Tomorrow_IsTheNextUtcDay_WhateverTheCallersOffset()
    {
        var athens = new DateTimeOffset(2026, 11, 19, 1, 30, 0, TimeSpan.FromHours(2));

        DashboardPeriod.TryCreate("7d", athens, out var period);

        Assert.Equal(new DateTimeOffset(2026, 11, 19, 0, 0, 0, TimeSpan.Zero), period!.To);
        Assert.Equal(TimeSpan.Zero, period.To.Offset);
    }

    [Fact]
    public void EachDay_ListsEveryDayOfThePeriod_OldestFirst()
    {
        DashboardPeriod.TryCreate("7d", Now, out var period);

        var days = period!.EachDay();

        Assert.Equal(7, days.Count);
        Assert.Equal(new DateOnly(2026, 11, 12), days[0]);
        Assert.Equal(new DateOnly(2026, 11, 18), days[^1]);
    }
}
