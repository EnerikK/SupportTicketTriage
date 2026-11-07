# Judge selection under rubric `judge-v2`

Committed before any `judge-v2` configuration runs, so git timestamps the rule ahead of
the numbers it will be applied to. Supersedes `DECISION-RULE.md`, which governed the
`judge-v1` comparison and **stands as recorded** — its result is not retro-labelled.

## Why v1's rule was not good enough

v1 selected on overall agreement with the hand labels. That averages two error types with
opposite consequences:

| | consequence |
|---|---|
| **missed-ungrounded** — labels ungrounded, judge grounded | the published groundedness score is **flattered**; unsupported claims pass |
| **false-flag** — labels grounded, judge ungrounded | the score is depressed, and the metric becomes noise nobody acts on |

v1's rule picked `gpt-5-mini`, which had the best agreement **and the worst miss rate**
(7 of 22). The configuration with the best miss rate, `gpt-5` at default effort, wrongly
flagged 20 of 24 good drafts. Neither is usable, and a single agreement number said one
of them was the winner.

| v1 config | missed-ungrounded | false-flag |
|---|---|---|
| `gpt-5` default | 1/22 = 4.5% | 20/24 = 83.3% |
| `gpt-5` low | 2/22 = 9.1% | 13/24 = 54.2% |
| `gpt-5-mini` default | 7/22 = 31.8% | 7/24 = 29.2% |

## The rule

**Eligibility.** A configuration is eligible only if its **false-flag rate is at most
25%**, measured as a point estimate on the held-out labelled pairs.

**Among eligible configurations, in order:**

1. lowest **missed-ungrounded** rate
2. then lowest **false-flag** rate
3. then **cheapest**

**If no configuration is eligible, none is adopted and no groundedness score is
published.** That is a real possible outcome and is written down before the run so it
cannot later be renegotiated.

### Why a ceiling rather than ranking on misses alone

Ranking on misses alone selects a judge that answers "ungrounded" to everything: zero
misses, and no information. The ceiling is what makes "fewest misses" safe to optimise.

**25% is a judgement call**, and the reasoning is this: v1's best false-flag rate was
29.2% under a rubric where 12 of 20 false flags were defects in the rubric or the prompt
rather than the judge. With those fixed, 25% should be comfortably reachable, while still
disqualifying anything that buys a low miss rate by flagging indiscriminately. It is a
point estimate, not a confidence bound — with roughly 40 ungrounded-labelled pairs the
interval is wide, and treating the bound as the test would let almost anything through.

### Why misses outrank false flags

A judge that misses unsupported claims flatters the metric, and a flattering groundedness
score is worse than no score: it is quotable, it looks like evidence, and it certifies
exactly the failure the measurement exists to detect. A judge that over-flags produces a
number nobody trusts, which is recoverable.

## Reported, not decisive

Overall agreement, its Wilson interval, and Cohen's kappa are reported per configuration.
They describe the judge and are not used to select, because averaging the two error
directions is what produced v1's result.

McNemar's exact test is reported between configurations **restricted to
ungrounded-labelled pairs**, which is the comparison the primary criterion rests on.

## The held-out set

Agreement is measured **only on pairs labelled after `judge-v2` was fixed**, from a fresh
generation run, target 80-100 pairs.

The 46 pairs labelled under `judge-v1` are **rubric-development data** from now on. They
produced the rules; they cannot also test them. Their labels are frozen as committed and
are not corrected, even where later reading shows some were wrong — a correction made
after seeing the verdicts is anchored by having seen them.

## Dev-check protocol

Before the held-out run, `judge-v2` is run against the 46 development pairs with an
expected verdict derived case by case from the rules above. This is a behavioural check,
never a reported number.

Every mismatch is classified as one of two things and nothing else:

- **rubric ambiguity** — the wording genuinely admits the judge's reading. Fix the
  wording, log the change, bump the version if the meaning moved.
- **judge error** — the wording is clear and the judge got it wrong. Record it. **No
  rubric change.**

