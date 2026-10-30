using System.Text;
using SupportTicketTriage.Eval.Judging;
using SupportTicketTriage.Infrastructure.Ai;

namespace SupportTicketTriage.UnitTests;

/// The writer and the judge must see the same evidence.
///
/// judge-v1 showed the judge only each precedent's resolution while the
/// writer also saw its subject and body, so the judge scored drafts against
/// strictly less evidence than they were produced from. Any claim resting on
/// a precedent's own words was unsupported by construction, and three of
/// twenty disagreements in the first comparison run were that gap rather than
/// a judgement about groundedness.
///
/// Nothing caught it. Both prompts compiled, both ran, and the asymmetry was
/// only visible by reading two files side by side.
public class EvidenceParityTests
{
    private const string Ticket = "Charged twice\n\nMy statement shows two identical charges.";

    private static readonly List<EvidenceSource> Sources =
    [
        new(Guid.CreateVersion7(),
            "Distinctive source subject",
            "Distinctive source body.",
            "Distinctive source resolution."),
    ];

    private static string Generator() =>
        GenerationPrompt.User("---d---", Ticket, Sources);

    private static string Judge() =>
        GroundednessPrompt.User("---d---", "A draft.", Ticket, Sources);

    /// The guarantee, stated as the thing that failed: whatever the writer was
    /// shown, the judge is shown too.
    [Theory]
    [InlineData("Distinctive source subject")]
    [InlineData("Distinctive source body.")]
    [InlineData("Distinctive source resolution.")]
    [InlineData("My statement shows two identical charges.")]
    public void TheJudgeSeesEverythingTheGeneratorSees(string fragment)
    {
        Assert.Contains(fragment, Generator());
        Assert.Contains(fragment, Judge());
    }

    /// The general form, so a field added to one prompt and not the other
    /// fails here rather than in a comparison run six weeks later.
    [Fact]
    public void TheJudgesEvidenceIsNeverASubsetOfTheGenerators()
    {
        var generator = Generator();
        var judge = Judge();

        var missing = EvidenceLines(generator).Except(EvidenceLines(judge)).ToList();

        Assert.True(
            missing.Count == 0,
            "The generator renders evidence the judge never sees: " + string.Join(" | ", missing));
    }

    /// Both render through the same function, so the precedent blocks are
    /// byte-identical rather than merely equivalent.
    [Fact]
    public void BothPromptsRenderPrecedentsIdentically()
    {
        var builder = new StringBuilder();
        EvidenceBlocks.AppendPrecedents(builder, "---d---", Sources);
        var expected = builder.ToString();

        Assert.Contains(expected, Generator());
        Assert.Contains(expected, Judge());
    }

    /// The ticket reaches the judge labelled as an account, not as evidence.
    /// Showing it was the v2 reversal; labelling it is what keeps the reversal
    /// from rewarding relevance.
    [Fact]
    public void TheTicketIsLabelledAsAnAccountInBothPrompts()
    {
        Assert.Contains(EvidenceBlocks.AccountLabel, Generator());
        Assert.Contains(EvidenceBlocks.AccountLabel, Judge());
    }

    private static IEnumerable<string> EvidenceLines(string prompt) =>
        prompt
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith("---block", StringComparison.Ordinal));
}
