# The drafter replacement: the result

Produced by applying [[ADR-043]] to the three candidates' runs. The rule and its temperature
amendment were committed before any candidate drafted, classified or was judged. Three
questions that came up after the numbers existed are listed under
[Calls made after the runs](#calls-made-after-the-runs), each with its effect on the outcome,
which in all three cases is none.

| | |
|---|---|
| Rule | [[ADR-043]], committed `9e103a9`, 2026-11-15 |
| Temperature amendment | `61915cc`, 2026-11-15: `gpt-5.4-mini` at 0, both luna models `omit` |
| Pricing | version 1.2, committed `ec015fa`, rates retrieved 2026-11-15 |
| Runs | 2026-11-15; per candidate one full 300-ticket run and one 111-query held-out run |
| Judge | `gpt-5` at Low, rubric `judge-v2` (`9a0b0300…`), every judged verdict `gpt-5-2025-08-07` |
| Prompts | classification `classify-v1`, generation `draft-v2` (`83fccb7f…`), unchanged |
| Raw outputs | `eval/results/drafter/<candidate>/`, gitignored, as for every other harness run |

## The rule, applied

| criterion (floor) | `gpt-5.4-mini` | `gpt-5.6-luna` | `gpt-6-luna` |
|---|---|---|---|
| Classification macro-F1 (≥ 88.4%: baseline 93.4% − 5 pp) | 92.7% | 93.5% | 92.5% |
| Citation validity (≥ 98.0%: baseline 100% − 2 pp) | 100% (47/47) | **97.9% (46/47)** | 100% (47/47) |
| Groundedness (≥ 50.9%: 60.9% − 10 pp) | 92.0% (80/87) | 80.5% (70/87) | 93.2% (82/88) |
| Call failures other than `content_filter` | 0 (5 `content_filter`) | 0 (5 `content_filter`) | 0 (5 `content_filter`) |
| `TimeoutException` | 0 | 0 | 0 |
| **Eligible** | yes | **no** | yes |

Macro-F1, citation validity and call failures come from the full run; groundedness from the
held-out run. All five `content_filter` rejections per candidate are classification calls,
the same failure the incumbent shows on its injection tickets, one more than its four.

**`gpt-5.6-luna` is ineligible by one citation.** One of its 47 drafts cited a ticket outside
its retrieval set, and 97.9% is 0.1 pp under the floor. The floor is the regression gate's own
tolerance ([[ADR-021]]), set before this ran; a candidate the CI gate would reject cannot be
adopted, and a near miss is exactly the case the pre-registration exists for.

## Selection

| | cost per drafted ticket (USD) | per 1,000 drafted | retires |
|---|---|---|---|
| `gpt-4.1-mini` (incumbent, baseline) | 0.001054 | 1.05 | 2027-04-14 |
| `gpt-5.4-mini` | 0.002321 | 2.32 | 2027-09-21 |
| `gpt-6-luna` | **0.000425** | **0.43** | 2028-03-11 |

### `gpt-6-luna` is the selected drafter.

Lowest cost among the eligible, by a factor of 5.5, so the 10% tie band and the retirement
tiebreak are never consulted. They would have agreed: `gpt-6-luna` also retires last.

The cost figures were computed by hand, not printed by the harness. The runs predate pricing
1.2, so their reports show no cost. Each figure applies the harness's own formula
(`CostMetrics.cs`: ingest cost ÷ 300 tickets + generation cost ÷ 47 drafts) to the token
counts in that candidate's full-run report, at the pricing 1.2 rates. The next live run of
`gpt-6-luna` will print the same figure itself, and that is the number the baseline takes.

## Reported, not criteria

| | `gpt-5.4-mini` | `gpt-5.6-luna` | `gpt-6-luna` |
|---|---|---|---|
| Accuracy (full run) | 92.0% | 92.7% | 91.7% |
| Routing refusals, full run | 13/60 | 13/60 | 13/60 |
| Gate A separation | 0.076 | 0.051 | 0.066 |
| Classification p50 / p95 | 1,388 / 1,686 ms | 935 / 1,310 ms | 1,675 / 2,142 ms |
| Generation p50 / p95 | 1,801 / 2,142 ms | 2,428 / 4,060 ms | 4,246 / 6,597 ms |
| Output tokens, classification + generation | 14,520 | 26,229 | 37,401 |
| Held-out drafts with no citation at all | 0 | 8 | 10 |
| Held-out hard negatives drafted, judged grounded | 6, 5 | 6, 3 | 6, 1 |

Gate B's refusals are identical because retrieval does not involve the chat model. Gate A's
separation is the number [[ADR-019]]'s floors were chosen from, measured on a different
model, which is why they are re-checked [below](#the-routing-floors-re-checked).

`gpt-6-luna` writes about 2.8 times the incumbent's output tokens (37,401 against 13,270) and
is still the cheapest, because its rates are a quarter to a third of `gpt-4.1-mini`'s. It is also the slowest: a p95 generation
of 6.6 s is inside [[ADR-039]]'s 15 s attempt limit, at less than half of it.

## Calls made after the runs

**1. Verdicts with no `ResolvedModel`.** [[ADR-043]] says any `ResolvedModel` other than
`gpt-5-2025-08-07` voids the run. 8 `gpt-5.6-luna` and 10 `gpt-6-luna` verdicts carry none.
Every one is a draft that cited no sources, which the harness marks ungrounded without calling
the judge. The rule exists to catch a substituted judge, and these verdicts reached no judge,
so they are counted. Decided by the owner on seeing them. A literal reading would have voided
both luna runs. That changes nothing for `gpt-5.6-luna`, already out on citations. For
`gpt-6-luna` it would have left no groundedness result at all, rather than a different one.

**2. Which population "grounded ÷ judged" means.** The criterion compares against 53 of 87,
which excludes the six engineered hard negatives that cleared Gate B, because
`DECISION-RULE-V2.md`'s addendum keeps them out of every reported figure. The table above
uses the same exclusion. Read literally, over every judged draft including them, the figures
are 85/93 = 91.4%, 73/93 = 78.5% and 83/94 = 88.3%. Both readings clear the floor by more
than 25 pp for every candidate. The hard-negative counts are reported separately above, as
[[ADR-038]] does for the incumbent.

**3. Short-context rates for the luna models.** Azure prices both luna models at a
short-context and a long-context rate, and pricing 1.2 uses the short. These prompts run to
about 1–2K tokens, and the price list does not state the threshold. At the long-context
rates (0.20 in, 0.75 out) `gpt-6-luna` would cost USD 0.000734 per drafted ticket, still a
third of `gpt-5.4-mini`'s, so the selection would not change.

## Why the denominators differ

`gpt-6-luna` was judged on 88 scored drafts, the others on 87. The held-out pair export keeps
only drafts whose citations are all valid, so:

- `gpt-5.4-mini` produced one draft that cited something that is not a ticket id at all
  (`q-ho-returns-where-to-drop-off`). It failed to parse, so it counts neither as a draft nor
  as an invalid citation. It is a fabricated citation all the same.
- `gpt-5.6-luna`'s draft for `q-ho-fault-same-alert-several-copies` cited outside its
  retrieval set and was not exported.
- `gpt-6-luna` produced a valid draft for both.

## How the runs went

The first pass was reported as killed by the session for low memory at 11:11, during
`gpt-6-luna`'s full run. It was not: it went on to finish everything by 11:58. A restart
launched at about 11:15 therefore ran against the same database at the same time, and every
run resets that database when it starts. Both `gpt-6-luna` drafting runs from that window
overlapped those resets, were discarded unread, and were re-run alone. The restart's own last
run also failed on DNS errors when the network dropped.

The other two candidates' drafting runs finished by 11:02, before any overlap. Their judging
reads only the exported pair files, never the database, so it was unaffected.

## Weaknesses in this result

**One run per candidate, and the luna models are nondeterministic.** Both accept only the
default temperature (the amendment's smoke test), so a second run would differ. The F1 and
citation floors were set against the incumbent's spread at temperature 0, which may understate
theirs. `gpt-6-luna` cleared F1 by 4.1 pp and citations by 2.0 pp, the latter with no slack:
one invalid citation on a re-run would put it where `gpt-5.6-luna` is.

**The drafting model's version is not recorded by the harness.** [[ADR-041]] captures it for
the judge only. The evidence that each deployment served the intended version is the
amendment's smoke test (`gpt-6-luna-2026-09-22` and the others), plus `NoAutoUpgrade` on the
deployments.

**Ten `gpt-6-luna` drafts cited nothing.** None of them is a citation error, so citation
validity cannot see them, and all ten were judged ungrounded, so the groundedness figure
already counts them. A reviewer would see a confident draft with no sources. That is worth
watching after cutover.

**The groundedness figures are not a ranking.** 93.2% against the incumbent's 60.9% looks
like the headline, but it is one held-out run per model, judged by a judge that retires
2027-02-09. [[ADR-043]] made groundedness a floor because the sample cannot rank candidates
inside its interval, and the same caution applies to comparing the winner with the
incumbent. The published figure stays the incumbent's until a cutover run replaces it.

## The routing floors, re-checked

[[ADR-043]] blocks cutover until [[ADR-019]]'s floors are re-checked against the new model,
because they were chosen from `gpt-4.1-mini`'s scores. The check needs no new run: the
calibration tables are in `gpt-6-luna`'s full-run report, produced at the committed floors
(`MinimumClassificationScore` 0.5, `MinimumSimilarity` 0.6).

**Gate A stays at 0.50.** [[ADR-019]] made it a tripwire for scores that carry no
information, not a quality filter, and on `gpt-6-luna` it still behaves as one:

| floor | passes | correct among passed | gated | gated that were correct |
|---|---|---|---|---|
| 0.00 | 295 | 93.2% | 0 | 0 |
| **0.50** | **294** | **93.2%** | **1** | **1** |
| 0.80 | 286 | 93.4% | 9 | 8 |
| 0.90 | 247 | 97.2% | 48 | 35 |
| 0.95 | 205 | 98.5% | 90 | 73 |

At 0.50 it gates one ticket of 295, which would have been classified correctly; any floor
from 0.40 to 0.55 gives the same row. The five tickets with no score are the `content_filter`
rejections, which go to manual triage at any floor. The distribution does differ from the
incumbent's above 0.80, where `gpt-6-luna`'s scores spread out (0.90 gates 16% against
`gpt-5.4-mini`'s 4%), but the floor sits well below that region. Separation is 0.066, as
uninformative as [[ADR-019]] found the incumbent's, which is the reason Gate A is a tripwire
in the first place.

**Gate B stays at 0.60.** It scores retrieval, which uses the embedding model and not the
chat model. All three candidates refused the same 13 of 60 queries, including all six with no
correct answer.

Neither floor moves, so cutover is unblocked.

## What this does not do

It does not switch the drafter. Cutover points the chat deployment at `gpt-6-luna` with
`ChatTemperature=omit`, regenerates `evaluation-baseline.json` under pricing 1.2, and states
the cost change in the README.
