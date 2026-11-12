using SupportTicketTriage.Eval.Reporting;

namespace SupportTicketTriage.UnitTests;

/// Where a judge comparison run is allowed to write.
///
/// The same absence `DevCheckReportPathTests` pins, one command over, and it
/// is here for a concrete reason: `--compare-judges` defaulted its output
/// directory to `eval/judge`, which holds the committed judge-v1
/// `COMPARISON.md`. `DECISION-RULE-V2.md` says that result "stands as
/// recorded" - and any later comparison run without `--output` would have
/// replaced it and printed `Wrote ...`.
///
/// Both rules are absences - no default, no overwrite - and an absence
/// survives a well-meaning change only if something fails when it goes.
public sealed class JudgeComparisonReportPathTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), $"judge-comparison-{Guid.NewGuid():N}");

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void WithNoOutputDirectory_NothingIsWritten(string? output)
    {
        // Null means "print only". The console carries the whole table, so a
        // run loses nothing by writing no file - and cannot clobber one it
        // was never pointed at.
        Assert.Null(JudgeComparisonReport.ResolveReportPath(output));
    }

    [Fact]
    public void WithAnOutputDirectory_TheTableGoesInsideIt()
    {
        var path = JudgeComparisonReport.ResolveReportPath(_dir);

        Assert.NotNull(path);
        Assert.Equal(Path.Combine(_dir, "COMPARISON.md"), path);
    }

    [Fact]
    public void AnOutputDirectoryThatDoesNotExistYetIsAllowed()
    {
        // The directory is created at write time. Refusing here would force a
        // caller to mkdir before every run, and the guard is about not
        // destroying an existing table, not about where new ones may go.
        Assert.False(Directory.Exists(_dir));

        Assert.Equal(
            Path.Combine(_dir, "COMPARISON.md"),
            JudgeComparisonReport.ResolveReportPath(_dir));
    }

    [Fact]
    public void AnExistingTableIsNeverOverwritten()
    {
        Directory.CreateDirectory(_dir);
        var existing = Path.Combine(_dir, "COMPARISON.md");
        File.WriteAllText(existing, "the judge-v1 result");

        var ex = Assert.Throws<InvalidOperationException>(
            () => JudgeComparisonReport.ResolveReportPath(_dir));

        // The message names the file because the caller's next action is to
        // pick another directory or remove that one deliberately.
        Assert.Contains("COMPARISON.md", ex.Message);
        Assert.Contains("--output", ex.Message);

        // And the refusal leaves the record intact. A guard that threw after
        // truncating would be worse than no guard at all.
        Assert.Equal("the judge-v1 result", File.ReadAllText(existing));
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }
}
