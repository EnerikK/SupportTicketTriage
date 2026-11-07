# Intra-rater re-label: the procedure, fixed before the second pass

Committed **before the second labelling pass and before any held-out verdict exists**, so git
timestamps this ahead of every number it governs. The same discipline as `DECISION-RULE.md`
and `DECISION-RULE-V2.md`, applied to the labeller rather than to the judge.

This measures one thing: **how consistently the same person applies the `judge-v2` guidance to
the same drafts on two occasions.** It is not a quality check on the first pass, and it does not
correct it.

## Preconditions

| | |
|---|---|
| First pass committed | `c404bb4`, 2026-11-07 20:36:42 +0200 |
| First-pass seal | `5bdd18cf1b325a0165ced7fc131d8785baf0e478b2e592cd7ca1c3e510e9d795` |
| Sample | 20 ids, seed `20261102`, sha256[:16] `30530804f4309d68` |
| Earliest start | **2026-11-10 20:36 +0200** — 72 hours after the first pass |
| Held-out verdicts | **none may exist.** Verify before starting |

The seal is sha256 over `queryId\tgrounded\tabstained` for all 93 pairs, id-sorted, taken from
the committed blob. It is what lets a reader check afterwards that the first-pass labels were
not revised to improve agreement.

## The sample was redrawn, and why

The original sample — seed `20261101`, hash `260d8e1be7c91c78`, recorded in
`docs/PROJECT-STATE.md` §2c — **is abandoned.**

While preparing this procedure on 2026-11-07, a verification command printed the first-pass
label distribution *within that sample*: how many of those 20 the labeller had marked
grounded, ungrounded and abstained. That is marginal information rather than per-item labels,
so the leak is weak — but knowing roughly what fraction were ungrounded could move a borderline
call, and the measurement's whole value is that nothing from the first pass reaches the second.

The redraw: **seed `20261102`, 20 ids drawn from the 73 pairs outside the original sample,
overlap zero.** The seed was fixed in writing before the draw was made. **No other seed is to
be tried.** A sample redrawn until it looks convenient is not a sample, and the one-leak,
one-redraw sequence is recorded here precisely so that a second redraw would be visibly a
departure rather than a tidy-up.

No distribution has been computed over the new sample.

Reproducible from the committed pair file alone:

```python
import json, random, hashlib
ids = sorted(p['queryId'] for p in json.load(open('eval/judge/holdout-pairs.json', encoding='utf-8-sig'))['pairs'])
original = set(random.Random(20261101).sample(ids, 20))     # abandoned, see above
pool = sorted(set(ids) - original)                           # 73 remaining
sample = sorted(random.Random(20261102).sample(pool, 20))
assert hashlib.sha256('\n'.join(sample).encode()).hexdigest()[:16] == '30530804f4309d68'
```

Verified on CPython 3.13.1. `random.Random(seed).sample` is reproducible across versions for a
list pool, but the assertion is what matters: if it ever fails, the sample in use is not the one
this file pre-registered, and that is the thing a reader needs to be able to check.

## The 72-hour minimum

A re-label done hours after the first measures memory, not consistency, and will overstate
agreement — the labeller recalls the decision rather than remaking it. 72 hours does not
eliminate recall over 20 distinctive drafts, and is not claimed to.

**The elapsed time is published beside the figure** so a reader can discount accordingly, and
the judge run waits for it. Longer is better; this is a floor, not a target.

## Procedure

1. **Commit this file**, seal included.
2. **Build a second labelling page** over the 20 sampled pairs only: fresh shuffle seed, a
   *different* `localStorage` key, both label fields blank. It must not read the first pass's
   storage, or it will silently restore those labels.
3. **Label without reference to the first pass**, under the same `judge-v2` guidance.
4. **Write to `eval/judge/intra-rater-pass2.json`.** Never into `holdout-pairs.json`.
5. **Report**, separately for `grounded` and `abstained`, with the elapsed time beside them.

## What is reported, and which number leads

**Raw agreement is the primary figure.** At n=20 with a skewed base rate — the first pass over
all 93 ran roughly three-quarters grounded — chance-corrected statistics become unstable:
kappa's correction is largest exactly where one class dominates, so it can read low while the
two passes agree on nearly every item. Leading with kappa here would understate consistency for
an arithmetic reason rather than a labelling one.

Reported alongside, none of them decisive:

- **Cohen's kappa**, because raw agreement alone is inflated under skew and omitting the
  correction entirely would invite the obvious objection.
- **Gwet's AC1**, which is designed for exactly this case and is far less sensitive to
  prevalence than kappa.
- **The first-pass base rate within the sample**, so a reader can see the skew the other two
  numbers are reacting to.
- **Confidence intervals on all three.** At n=20 they are wide, and that is the finding: this
  figure characterises the labelling, it does not certify it.

Reporting three statistics is not hedging. Kappa and AC1 disagree systematically under skew,
and publishing only whichever flatters the result is the failure this file exists to prevent.

## Pre-registered: the first pass stands

Where the two passes disagree, **the first-pass label is the label of record and is not
corrected.** `DECISION-RULE-V2.md` says of the v1 development labels that *"a correction made
after seeing the verdicts is anchored by having seen them"*; the same holds for a correction
made after seeing a second pass.

The disagreements **are the measurement**, not a defect list. A procedure that let the second
pass overwrite the first would measure nothing and would quietly improve the labels the judge
is scored against.

## What this does not measure

Inter-rater agreement. One person labelled all 93, so nothing here speaks to whether a
different careful reader would produce the same labels — which is the stronger claim and the
one this project cannot make. `ADR-029` already records that the judge-agreement figure
validates the judge *on this sample* rather than in general; the same caveat applies one level
down, to the labels themselves.
