# Judge selection rule, version 3

**Committed 2026-11-14.** Supersedes `DECISION-RULE-V2.md` for every judge selection made
after this commit. It does not apply to, re-run, or re-label any selection made before it. The
rubric is unchanged: `judge-v2`. See [[ADR-040]].

## Why a v3, and why v2 is not edited

`DECISION-RULE-V2.md:135-138` says its criteria "are not revisable by this addendum or by any
later one". **This file revises them**, so it supersedes rather than amends. An amendment to a
rule that forbids its own amendment cannot be defended; a new version that states what it
replaces can.

The precedent is the project's own. v2 superseded v1 when v1's rule proved inadequate, and v1's
result stands as recorded. [[ADR-037]]'s reversal clause names the same route: a v3 "would
supersede this the way v2 superseded v1 — with its own file committed before any v3
configuration runs". That clause speaks of a v3 *rubric*. This is a v3 *rule* with the rubric
unchanged, which is a narrower change of the same kind.

**v2 is left exactly as committed.** Its 2026-11-13 selection (`gpt-5` at Low), `RESULT.md`
and the published 60.9% in `GROUNDEDNESS.md` stand, attached to v2 and to the candidate set as
it was then.

## The rule

A configuration is a **model, model version and reasoning effort**. The same model at two
efforts is two configurations, and each is assessed on its own.

**Eligibility.** A configuration is eligible only if, measured as point estimates on the v3
held-out set (below), both hold:

- **false-flag rate at most 25%** (unchanged from v2), and
- **ungrounded recall at least 80%**: the share of ungrounded-labelled pairs the judge calls
  ungrounded, i.e. `1 − missed-ungrounded rate`. **New in v3.**

**Among eligible configurations, in order** (unchanged from v2):

1. lowest missed-ungrounded rate
2. then lowest false-flag rate
3. then cheapest

**If no configuration is eligible, none is adopted and no groundedness score is published.**
Unchanged from v2.

### Why the recall floor, and why 80%

v2's false-flag ceiling stops a judge that flags everything. Nothing in v2 stopped the
opposite: a judge that passes nearly everything was eligible as long as it did not over-flag,
and would have been selected whenever it was the only eligible configuration.

**80% comes from what the score is for.** Every draft goes to a human reviewer before anything
is done with it, so human review is the backstop and the judge's job is to measure how often
that backstop is needed. A judge that misses more than one ungrounded draft in five is no longer
measuring that: review has become the real filter, and the groundedness figure flatters the
system by the amount the judge misses.

**The anchoring, stated.** This floor was chosen after seeing the v2 held-out results:
ungrounded recall **84%** for `gpt-5` at Low and **56%** for `gpt-5-mini` at Default, on
**n = 25** ungrounded pairs. The argument above does not depend on those numbers and would
give 80% without them. But a reader cannot verify that, so it is recorded rather than
asserted away. Those results are **not evidence for or against any candidate** under this
rule; see the next section.

## The v3 held-out set

Eligibility under v3 is measured **only** on a new held-out set:

- **at least 100 ungrounded-labelled pairs**, plus the grounded pairs that come with them;
- **disjoint** from every pair used so far: the 46 development pairs and the 93 v2 held-out
  pairs, the 25 ungrounded among them included;
- labelled under `judge-v2` by the same procedure as the v2 held-out set, **before** any v3
  candidate judges a pair in it.

**Why a new set.** With 25 ungrounded pairs, recall moves 4 points per pair, so a 95%
interval spans roughly 30 points and an 80% floor could not separate anything. And the 25
have already been read alongside verdicts, which is what anchoring means. At about 100, the
interval narrows to roughly ±8 points. That is still wide, which is why this remains a point
estimate, as in v2.

**What the v2 results become.** Reported history. They do not qualify, disqualify or rank any
configuration under v3, including the configurations they were measured on.

## The candidate set

The v2 conditions on additions carry over unchanged (`DECISION-RULE-V2.md:148-156`):

- an addition is committed to this file before it judges a single held-out pair;
- it records the date, the trigger and full provenance (model id, model version, reasoning
  effort, API version);
- where other candidates' verdicts are already visible, that is stated in the entry and in
  the README.

**Triggers.** The candidate set may be extended when:

1. a listed configuration becomes undeployable (retired, withdrawn, or unavailable in the
   region and deployment type this project uses); or
2. no listed configuration is eligible; or
3. **a listed configuration has a scheduled retirement date.** New in v3. The trigger fires
   once the date is published, not when it arrives. Under v2's first trigger alone, the
   outgoing judge was gone before a successor could be added, and a same-judge comparison
   across a migration needs both to exist at once.

**Trigger 3 has already fired** for `gpt-5` (retires 2027-02-09) and `gpt-5-mini` (retires
2027-07-13). Every addition under v3 is therefore made with v2's verdicts visible, and says so.

## Candidates

None yet. Each is added below by its own commit, before it judges any v3 held-out pair.
