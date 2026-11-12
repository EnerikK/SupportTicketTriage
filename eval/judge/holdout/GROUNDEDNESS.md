# The groundedness score

The first and only groundedness figure this project publishes. Everything it rests on was
committed before it existed: the rubric, the selection rule, the labels, and the verdicts.

## The number

**60.9% of held-out drafts are grounded** — 53 of 87, 95% Wilson interval **[50.4, 70.5]**.

Judged by **`gpt-5` at Low reasoning effort**, selected under `DECISION-RULE-V2.md` and recorded
in `RESULT.md`. That judge **retires 2027-02-09**, and this figure does not outlive it as a
current measurement: a successor judge produces its own number, and this one becomes the
pre-migration reference it is compared against.

[[ADR-018]] makes the judge's agreement with hand labels a precondition for quoting the score at
all, so it belongs in the same breath: **77.4%** agreement, κ 0.51, over all 93 labelled pairs.

## What was counted

| | |
|---|---|
| Held-out queries | 111 (`eval/dataset/judge-holdout.json`) |
| Refused by Gate B, no draft produced | 18 — twelve engineered hard negatives, six ordinary queries |
| Drafted | 93 |
| Engineered hard negatives that cleared the floor | **6, excluded** |
| **Scored** | **87** |

The six exclusions are not a convenience. `DECISION-RULE-V2.md`'s addendum fixed, before any of
this ran, that those six "are not pooled with the ordinary queries in any reported figure" —
they were written to tempt the model with evidence that looks sufficient and is not, so a
corpus-wide rate containing them measures the engineering rather than the system. They are
reported separately and descriptively under [[ADR-038]].

The 18 refusals are also not failures being hidden. Gate B withholding a draft is the guardrail
working; a draft that was never written cannot be ungrounded. What the groundedness score
measures is the quality of drafts the system actually produced.

Drafts came from `gpt-4.1-mini` under generation prompt `draft-v2` (`83fccb7f…`), judged under
rubric `judge-v2` (`9a0b0300…`). Different model families, so no same-family disclosure is owed
here — though [[ADR-037]]'s amendment attaches one the day `gpt-5-mini` becomes the drafting
model.

## The score reads low, and here is why

The same 87 drafts carry hand labels, and **the labeller found 63 of 87 grounded — 72.4%**
[62.2, 80.7]. The judge reports 11.5 points lower.

| over the 87 | labels grounded | labels ungrounded |
|---|---|---|
| **judge grounded** | 49 | 4 |
| **judge ungrounded** | 14 | 20 |

The 14 are the gap. The selected judge flags 22.2% of the drafts a human calls grounded —
close to the 25.0% measured over all 93, which is the rate that only just cleared its
eligibility ceiling. **The published figure is therefore an understatement of draft quality by
roughly the judge's false-flag rate**, and that is a property of the measurement, not of the
drafts.

**No correction is applied.** It would be arithmetically easy to adjust 60.9% upward by the
judge's error rates and publish the result. That number would carry the uncertainty of two
estimates and read as more precise than either, and a figure nobody can recompute from the
committed artifacts is not a measurement. 60.9% is what the judge said; 72.4% is what the
labeller said; the gap is reported rather than resolved.

For the same reason the headline agreement figure is **77.4% over all 93 pairs**, not the 79.3%
the 87 ordinary drafts alone produce. The first is the pre-registered figure from the
comparison that selected the judge. The second is the more flattering of two slices, and
choosing it after seeing both is exactly the move this project's pre-registration discipline
exists to prevent.

## What a reader should not conclude

**Not that 60.9% is precise.** The interval runs from 50.4% to 70.5% — twenty points wide at
n=87. It is a first measurement, not a tracked metric.

**Not that it generalises past this dataset.** 111 synthetic held-out queries against a
240-ticket corpus. [[ADR-029]] already records that the judge is validated *on this sample*.

**Not that the labels are ground truth.** `INTRA-RATER-RESULT.md` measured the labeller's own
self-agreement on `grounded` at **95.0%** (AC1 0.911) over a five-day gap. That is high, and it
is still a ceiling: the 72.4% comparison figure inherits it, and no judge can be shown to track
these labels more consistently than the labeller does.

**Not that the judge was validated on data independent of its own selection.** The three
candidate configurations were compared using the labels on these same 93 pairs, so the selected
judge is the one that fitted this sample best among three. With three candidates and a
mechanical rule fixed in advance the bias is small, but it is not zero, and the honest fix is a
second held-out set rather than a sentence. There is not one yet.

## Reproducing it

```
dotnet run -c Release --project tools/SupportTicketTriage.Eval -- \
  --compare-judges eval/judge/verdicts-holdout-gpt5-default.json,eval/judge/verdicts-holdout-gpt5-low.json,eval/judge/verdicts-holdout-gpt5mini-default.json \
  --judge-labels eval/judge/holdout-pairs.json \
  --output eval/judge/holdout
```

That regenerates `COMPARISON.md`, which carries the agreement and error rates. The score itself
is the grounded count in `verdicts-holdout-gpt5-low.json` over the 87 query ids in
`judge-holdout.json` that are drafted and not marked `hard-negative`.

The judging run that produced those verdicts cost **USD 0.6743** for 93 pairs.

## Not in the regression gate

`docs/BUILD-SPEC.md` wants groundedness gated alongside classification, retrieval, citation
validity, latency and cost. It is not, and the reason is a population mismatch rather than
an oversight: `evaluation-baseline.json` is regenerated from runs over `eval/dataset/tickets.json`,
while this figure is over the held-out set. Putting a held-out number into a baseline CI
recomputes from dataset runs would produce a gate comparing two different things.

Closing it needs a decision about which population the gate judges and what a judging run costs
per CI invocation. Until then groundedness is published and ungated, and this sentence is the
record of that.
