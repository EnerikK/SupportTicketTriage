using System.Text.RegularExpressions;
using SupportTicketTriage.Eval;

namespace SupportTicketTriage.UnitTests;

/// The parser used to ignore what it did not recognise, which is the one
/// behaviour a regression gate cannot tolerate: a mistyped flag that exits zero
/// reports success for a check that never ran.
public class HarnessArgumentsTests
{
    [Fact]
    public void AFlagWithAValueIsParsed()
    {
        var args = HarnessArguments.Parse(["--connection", "Host=localhost"]);

        Assert.True(args.Has("connection"));
        Assert.Equal("Host=localhost", args.Value("connection"));
    }

    [Fact]
    public void AFlagWithNoValueIsPresentWithANullValue()
    {
        var args = HarnessArguments.Parse(["--reset"]);

        Assert.True(args.Has("reset"));
        Assert.Null(args.Value("reset"));
    }

    [Fact]
    public void AValuelessFlagFollowedByAnotherFlagDoesNotSwallowIt()
    {
        var args = HarnessArguments.Parse(["--reset", "--skip-generation"]);

        Assert.True(args.Has("reset"));
        Assert.True(args.Has("skip-generation"));
    }

    [Fact]
    public void FlagsAreCaseInsensitive()
    {
        var args = HarnessArguments.Parse(["--CONNECTION", "x"]);

        Assert.Equal("x", args.Value("connection"));
    }

    /// The whole point of the type.
    [Fact]
    public void AnUnrecognisedFlagIsRejectedRatherThanIgnored()
    {
        var error = Assert.Throws<ArgumentException>(
            () => HarnessArguments.Parse(["--compare-baselines", "eval/baseline.json"]));

        Assert.Contains("--compare-baselines", error.Message);
    }

    /// A near-miss is the realistic case - the flag exists, the spelling does
    /// not - and the message has to show what was expected or the reader is
    /// left guessing which of eight flags they meant.
    [Fact]
    public void TheErrorNamesTheRecognisedFlags()
    {
        var error = Assert.Throws<ArgumentException>(
            () => HarnessArguments.Parse(["--write-baselines"]));

        Assert.Contains("--write-baseline", error.Message);
        Assert.Contains("--skip-generation", error.Message);
    }

    [Fact]
    public void EveryUnknownFlagIsReportedRatherThanOnlyTheFirst()
    {
        var error = Assert.Throws<ArgumentException>(
            () => HarnessArguments.Parse(["--nope", "--also-nope"]));

        Assert.Contains("--nope", error.Message);
        Assert.Contains("--also-nope", error.Message);
    }

    /// No positional arguments exist, and rejecting stray bare words would turn
    /// a shell-expanded path into a failure for no benefit.
    [Fact]
    public void BareWordsAreIgnoredRatherThanRejected()
    {
        var args = HarnessArguments.Parse(["stray", "--reset", "also-stray"]);

        Assert.True(args.Has("reset"));
    }

    [Fact]
    public void AnEmptyCommandLineParses()
    {
        Assert.False(HarnessArguments.Parse([]).Has("connection"));
    }

    /// The help text and the parser have to agree, or the harness advertises
    /// a flag it rejects - or hides one it accepts.
    ///
    /// This used to compare Recognised against a hand-maintained copy of
    /// itself in this file, which made three copies of the same facts and
    /// guarded the wrong direction. The judge flags shipped documented but
    /// not recognised, and the harness refused every run that used them; this
    /// test could not see it, because neither list it compared was the help
    /// text. It now reads the real usage string.
    [Fact]
    public void TheHelpTextAndTheAllowlistDescribeTheSameFlags()
    {
        var documented = Regex
            .Matches(HarnessHelp.Text, @"^\s*--([a-z-]+)", RegexOptions.Multiline)
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Equal(documented.Order(), HarnessArguments.Recognised.Order());
    }

    [Fact]
    public void ExportJudgePairs_CarriesItsPath()
    {
        var arguments = HarnessArguments.Parse(
            ["--connection", "host", "--export-judge-pairs", "eval/judge/pairs.json"]);

        Assert.Equal("eval/judge/pairs.json", arguments.Value("export-judge-pairs"));
    }
}
