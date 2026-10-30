# Smoke check: does `--judge-effort` actually reach the API?

**Run 2026-10-30, against Azure `gpt-5` (GlobalStandard, version 2025-08-07), rubric `judge-v1`.**

## Why this check exists

Reasoning effort is set through `ChatOptions.RawRepresentationFactory`, which hands the
provider an options object it is **free to ignore**. A setting that never reaches the API
produces verdicts indistinguishable from a working one, so the comparison table would
carry a row labelled "low effort" that was really another default-effort run. Nothing in
the verdicts themselves would reveal it.

Token counts are the only available evidence.

## What was measured

The same five pairs from `pairs-to-label.json`, judged twice per configuration —
`Default` (parameter not sent) against `Low` — via `--judge-pairs-from`, which regenerates
nothing.

| | output tokens, Default | output tokens, Low | reduction |
|---|---|---|---|
| run 1 | 7,536 | 3,323 | **56%** |
| run 2 | 8,948 | 3,346 | **63%** |

Per pair in run 2:

| pair | Default | Low | |
|---|---|---|---|
| `q-duplicate-charge` | 1,113 | 729 | 35% |
| `q-pii-heavy` | 1,878 | 737 | 61% |
| `q-billing-auto-renewal-surprise` | 1,238 | 795 | 36% |
| `q-billing-charged-after-stopping` | 3,416 | 862 | 75% |
| `q-billing-paperwork-for-expenses` | 1,303 | 223 | 83% |

**Every pair dropped, in both runs.** The setting reaches the API.

## Verdicts were unchanged

5 of 5 identical in both runs: 2 grounded, 3 ungrounded, 0 unjudged, under both
configurations. That is a hint and not a result — five pairs cannot show that `Low` judges
as well as `Default`, and the real comparison on 47 labelled pairs is what the decision
rule governs.

## Two findings worth carrying forward

**Reasoning tokens are not reported.** `AdditionalCounts` comes back with
`InputTokenDetails.AudioTokenCount`, `OutputTokenDetails.AudioTokenCount`,
`OutputTokenDetails.AcceptedPredictionTokenCount` and
`OutputTokenDetails.RejectedPredictionTokenCount` — and **no reasoning count at all**. The
`Microsoft.Extensions.AI` OpenAI bridge does not surface it on this path. The harness
prints "not reported" rather than zero, and output tokens stand in: a boolean verdict
costing 1,000–3,400 output tokens is almost entirely reasoning.

**Default-effort token spend is noisy between runs.** The same five pairs cost 7,536 then
8,948 output tokens — **19% more for identical input**. `Low` was stable (3,323 then
3,346, under 1%). So a cost comparison between configurations should not rest on a single
run of the expensive one, and the cost column in the comparison table needs that caveat
or an average over repeats.

## Reproducing

```
--judge-pairs-from eval/judge/pairs-to-label.json --judge-limit 5 \
  --judge-verdicts eval/judge/smoke-default.json

--judge-pairs-from eval/judge/pairs-to-label.json --judge-limit 5 --judge-effort low \
  --judge-verdicts eval/judge/smoke-low.json
```

`smoke-default.json` and `smoke-low.json` are committed beside this note. They are the
second run; run 1 was overwritten when the files were regenerated to fix enum
serialisation, and its totals are recorded in the table above rather than reconstructed.