**The wording is not iterated toward a perfect dev score.** A rubric tuned until the judge
agrees on all 46 has been fitted to one judge on one sample, and the held-out run would
then measure nothing. The held-out run is the test.

## Addendum (dated): reported breakdowns, not selection criteria

Added after the dev-check runs and before any held-out generation. Nothing in this addendum changes the selection rule above.

### Groundedness by retrieval similarity

Band is each query's top-1 cosine against the 240-ticket corpus, taken from the similarity recorded at draft time, not recomputed (embeddings are not bit-identical run to run). Bands: <0.70, 0.70–0.80, >0.80. Report agreement, missed-ungrounded and false-flag rates per band. Per-band counts are small; report them as descriptive, with intervals.

### Hard negatives that received a draft

12 hard negatives score below 0.60 and are refused by Gate B. 6 were written to clear the floor. Report how many of the 6 were drafted. Their drafts are labelled for groundedness under the same rules as every other draft. Separately, each is labelled for appropriate abstention: did the draft avoid presenting an answer the evidence does not provide? These 6 are not pooled with the ordinary queries in any reported figure.
## Addendum, 2026-11-07: extending the candidate set

Added **before any held-out pair carries a groundedness label** and before any configuration
has produced a verdict over the held-out set, so git timestamps this ahead of every number it
governs. Nothing here changes the selection criteria above. It governs only **which
configurations those criteria are applied to.**

### Why this is needed now

Every listed candidate has a retirement date, and they are closer than the comparison is:

| configuration | retires | runway from 2026-11-07 |
|---|---|---|
| `gpt-5` default | 2027-02-09 | ~3 months |
| `gpt-5` low | 2027-02-09 | ~3 months |
| `gpt-5-mini` default | 2027-07-13 | ~8 months |

Two of the three disappear first. **A foreseeable and unwelcome outcome follows**, and it is
named here rather than discovered: under `judge-v1` the only non-`gpt-5` candidate scored a
29.2% false-flag rate, which is **above this rule's 25% ceiling**. If the two `gpt-5`
configurations retire before the comparison runs and `gpt-5-mini` is again ineligible, the
rule adopts nothing and no groundedness score is ever published — closing out a Tier 1
requirement by attrition rather than by measurement.

### What may change, and what may not

**The criteria are frozen.** The 25% false-flag ceiling, the ordering (misses, then false
flags, then cost), and the no-eligible-configuration outcome are not revisable by this
addendum or by any later one. A configuration already judged and found ineligible is **never
re-run under altered criteria**.

**The candidate set may be extended, under two triggers and no others:**

1. a listed configuration becomes undeployable — retired, withdrawn, or unavailable in the
   region and deployment type this project uses; or
2. no listed configuration is eligible under the frozen criteria.

### The conditions an addition must satisfy

- It is **committed to this file before that configuration judges a single held-out pair.**
  The git history, not this sentence, is what evidences the ordering.
- The entry records the **date, the trigger, and the configuration's full provenance** —
  model id, model version, reasoning effort, API version. "`gpt-6-something` at default
  effort" is not a candidate specification; defaults move.
- Where an addition is made **after other candidates' verdicts are visible**, that fact is
  stated in the entry and repeated in the README. It is permitted — a retirement does not
  wait for a convenient moment — but a reader must not have to reconstruct the ordering from
  commit dates to discover it.

### What an addition does not do to results already obtained

A selection outcome attaches to the candidate set **as it stood when the selection was made**,
exactly as `DECISION-RULE.md`'s v1 result stands as recorded. Extending the set later does not
retro-label an earlier outcome, and a figure published under the earlier set keeps the judge
it was computed with named beside it.

### The honest weakness

A rule that permits its own candidate set to grow is weaker than one that cannot, and the
mechanism could in principle be used to keep adding configurations until one passes. Three
things constrain it rather than one: the triggers are enumerated, the criteria cannot move,
and every addition is timestamped against the verdicts it precedes or follows. That is
weaker than a closed set and stronger than an undocumented extension made under deadline
pressure, which is the realistic alternative given the dates in the table above.
