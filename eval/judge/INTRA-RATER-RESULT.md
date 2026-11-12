# Intra-rater re-label: the result

Produced under the procedure in `INTRA-RATER.md`, which was committed before this pass ran.
Nothing below was decided after seeing these numbers.

| | |
|---|---|
| First pass | 2026-11-07 20:36:42 +0200, commit `c404bb4` |
| Second pass | 2026-11-12 20:23:35 UTC |
| **Elapsed** | **5 days 1 hour 46 minutes** (floor was 72 hours) |
| Sample | 20 of 93, seed `20261102`, hash `30530804f4309d68` |
| Presentation order | seed `20261112`, different from pass 1 |
| Completion | 20/20, both fields, none skipped |

The second pass saw no label from the first: they were stripped at build time and their absence
asserted in the rendered page, which carried five text fields per pair and no boolean at all.

## The figures

**Raw agreement leads**, as pre-registered. The rest is reported, not decisive.

| | GROUNDED | ABSTAINED |
|---|---|---|
| Agreement | **19/20 = 95.0%** | **15/20 = 75.0%** |
| Wilson 95% CI | [76.4, 99.1] | [53.1, 88.8] |
| Cohen's κ | 0.886 | 0.419 |
| Gwet's AC1 | 0.911 | 0.584 |
| First-pass base rate | 70.0% | 15.0% |

Disagreements, which are the measurement rather than a defect list:

- **grounded** — `q-ho-product-commercial-use-guarantee`
- **abstained** — `q-ho-account-typo-in-registered-email`, `q-ho-fault-pdf-missing-content`,
  `q-ho-returns-money-held-back`, `q-ho-returns-too-heavy-to-post`,
  `q-ho-shipping-box-address-refused`

**No label was changed.** `INTRA-RATER.md` pre-registered that the first pass stands where the
two disagree, and `holdout-pairs.json` is byte-identical to `c404bb4`.

## Why three statistics, demonstrated rather than argued

`INTRA-RATER.md` predicted that κ would understate agreement under a skewed base rate and that
leading with it would be an arithmetic artefact rather than a finding. The two rows are a clean
natural experiment in exactly that:

- **grounded**, base rate 70% — barely skewed, so chance-correction barely bites, and κ 0.886
  sits close to AC1 0.911.
- **abstained**, base rate 15% — heavily skewed, and κ 0.419 falls far below AC1 0.584 on
  identical data.

Had this file reported κ alone it would have described a label the rater agrees with themselves
on three times in four as "moderate agreement", which is true of the statistic and misleading
about the labelling. Had it reported raw agreement alone, 75% would have looked respectable
without showing that most of it is available by answering "no" every time.

## The consequence: this is a ceiling, not a side measurement

**A judge cannot be shown to track these labels more consistently than the labeller tracks
them.** Whatever agreement a judge configuration reaches on abstention is bounded above by 75%
in the same sense, and a judge scoring materially higher would be evidence of something wrong
with the comparison rather than of an unusually good judge.

That lands differently on the two labels:

**Groundedness — the Tier 1 figure — is on firm ground.** 95% agreement, AC1 0.911, over a five
day gap. [[ADR-018]] makes the judge-agreement figure a precondition for publishing any
groundedness score, and this says the labels that figure is measured against are stable.

**Abstention is not, and `DECISION-RULE-V2.md`'s addendum leans on it.** That addendum asks for
appropriate-abstention labelling on the six engineered hard negatives that cleared Gate B. Those
six are now a 6-item denominator read through a label with 75% self-consistency. The arithmetic
is unforgiving: one flipped judgement moves that figure by 16.7 points, and the labelling itself
is not reliable to anything like that resolution.

**This result does not decide what to do about that**, deliberately — it is a measurement, and
`DECISION-RULE-V2.md`'s criteria are frozen. Three honest options, for a decision recorded
separately: publish the abstention breakdown with the 75% ceiling stated beside it; tighten the
abstention definition and re-label under a fresh pre-registration; or keep abstention as
internal signal and publish nothing from it. **None of them is "report it without the ceiling."**

## What this does not measure

Inter-rater agreement. One person labelled all 93 and the same person re-labelled 20, so nothing
here speaks to whether a different careful reader would produce the same labels. [[ADR-029]]
already records that the judge-agreement figure validates the judge *on this sample* rather than
in general; the same caveat applies one level down, and this file does not remove it.

At n=20 every interval here is wide — the groundedness CI runs from 76.4% to 99.1%. These
figures characterise the labelling. They do not certify it.
