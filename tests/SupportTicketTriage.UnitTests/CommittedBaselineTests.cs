using SupportTicketTriage.Eval.Baseline;

namespace SupportTicketTriage.UnitTests;

/// The committed baselines, loaded by the code that has to read them.
///
/// **This is the test that would have caught the last failure.** The previous
/// baseline was a serialised report; six properties were added to that record
/// afterwards and the file silently stopped being readable. Nothing noticed,
/// because nothing ever loaded it - `--write-baseline` wrote it and no code
/// path read it back.
///
/// So these do the one thing that was missing: open what is actually in the
/// repository and run it through the real loader and validator. A field added
/// without regenerating the file now fails here, at `dotnet test`, rather than
/// in CI on a merge to main.
public class CommittedBaselineTests
{
    /// Walks up from the test binary to the repository root. Tests run out of
    /// bin/Release/net10.0, so a relative path from the working directory
    /// would be wrong in a way that depends on the configuration.
    private static string RepositoryFile(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SupportTicketTriage.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);

        return Path.Combine([directory.FullName, .. segments]);
    }

    [Fact]
    public async Task TheCommittedEvaluationBaselineLoadsAndValidates()
    {
        var path = RepositoryFile("eval", "baseline", "evaluation-baseline.json");
        Assert.True(File.Exists(path), $"No committed baseline at {path}.");

        var baseline = await EvaluationBaseline.LoadAsync(path);

        Assert.Equal(EvaluationBaseline.CurrentVersion, baseline.Version);
        Assert.NotEmpty(baseline.Metrics.RecallAtK);

        // Written from a real deployment, which is what ADR-007 requires of
        // this file and what makes its quality numbers mean anything.
        Assert.NotEqual("fake-lexical-v1", baseline.Provenance.EmbeddingModel);
        Assert.NotNull(baseline.Provenance.ChatModel);
    }

    [Fact]
    public async Task TheCommittedLogicBaselineLoadsAndValidates()
    {
        var path = RepositoryFile("eval", "baseline", "lexical-logic-baseline.json");
        Assert.True(File.Exists(path), $"No committed logic baseline at {path}.");

        var baseline = await EvaluationBaseline.LoadAsync(path);

        Assert.Equal(EvaluationBaseline.CurrentVersion, baseline.Version);
        Assert.NotEmpty(baseline.Metrics.RecallAtK);

        // The mirror requirement: this one must come from the stand-in, or the
        // pull-request gate would be comparing quality numbers it cannot
        // reproduce without a deployment.
        Assert.Equal("fake-lexical-v1", baseline.Provenance.EmbeddingModel);

        // And it gates retrieval only - an offline run measures nothing else,
        // so anything here would be a figure the gate could never reproduce.
        Assert.Null(baseline.Metrics.ClassificationAccuracy);
        Assert.Null(baseline.Metrics.CitationValidityRate);
        Assert.Null(baseline.Metrics.CostTotal);
    }

    /// The two files must not drift onto different tolerances. They are the
    /// same gate applied to different scopes, and a reader comparing them
    /// should not have to check whether the numbers agree.
    [Fact]
    public async Task BothBaselinesCarryTheSameTolerances()
    {
        var real = await EvaluationBaseline.LoadAsync(
            RepositoryFile("eval", "baseline", "evaluation-baseline.json"));
        var logic = await EvaluationBaseline.LoadAsync(
            RepositoryFile("eval", "baseline", "lexical-logic-baseline.json"));

        Assert.Equal(real.Tolerances, logic.Tolerances);
    }
}
