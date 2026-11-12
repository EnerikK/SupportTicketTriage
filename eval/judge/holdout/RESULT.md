# The held-out judge selection: the result

Produced by applying `../DECISION-RULE-V2.md` to the verdicts in this directory's sibling
files. Every criterion was committed before the verdicts existed, and nothing below was
decided after seeing a number.

| | |
|---|---|
| Rule | `eval/judge/DECISION-RULE-V2.md`, committed `20f214b`, 2026-10-30 |
| Candidate-set addendum | `a5d7ffa`, 2026-11-07 |
| [[ADR-037]] amendment | `50325c6`, 2026-11-07 |
| Labels | `eval/judge/holdout-pairs.json`, sha256 `e372c34d548a`, committed `c404bb4`, 2026-11-07 |
| Verdicts | run 2026-11-12 21:36–23:57 UTC, committed `cb99abb` |
| Drafts judged | 93, all from `gpt-4.1-mini` under generation prompt `draft-v2` (`83fccb7f…`) |
| Rubric | `judge-v2` (`9a0b0300…`), identical across all three configurations |

The labels call **68 grounded and 25 ungrounded**. Those two numbers are the denominators
of the two error rates, and they are not interchangeable.

## The rule, applied in its pre-registered order

| configuration | missed-ungrounded | false-flag | eligible |
|---|---|---|---|
| `gpt-5` / Default | 4/25 = 16.0% [6, 35] | **22/68 = 32.4%** [22, 44] | **no** |
| `gpt-5` / Low | **4/25 = 16.0%** [6, 35] | 17/68 = 25.0% [16, 36] | yes |
| `gpt-5-mini` / Default | 11/25 = 44.0% [27, 63] | 17/68 = 25.0% [16, 36] | yes |

**Step 1 — the 25% false-flag ceiling.** `gpt-5` at default effort flags 22 of 68 drafts the
labels call grounded. It is removed before any other criterion is consulted.

**Step 2 — lowest missed-ungrounded among the eligible.** 16.0% against 44.0%. Decided here.

### `gpt-5` at Low reasoning effort is the selected configuration.

Steps 3 and 4 are never reached. Worth recording that they would have pointed the other way:
`gpt-5-mini` cost **USD 0.1729** for this run against **USD 0.6743**, roughly a quarter. The
ordering exists precisely so that a four-fold cost saving cannot buy a judge that misses 11 of
25 unsupported drafts instead of 4.

McNemar's exact test, restricted to the 25 ungrounded-labelled pairs as the rule specifies:
`gpt-5` / Low correct where `gpt-5-mini` was wrong on 8, the reverse on 1, **p = 0.0391**. The
separation on the primary criterion is not an artefact of a small denominator.

## The margin, which is the part a reader should carry away

**Both eligible configurations landed on exactly 17/68 — 25.000%.** The rule says "at most
25%". One further false flag on either would have disqualified it; one on each would have left
nothing eligible, and `DECISION-RULE-V2.md` would have adopted no judge and published no
groundedness score.

That outcome was pre-registered as real, and it came within a single pair of happening.

This matters beyond bookkeeping. [[ADR-037]]'s amendment records that **every candidate here
retires within eight months** — `gpt-5` at both efforts on 2027-02-09, `gpt-5-mini` on
2027-07-13 — so the rule will be re-run on a successor candidate set before February. Anyone
doing that should expect "nothing is eligible" as the *likely* case rather than the edge case.
The v1 rubric's best false-flag rate was 29.2%, above this ceiling; v2's best is 25.0%, exactly
on it. The rubric work bought a real improvement, and on this denominator it is three pairs wide.

**What must not happen then is the ceiling moving.** It is frozen by the addendum and by
[[ADR-037]], it is a `const` in `JudgeComparisonReport` rather than a command-line option, and
a unit test fails if the constant is edited. Three mechanisms, because the pressure to move it
will arrive at exactly the moment the rule says to publish nothing.

## Three honest weaknesses in this result

**The denominator is 25, not the ~40 the rule anticipated.** `DECISION-RULE-V2.md` reasons
about the ceiling "with roughly 40 ungrounded-labelled pairs". The held-out set produced 25,
so the primary criterion moves in 4-point steps. The 16.0%/44.0% gap is seven pairs wide and
survives that comfortably, but a closer result would not have been resolvable.

**The intervals do not respect the ceiling the point estimates cleared.** `gpt-5` / Low's
false-flag Wilson interval is [16.2, 36.4] — it contains 25% and runs well past it. This is
the rule working as written, not a loophole: it pre-registered the point estimate as the test
and said why, that "treating the bound as the test would let almost anything through". But no
reader should take 25.0% as established within ±1 point.

