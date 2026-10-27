using SupportTicketTriage.Eval.Judging;

namespace SupportTicketTriage.Eval.Metrics;

/// The groundedness score for a run.
///
/// <see cref="Unjudged"/> is excluded from the denominator rather than
/// counted as ungrounded: a judge outage is not evidence about a draft, and
/// blending the two would make the score fall whenever the judge deployment
/// wobbled.
public sealed record GroundednessScore(
    double GroundedRate,
    int Judged,
    int Grounded,
    int Ungrounded,
    int Unjudged);

/// How well the judge agrees with hand labels.
///
/// Both numbers are reported, and the second is the one that matters.
/// Raw agreement is inflated whenever the classes are skewed: if 85% of
/// drafts are grounded, a judge that answers "grounded" every time scores 85%
/// while carrying no information at all. Cohen's kappa measures agreement
/// above what that constant judge would get by chance, so it collapses toward
/// zero exactly where raw agreement flatters.
public sealed record JudgeAgreement(
    double RawAgreement,
    double Kappa,
    int Compared,
    int BothGrounded,
    int BothUngrounded,
    int JudgeGroundedHumanNot,
    int HumanGroundedJudgeNot);

public static class GroundednessMetrics
{
    public static GroundednessScore Summarise(IReadOnlyCollection<GroundednessCase> cases)
    {
        var grounded = cases.Count(c => c.Grounded == true);
        var ungrounded = cases.Count(c => c.Grounded == false);
        var judged = grounded + ungrounded;

        return new GroundednessScore(
            GroundedRate: judged == 0 ? 0 : (double)grounded / judged,
            Judged: judged,
            Grounded: grounded,
            Ungrounded: ungrounded,
            Unjudged: cases.Count(c => c.Grounded is null));
    }

    /// Compares the judge against hand labels on the pairs that have both.
    ///
    /// A pair the judge could not read is dropped rather than scored as a
    /// disagreement: it says nothing about whether the judge and the human
    /// would have agreed, and counting it would make judge reliability and
    /// judge availability the same number.
    public static JudgeAgreement Compare(
        IReadOnlyCollection<GroundednessCase> judged,
        IReadOnlyDictionary<string, bool> humanLabels)
    {
        var byId = judged
            .Where(c => c.Grounded is not null)
            .ToDictionary(c => c.QueryId, c => c.Grounded!.Value);

        var pairs = humanLabels
            .Where(l => byId.ContainsKey(l.Key))
            .Select(l => (Human: l.Value, Judge: byId[l.Key]))
            .ToList();

        if (pairs.Count == 0)
        {
            return new JudgeAgreement(0, 0, 0, 0, 0, 0, 0);
        }

        var bothGrounded = pairs.Count(p => p is { Human: true, Judge: true });
        var bothUngrounded = pairs.Count(p => p is { Human: false, Judge: false });
        var judgeOnly = pairs.Count(p => p is { Human: false, Judge: true });
        var humanOnly = pairs.Count(p => p is { Human: true, Judge: false });

        var n = (double)pairs.Count;
        var observed = (bothGrounded + bothUngrounded) / n;

        // Chance agreement: the rate the two would match if each kept its own
        // marginal frequency but answered independently.
        var expected =
            ((bothGrounded + judgeOnly) / n * ((bothGrounded + humanOnly) / n))
            + ((bothUngrounded + humanOnly) / n * ((bothUngrounded + judgeOnly) / n));

        // Perfect chance agreement means both labelled everything the same
        // way, and kappa is 0/0. Reported as 1 when they also agreed and 0
        // when they did not, rather than NaN reaching a report.
        var kappa = Math.Abs(1 - expected) < 1e-12
            ? (Math.Abs(observed - 1) < 1e-12 ? 1 : 0)
            : (observed - expected) / (1 - expected);

        return new JudgeAgreement(
            RawAgreement: observed,
            Kappa: kappa,
            Compared: pairs.Count,
            BothGrounded: bothGrounded,
            BothUngrounded: bothUngrounded,
            JudgeGroundedHumanNot: judgeOnly,
            HumanGroundedJudgeNot: humanOnly);
    }
}
