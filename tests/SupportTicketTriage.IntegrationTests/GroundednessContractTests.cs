using SupportTicketTriage.Eval.Judging;
using SupportTicketTriage.Infrastructure.Ai;

namespace SupportTicketTriage.IntegrationTests;

/// The rubric asks for a shape; the judge parses a shape. Nothing held the
/// two together.
///
/// That gap cost a dev-check run. The rubric was changed to return
/// `unsupported_claims` as an array while the parser still read a singular
/// `unsupported_claim`, so every mismatch reported "no claims returned" and
/// eight diagnostics were blank until it was noticed by eye. Both halves
/// compiled and both ran.
public class GroundednessContractTests
{
    private static readonly List<EvidenceSource> Source =
    [
        new(Guid.CreateVersion7(), "A subject", "A body.", "A resolution."),
    ];

    private static async Task<GroundednessCase> JudgeReturning(string response)
    {
        var chat = new FakeChatClient { ResponseText = response };

        return await new GroundednessJudge(chat, "test-judge")
            .JudgeAsync("q-1", "A draft.", "A ticket.", Source);
    }

    /// The example the rubric literally shows the model must parse. If it does
    /// not, the judge is asking for output it cannot read.
    [Fact]
    public async Task TheExampleOutputInTheRubricParses()
    {
        var result = await JudgeReturning(GroundednessPrompt.ExampleOutput);

        Assert.False(result.Grounded);
        Assert.Equal("a quoted claim | another", result.UnsupportedClaim);
    }

    [Fact]
    public async Task TheGroundedExampleOutputParses()
    {
        var result = await JudgeReturning(GroundednessPrompt.ExampleGroundedOutput);

        Assert.True(result.Grounded);
        Assert.Null(result.UnsupportedClaim);
    }

    /// The example has to appear in the prompt, or the test above is checking
    /// a constant nobody sends.
    [Fact]
    public void TheRubricShowsTheExampleItIsTestedAgainst()
    {
        Assert.Contains(GroundednessPrompt.ExampleOutput, GroundednessPrompt.System("---d---"));
    }

    /// The field name is the part that drifted. Named explicitly so a rename
    /// in the rubric fails here rather than silently returning nothing.
    [Fact]
    public void TheRubricAsksForTheFieldNameTheParserReads()
    {
        var rubric = GroundednessPrompt.System("---d---");

        Assert.Contains("unsupported_claims", rubric);
        Assert.Contains("\"grounded\"", rubric);
    }
}
