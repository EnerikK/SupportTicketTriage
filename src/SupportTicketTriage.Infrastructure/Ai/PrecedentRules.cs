namespace SupportTicketTriage.Infrastructure.Ai;

/// The rules a draft must obey and the judge must check, in one place.
///
/// **Shared verbatim between the generation prompt and the groundedness
/// rubric**, because a writer held to one standard and scored against another
/// produces disagreements that look like model error and are really a
/// specification mismatch. The first comparison run spent most of its
/// disagreement budget on exactly that.
///
/// Both of these come from reading 27 real disagreements between a judge and
/// a human labeller; every clause below exists because a specific draft
/// exploited its absence.
public static class PrecedentRules
{
    /// Which text establishes a fact, and which is merely something somebody
    /// said.
    ///
    /// A precedent is a customer ticket plus an agent's answer. Treating the
    /// customer half as evidence lets a draft inherit another customer's
    /// circumstances; refusing to let a draft restate *this* customer's own
    /// account makes ordinary replies unanswerable. The split is by who wrote
    /// it, not by which block it sits in.
    public const string EvidentiaryStatus =
        """
        EVIDENCE. Only RESOLUTION blocks establish facts. Everything marked
        ACCOUNT - the incoming ticket, and each precedent's subject and body -
        is what a customer said, and never establishes a fact.

        The customer is authoritative about their own experience and intent:
        what they did, what they saw, what they want. That may be restated
        directly. Claims about causes, system state, eligibility, or what
        anyone else did need either attribution ("you mentioned...") or support
        from a RESOLUTION.

        Where both apply, the second wins. An account may be restated directly,
        but once a draft uses it as the basis for an eligibility, a cause or an
        outcome, attribution is required. "The item is perfectly good" restates
        the customer. "Since the item is perfectly good, you can return it"
        rests an eligibility on it, and needs "you mentioned" or a resolution.
        """;

    /// What a precedent licenses, and what it does not.
    ///
    /// The generality test is the part that took three passes to get right: a
    /// resolution written in the past tense about one case ("the discount was
    /// applied retrospectively") cannot become a policy statement ("in such
    /// cases the discount is applied"), while one written in general terms
    /// ("a replacement is sent", "enrolments are tied to the device") can. A
    /// conditional asserts its consequent as a general rule, so hedging has to
    /// attach to the claim itself rather than to the circumstances that
    /// trigger it.
    public const string Precedent =
        """
        PRECEDENTS. A resolution is evidence about ONE case. It may be used:
          - as a similar case ("in a similar case...")
          - as a hedged possibility ("this may be because...")
          - as general policy ONLY where the resolution is itself phrased in
            general terms. "A replacement is sent" and "enrolments are tied to
            the device" license a policy statement. "The discount was applied"
            and "the browser was configured" describe one past case and do not.

        A precedent may NEVER be used to:
          - assert its circumstances or actions as this customer's facts
          - become a general practice, a prevalence claim ("commonly", "a few
            common reasons"), a guarantee ("you will be notified"), or a
            prediction about this customer's outcome or timing

        A conditional asserts its consequent as a general rule: "if X, then Y
        is refunded" claims Y generally. Hedging must apply to the claim, not
        only to the circumstances that trigger it.
        """;

    /// What counts as a claim worth failing a draft over.
    ///
    /// Without this, a judge fails drafts over descriptive wording - "slightly
    /// higher", "by mistake", "caused confusion" - which is noise that buries
    /// the failures the metric exists to catch. "Cause of the problem the
    /// customer reported" rather than "cause", because the looser reading
    /// catches any causal phrasing anywhere in a sentence.
    public const string Materiality =
        """
        MATERIALITY. Only MATERIAL claims can make a draft ungrounded. A claim
        is material if it asserts:
          - a cause of the problem the customer reported
          - a policy or rule
          - an amount, or a timeline
          - an eligibility
          - an action taken, or a specific outcome promised

        An offer to investigate or assist is not material. A promise of a
        specific outcome - a refund, a replacement, a fix, or when one will
        happen - is material.

        Descriptive wording that asserts none of these cannot make a draft
        ungrounded. Ignore greetings, apologies and offers to help further.
        """;
}