**Agreement with the labels is bounded by the labels' own reliability.** `INTRA-RATER-RESULT.md`
measured the labeller's self-agreement on `grounded` at **95.0%** over a five-day gap. The
selected configuration agrees with those labels **77.4%** of the time. That is comfortably
under the ceiling, which is the expected shape — a judge cannot be shown to track these labels
more consistently than the labeller does. A configuration scoring near or above 95% would be
evidence of something wrong with the comparison, not of an unusually good judge.

## What follows from [[ADR-037]]'s amendment

**`gpt-5-mini` is not the judge, so it stays available as the drafting replacement.** The
amendment made role eligibility exclusive and was written before either answer existed,
specifically so the order in which the two arrived could not determine the outcome. It is
worth noting that the constraint did not bind: the rule removed `gpt-5-mini` on its own terms.

**If `gpt-5-mini` does become the drafting model, the pairing must be disclosed.** A `gpt-5`
judge scoring `gpt-5-mini` drafts satisfies [[ADR-018]]'s letter and shares a training lineage.
Today's pairing is `gpt-5` judging `gpt-4.1-mini`, which is cross-family; that changes the day
the drafting model does, and the disclosure obligation attaches then.

**This winner is provisional and validates the procedure, not the judge.** `gpt-5` retires
2027-02-09. What this run establishes is that the selection procedure executes end to end on
held-out data and yields a defensible answer, plus a reference figure computed while `gpt-5`
still exists — which any successor comparison needs on the other side of the migration. Any
published groundedness figure carries the judge that produced it and that judge's retirement
date.

**[[ADR-018]]'s separation guard remains narrower than it claims.** It compares deployment
names, not resolved model identities. Nothing in this run changes that, and the amendment's
"configuration discipline rather than a mechanism" still carries the guarantee.

## Reported, not decisive

Overall agreement and Cohen's κ, which the rule explicitly does not select on:

| configuration | agreement | 95% CI | κ | output tokens | cost (USD) |
|---|---|---|---|---|---|
| `gpt-5` / Default | 72.0% | 62–80% | 0.42 | 161,834 | 1.7532 |
| `gpt-5` / Low | 77.4% | 68–85% | 0.51 | 53,941 | 0.6743 |
| `gpt-5-mini` / Default | 69.9% | 60–78% | 0.29 | 72,995 | 0.1729 |

Note that **agreement and the two error rates do not rank the candidates the same way**, which
is the whole reason for the v2 rule. On agreement, `gpt-5` at default effort (72.0%) outranks
`gpt-5-mini` (69.9%); on eligibility, default effort is the one removed. A single averaged
number would again have promoted a configuration the error rates disqualify.

### By retrieval similarity

The addendum asks for this breakdown as descriptive. Band is each query's top-1 cosine
recorded at draft time. Counts are small; nothing here is a finding.

| band | n | `gpt-5` Default | `gpt-5` Low | `gpt-5-mini` |
|---|---|---|---|---|
| <0.70 | 34 | 64.7% — miss 2/7, flag 10/27 | 73.5% — miss 1/7, flag 8/27 | 67.6% — miss 1/7, flag 10/27 |
| 0.70–0.80 | 43 | 72.1% — miss 1/11, flag 11/32 | 79.1% — miss 1/11, flag 8/32 | 65.1% — **miss 8/11**, flag 7/32 |
| >0.80 | 16 | 87.5% — miss 1/7, flag 1/9 | 81.2% — miss 2/7, flag 1/9 | 87.5% — miss 2/7, flag 0/9 |

The one row worth a second look is `gpt-5-mini` in the middle band: 8 of its 11 misses sit
there, where retrieval returned something plausible but not sufficient. Both `gpt-5` efforts
miss roughly evenly across bands. At n=11 this is a description of what happened, not a
mechanism, and it is not used in the selection.

## What this file does not contain

**The groundedness score itself.** Selecting a judge and publishing a figure are separate
steps. [[ADR-018]] makes the judge-agreement figure a precondition for publishing one, and the
figure above is it — the score computed from these verdicts is in `GROUNDEDNESS.md`, alongside
the population it is over and the exclusions applied to it.

**The abstention breakdown.** [[ADR-038]] fixes how it is reported — six engineered hard
negatives described individually, no rate — and that write-up is owed separately.

**Any claim about judges in general.** [[ADR-029]] already records that this validates a judge
*on this sample*. One person labelled all 93 pairs; no inter-rater figure exists, and nothing
here produces one.
