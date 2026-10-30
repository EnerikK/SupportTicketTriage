using System.Security.Cryptography;
using System.Text;
using SupportTicketTriage.Eval.Judging;
using SupportTicketTriage.Infrastructure.Ai;

namespace SupportTicketTriage.UnitTests;

/// A prompt version is a label; the hash is the identity.
///
/// judge-v2 was amended twice in one afternoon without a version bump - both
/// times correctly, since no published figure or label set depended on it.
/// The cost was that two verdict files an hour apart both said "judge-v2" and
/// nothing could tell which text produced which, so a dev-check result could
/// not be compared against its own predecessor. The hash closes that: the
/// label stays a human decision, the hash is computed from the text at run
/// time and recorded in provenance.
public class PromptProvenanceTests
{
    /// The one non-obvious property. Both prompts take a per-request
    /// delimiter that is random by design (ADR-002), so hashing the rendered
    /// text naively would produce a fresh hash on every run and identify
    /// nothing. The hash is taken over a canonical rendering instead.
    [Fact]
    public void TheHashIsStableAcrossCalls_DespiteTheRandomDelimiter()
    {
        Assert.NotEqual(GroundednessPrompt.NewDelimiter(), GroundednessPrompt.NewDelimiter());

        Assert.Equal(GroundednessPrompt.Sha256, GroundednessPrompt.Sha256);
        Assert.Equal(GenerationPrompt.Sha256, GenerationPrompt.Sha256);
    }

    /// Proves the hash covers the prompt text itself rather than a version
    /// string or some other stand-in - the failure that would make the whole
    /// mechanism decorative while still looking precise in a verdict file.
    [Fact]
    public void TheHashIsTakenOverThePromptText()
    {
        var expected = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(GroundednessPrompt.System("{delimiter}"))))
            .ToLowerInvariant();

        Assert.Equal(expected, GroundednessPrompt.Sha256);
    }

    /// The rules are shared verbatim between writer and judge, so a careless
    /// implementation could hash the shared fragment and report the same
    /// identity for two different prompts.
    [Fact]
    public void TheTwoPromptsHaveDifferentIdentities()
    {
        Assert.NotEqual(GroundednessPrompt.Sha256, GenerationPrompt.Sha256);
    }

    /// Changing any rule text must move the judge's hash. PrecedentRules is
    /// interpolated into both prompts; this pins that the hash reflects the
    /// interpolated result and not the template around it.
    [Fact]
    public void TheHashCoversTheSharedRuleText()
    {
        Assert.Contains(PrecedentRules.Materiality, GroundednessPrompt.System("{delimiter}"));
        Assert.Contains(PrecedentRules.EvidentiaryStatus, GenerationPrompt.System("{delimiter}"));
    }
}
