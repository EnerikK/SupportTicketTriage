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
    /// Wilson score interval on the agreement rate. Describes *this*
    /// configuration's uncertainty, and is deliberately not the instrument for
    /// comparing two configurations - see McNemar below.
    double AgreementLower,
    double AgreementUpper,
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
            return new JudgeAgreement(0, 0, 0, 0, 0, 0, 0, 0, 0);
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

        var (lower, upper) = Wilson(bothGrounded + bothUngrounded, pairs.Count);

        return new JudgeAgreement(
            RawAgreement: observed,
            AgreementLower: lower,
            AgreementUpper: upper,
            Kappa: kappa,
            Compared: pairs.Count,
            BothGrounded: bothGrounded,
            BothUngrounded: bothUngrounded,
            JudgeGroundedHumanNot: judgeOnly,
            HumanGroundedJudgeNot: humanOnly);
    }

    /// Wilson score interval at 95%.
    ///
    /// Not the normal approximation, which misbehaves exactly where this
    /// metric is expected to live: near the top of the range, on a sample of
    /// about forty. At 45/47 the normal interval runs past 1.0, and an
    /// agreement figure that claims an upper bound above certainty is worse
    /// than no interval.
    private static (double Lower, double Upper) Wilson(int successes, int total, double z = 1.96)
    {
        if (total == 0)
        {
            return (0, 0);
        }

        var n = (double)total;
        var p = successes / n;
        var denominator = 1 + (z * z / n);
        var centre = p + (z * z / (2 * n));
        var spread = z * Math.Sqrt((p * (1 - p) / n) + (z * z / (4 * n * n)));

        return (Math.Max(0, (centre - spread) / denominator), Math.Min(1, (centre + spread) / denominator));
    }
}

/// Whether two judge configurations disagree with the hand labels differently.
///
/// Both configurations judge the same items, so the comparison is paired and
/// only the **discordant** pairs carry information: items where one was right
/// and the other wrong. Items both got right, or both got wrong, say nothing
/// about which is better and are excluded by construction.
///
/// Comparing two overlapping Wilson intervals instead would discard the
/// pairing and lose most of the power - at n=47 two configurations differing
/// by a handful of verdicts will not separate that way even when this test
/// says they do.
public sealed record McNemarResult(
    int OnlyFirstCorrect,
    int OnlySecondCorrect,
    double PValue,
    bool Significant)
{
    public int Discordant => OnlyFirstCorrect + OnlySecondCorrect;
}

public static class McNemar
{
    /// Exact binomial two-sided test on the discordant pairs.
    ///
    /// Exact rather than the chi-squared approximation, because the
    /// approximation needs roughly 25 discordant pairs to be trustworthy and
    /// this comparison is expected to produce single digits. A chi-squared
    /// p-value computed on four discordant pairs is a number, not evidence.
    public static McNemarResult Compare(
        IReadOnlyDictionary<string, bool> labels,
        IReadOnlyDictionary<string, bool> first,
        IReadOnlyDictionary<string, bool> second,
        double alpha = 0.05)
    {
        var onlyFirst = 0;
        var onlySecond = 0;

        foreach (var (id, label) in labels)
        {
            if (!first.TryGetValue(id, out var a) || !second.TryGetValue(id, out var b))
            {
                continue;
            }

            var firstCorrect = a == label;
            var secondCorrect = b == label;

            if (firstCorrect && !secondCorrect)
            {
                onlyFirst++;
            }
            else if (secondCorrect && !firstCorrect)
            {
                onlySecond++;
            }
        }

        var n = onlyFirst + onlySecond;

        // No discordant pairs means the two configurations made identical
        // mistakes. p = 1: there is no evidence of a difference, which is not
        // the same as evidence they are the same.
        if (n == 0)
        {
            return new McNemarResult(0, 0, 1.0, false);
        }

        var smaller = Math.Min(onlyFirst, onlySecond);
        var tail = 0.0;

        for (var k = 0; k <= smaller; k++)
        {
            tail += Choose(n, k);
        }

        var p = Math.Min(1.0, 2 * tail / Math.Pow(2, n));

        return new McNemarResult(onlyFirst, onlySecond, p, p < alpha);
    }

    /// Binomial coefficient by multiplicative recurrence, in double.
    ///
    /// n is the discordant count - single digits here, and bounded by 47 even
    /// in the worst case - so the factorial overflow that usually argues for
    /// log-space arithmetic cannot arise.
    private static double Choose(int n, int k)
    {
        var result = 1.0;

        for (var i = 1; i <= k; i++)
        {
            result = result * (n - k + i) / i;
        }

        return result;
    }
}
