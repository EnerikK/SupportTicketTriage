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
                SHA256.HashData(
                    Encoding.UTF8.GetBytes(
                        GroundednessPrompt.System("{delimiter}").ReplaceLineEndings("\n"))))
            .ToLowerInvariant();

        Assert.Equal(expected, GroundednessPrompt.Sha256);
    }

    /// The hash identifies the wording, not the checkout.
    ///
    /// A raw string literal carries the newlines of the source file it is
    /// written in, so with no `.gitattributes` and `core.autocrlf` on, the
    /// same prompt hashed one way on Windows and another in CI - which was
    /// found on 2026-11-07 when a test pinning a recorded hash passed locally
    /// and failed on Linux with `c9d138e9…` against `83fccb7f…`. Two runs of
    /// identical wording looked like different experiments, which is the one
    /// thing the hash exists to prevent.
    ///
    /// This asserts the property directly rather than pinning a literal: the
    /// same text with CRLF endings must hash to the same value as with LF.
    [Fact]
    public void TheHashIsUnaffectedByLineEndings()
    {
        var lf = GroundednessPrompt.System("{delimiter}").ReplaceLineEndings("\n");
        var crlf = lf.ReplaceLineEndings("\r\n");

        Assert.NotEqual(lf, crlf);

        static string Hash(string text) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ReplaceLineEndings("\n"))))
                .ToLowerInvariant();

        Assert.Equal(Hash(lf), Hash(crlf));
        Assert.Equal(GroundednessPrompt.Sha256, Hash(crlf));
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
