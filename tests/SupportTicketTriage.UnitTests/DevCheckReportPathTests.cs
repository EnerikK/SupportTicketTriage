using SupportTicketTriage.Eval.Judging;

namespace SupportTicketTriage.UnitTests;

/// Where a dev-check run is allowed to write.
///
/// These exist because the old behaviour destroyed a committed document. The
/// report path defaulted to `eval/judge/DEV-CHECK.md` — the analysis of three
/// configurations across 26 cases — and a two-case smoke run with no
/// `--output` replaced it and reported success. It was recoverable only
/// because the file happened to be committed and the tree happened to be
/// clean.
///
/// Both rules below are absences: no default, and no overwrite. Absences are
/// lost to well-meaning changes unless something fails when they go, which is
/// the same reason `DraftResult`'s shape is pinned by reflection.
public sealed class DevCheckReportPathTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), $"devcheck-{Guid.NewGuid():N}");

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void WithNoOutputDirectory_NothingIsWritten(string? output)
    {
        // Null means "print only". The console already carries the whole
        // report, so a run loses nothing by not writing a file - and cannot
        // clobber one it was never told about.
        Assert.Null(DevCheck.ResolveReportPath(output));
    }

    [Fact]
    public void WithAnOutputDirectory_TheReportGoesInsideIt()
    {
        var path = DevCheck.ResolveReportPath(_dir);

        Assert.NotNull(path);
        Assert.Equal(Path.Combine(_dir, "DEV-CHECK.md"), path);
    }

    [Fact]
    public void AnExistingReportIsNeverOverwritten()
    {
        Directory.CreateDirectory(_dir);
        var existing = Path.Combine(_dir, "DEV-CHECK.md");
        File.WriteAllText(existing, "the previous run's record");

        var ex = Assert.Throws<InvalidOperationException>(() => DevCheck.ResolveReportPath(_dir));

        // The message has to name the file, because the caller's next action
        // is to choose a different directory or remove that one deliberately.
        Assert.Contains("DEV-CHECK.md", ex.Message);
        Assert.Contains("--output", ex.Message);

        // And the refusal must leave the record intact - a guard that throws
        // after truncating would be worse than no guard.
        Assert.Equal("the previous run's record", File.ReadAllText(existing));
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }
}
