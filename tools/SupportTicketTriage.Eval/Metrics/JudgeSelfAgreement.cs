using SupportTicketTriage.Eval.Judging;

namespace SupportTicketTriage.Eval.Metrics;

/// One draft's verdicts across repeated runs of the same judge.
///
/// <see cref="Verdicts"/> keeps the raw sequence, unjudged included as null,
/// so a reader can see *how* an item disagreed and not only that it did.
public sealed record SelfAgreementItem(
    string QueryId,
    IReadOnlyList<bool?> Verdicts,
    /// The share of runs that gave the most common verdict. 1.0 is unanimous;
    /// with two outcomes and N runs the floor is 0.5.
    double ModalShare,
    bool Unanimous);

public sealed record SelfAgreementResult(
    int Runs,
    IReadOnlyList<SelfAgreementItem> Items)
{
    /// The share of drafts on which every run gave the same verdict. The
    /// headline number: a draft whose verdict flips between identical calls
    /// cannot be scored by this judge at all.
    public double UnanimousRate => Items.Count == 0 ? 0 : (double)Items.Count(i => i.Unanimous) / Items.Count;

    /// Mean modal share. Less strict than unanimity, and reported beside it
    /// because unanimity falls with N even for a fixed per-call flip rate.
    public double MeanModalShare => Items.Count == 0 ? 0 : Items.Average(i => i.ModalShare);
}

/// How consistently one judge configuration agrees with itself: the same
/// judge, the same fixed drafts, N independent runs.
///
/// This is a different question from agreement with hand labels, and a prior
/// one. A judge that disagrees with itself on a draft has no stable verdict to
/// compare against a label, so its label agreement on that draft is partly
/// chance. Reported, never a selection criterion: neither decision rule names
/// it, and adding a criterion here would be a rule change by the back door.
public static class JudgeSelfAgreement
{
    /// `runs[r][i]` is run r's verdict on the i-th draft. Every run must have
    /// judged the same drafts in the same order, which the caller guarantees
    /// by judging one fixed list; it is checked rather than assumed, because a
    /// silent misalignment would report agreement between different drafts.
    ///
    /// Unjudged (null) is treated as its own outcome, not dropped: a judge
    /// that answers on one run and fails to on another has not agreed with
    /// itself.
    public static SelfAgreementResult Compute(IReadOnlyList<IReadOnlyList<GroundednessCase>> runs)
    {
        if (runs.Count < 2)
        {
            throw new ArgumentException("Self-agreement needs at least two runs.", nameof(runs));
        }

        var ids = runs[0].Select(c => c.QueryId).ToList();

        if (runs.Any(run => !run.Select(c => c.QueryId).SequenceEqual(ids)))
        {
            throw new ArgumentException("Every run must judge the same drafts in the same order.", nameof(runs));
        }

        var items = ids
            .Select((id, i) =>
            {
                var verdicts = runs.Select(run => run[i].Grounded).ToList();
                var modal = verdicts.GroupBy(v => v).Max(g => g.Count());

                return new SelfAgreementItem(
                    id,
                    verdicts,
                    (double)modal / verdicts.Count,
                    Unanimous: modal == verdicts.Count);
            })
            .ToList();

        return new SelfAgreementResult(runs.Count, items);
    }
}
