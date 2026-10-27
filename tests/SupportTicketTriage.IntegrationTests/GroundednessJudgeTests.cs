using Microsoft.Extensions.AI;
using SupportTicketTriage.Eval.Judging;

namespace SupportTicketTriage.IntegrationTests;

/// The judge against a faked model boundary.
///
/// No database and no collection fixture: the judge talks to an IChatClient
/// and nothing else, which is what keeps it testable without a second real
/// deployment.
public class GroundednessJudgeTests
{
    private static readonly List<GroundednessPrompt.Source> OneSource =
    [
        new(Guid.CreateVersion7(), "The second charge was an authorisation hold and drops off within five days."),
    ];

    private static GroundednessJudge Judge(FakeChatClient chat) => new(chat, "test-judge-model");

    [Fact]
    public async Task AGroundedVerdict_IsRead()
    {
        var chat = new FakeChatClient
        {
            ResponseText = """{"grounded": true, "unsupported_claim": ""}""",
        };

        var result = await Judge(chat).JudgeAsync("q-1", "A draft.", OneSource);

        Assert.True(result.Grounded);
        Assert.Null(result.UnsupportedClaim);
    }

    [Fact]
    public async Task AnUngroundedVerdict_KeepsTheQuotedClaim()
    {
        var chat = new FakeChatClient
        {
            ResponseText = """{"grounded": false, "unsupported_claim": "which improved it significantly"}""",
        };

        var result = await Judge(chat).JudgeAsync("q-1", "A draft.", OneSource);

        Assert.False(result.Grounded);
        Assert.Equal("which improved it significantly", result.UnsupportedClaim);
    }

    /// Chat models fence JSON often enough that treating it as a failure would
    /// report model formatting as judge unavailability.
    [Fact]
    public async Task AFencedResponse_IsStillRead()
    {
        var chat = new FakeChatClient
        {
            ResponseText = "```json\n{\"grounded\": true, \"unsupported_claim\": \"\"}\n```",
        };

        Assert.True((await Judge(chat).JudgeAsync("q-1", "A draft.", OneSource)).Grounded);
    }

    /// Unjudged, not ungrounded. The distinction is the whole reason Grounded
    /// is nullable: an unreadable response and a rejected draft mean opposite
    /// things to the score.
    [Fact]
    public async Task AnUnreadableResponse_IsUnjudgedRatherThanUngrounded()
    {
        var chat = new FakeChatClient { ResponseText = "I think it looks fine to me." };

        var result = await Judge(chat).JudgeAsync("q-1", "A draft.", OneSource);

        Assert.Null(result.Grounded);
        Assert.NotNull(result.UnsupportedClaim);
    }

    [Fact]
    public async Task AJudgeOutage_IsUnjudgedRatherThanUngrounded()
    {
        var chat = new FakeChatClient { ShouldThrow = true };

        Assert.Null((await Judge(chat).JudgeAsync("q-1", "A draft.", OneSource)).Grounded);
    }

    /// Arithmetic, not judgement: a draft citing nothing has no source that
    /// could support any claim it makes. Spending a judge call to be told so
    /// would be paying for a foregone conclusion.
    [Fact]
    public async Task ADraftCitingNothing_IsUngroundedWithoutCallingTheModel()
    {
        var chat = new FakeChatClient();

        var result = await Judge(chat).JudgeAsync("q-1", "A draft.", []);

        Assert.False(result.Grounded);
        Assert.Empty(chat.Received);
    }

    /// ADR-002's structural separation, applied to the judge. Cited
    /// resolutions are historical ticket content that reached the corpus
    /// through a human approval, which does not make them trusted - the
    /// approval loop is itself an injection vector.
    [Fact]
    public async Task InstructionsAndJudgedContent_TravelInSeparateMessages()
    {
        var chat = new FakeChatClient
        {
            ResponseText = """{"grounded": true, "unsupported_claim": ""}""",
        };

        await Judge(chat).JudgeAsync("q-1", "Distinctive draft sentence.", OneSource);

        var messages = Assert.Single(chat.Received);
        var system = Assert.Single(messages, m => m.Role == ChatRole.System);
        var user = Assert.Single(messages, m => m.Role == ChatRole.User);

        Assert.Contains("Distinctive draft sentence.", user.Text);
        Assert.Contains(OneSource[0].RedactedResolution, user.Text);
        Assert.DoesNotContain("Distinctive draft sentence.", system.Text);
    }

    /// Groundedness is a relation between a draft and its sources. Showing the
    /// judge the original ticket would invite it to reward a reply that
    /// answers the question - relevance, not groundedness - which would pull
    /// the score up on exactly the drafts that answer confidently from
    /// nothing.
    [Fact]
    public async Task TheOriginalTicket_IsNeverSentToTheJudge()
    {
        var chat = new FakeChatClient
        {
            ResponseText = """{"grounded": true, "unsupported_claim": ""}""",
        };

        await Judge(chat).JudgeAsync("q-1", "A draft.", OneSource);

        Assert.DoesNotContain("INCOMING TICKET", chat.AllText);
    }
}
