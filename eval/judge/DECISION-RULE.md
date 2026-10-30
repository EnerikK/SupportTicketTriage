# Judge selection: the rule, fixed before any configuration was run

This file exists to be committed **before** the first judge configuration runs, so that
git history shows the rule predates the numbers it was applied to. Choosing a judge after
seeing which one scored best is how a comparison becomes a justification.

`DECISIONS.MD` carries the same decision as ADR-031, but that file is gitignored and
therefore cannot evidence anything to a reader of this repository. This one can.

## What is being chosen

Which model and reasoning effort scores groundedness, out of:

| configuration | status |
|---|---|
| `gpt-5`, reasoning effort default | the current judge |
| `gpt-5`, reasoning effort `low` | |
| `gpt-5-mini`, reasoning effort default | needs provisioning |

Every configuration judges **the same 47 draft/context pairs**, against the same
hand labels, under rubric `judge-v1`.

## The rule

**Pick the cheapest configuration whose agreement with the hand labels is not
significantly worse than `gpt-5` at default effort.**

- Comparison between configurations is by **McNemar's test on the discordant pairs**,
  α = 0.05. Every configuration judges the same items, so the comparison is paired;
  testing it with overlapping confidence intervals would discard that pairing and
  lose most of the power. With 47 items, two configurations differing by a handful of
  verdicts will not separate on overlapping Wilson intervals even when the paired test
  says they differ.
- Each configuration additionally reports its agreement with a **Wilson score interval**,
  which describes that configuration's own uncertainty. It is reported, not used for the
  between-configuration decision.
- "Cheapest" is measured cost per judging run over the same 47 pairs, from the harness's
  own token accounting at the rates in `eval/pricing/model-pricing.json`.

**If nothing separates** — no configuration is significantly worse than the baseline —
the cheapest wins, and the README says so explicitly rather than presenting the choice
as though the data compelled it.

## What this rule can and cannot detect

The exact two-sided binomial test on discordant pairs gives, when every disagreement
falls the same way:

| discordant pairs, all one direction | p | |
|---|---|---|
| 4 | 0.125 | not significant |
| **5** | **0.063** | **not significant** |
| **6** | **0.031** | **significant** |
| 7 | 0.016 | significant |

~~So at n = 47 a difference is only detectable when **at least six items go one way and
none the other**.~~

> **Corrected 2026-10-30, after the first comparison run.** The sentence above is wrong as
> a general statement, and the table above it is only the special case it describes. Six
> all one way is the *minimum discordant count* at which significance is reachable — it is
> not the only route, and more discordant pairs can be significant while split.
>
> What actually governs it is the split, not the count. With `d` discordant pairs and `k`
> the smaller side, p = 2·Σ(i≤k) C(d,i) / 2^d:
>
> | split | discordant | p | |
> |---|---|---|---|
> | 6–0 | 6 | 0.031 | significant |
> | 9–0 | 9 | 0.004 | significant |
> | 13–6 | 19 | 0.167 | not significant |
> | 14–5 | 19 | 0.064 | not significant |
> | 15–4 | 19 | 0.019 | significant |
> | 16–3 | 19 | 0.004 | significant |
>
> The first run produced 13–6 on 19 discordant pairs for `gpt-5-mini` against the
> baseline — p = 0.167, not significant — which is the row that showed the original
> wording up. A majority that large looks decisive and is not, because nineteen coin
> flips land 13–6 or worse about one time in six.
>
> **The practical conclusion is unchanged and if anything stronger:** this test separates
> configurations only when one is wrong about items the other gets right *lopsidedly*, and
> at this sample size it will usually fail to separate them. The rule will most likely
> select on cost.

Three consequences, stated now rather than discovered later:

1. **The rule will most likely select on cost.** Two configurations that differ by two or
   three verdicts will not separate, and the cheapest will win by default. That is the
   rule working as intended, not a failure of the comparison.
2. **"Not significantly worse" means "no detectable difference at this sample size."**
   It does **not** mean the configurations are equivalent. This test was never capable of
   demonstrating equivalence; it can only fail to find a difference, and failing to find
   one at n = 47 is weak evidence.
3. **The README must describe the result in those terms.** Not "gpt-5-mini matched gpt-5",
   which claims equivalence the data cannot support. The honest phrasing is that the
   configurations could not be distinguished on 47 hand-labelled pairs, that a difference
   smaller than roughly 13 points of agreement would have been invisible to this test, and
   that the cheaper one was taken on that basis.

Demonstrating equivalence properly would need an equivalence test with a pre-stated
margin, and a sample far larger than the drafts this corpus produces. That is out of
scope and is named here so the limitation is a known one rather than an oversight.

## Why this is written down first

Three outcomes were foreseeable before running anything, and the rule has to survive all
three rather than be chosen to fit whichever arrives:

1. The cheap configuration is indistinguishable → it wins, and the honest README sentence
   is "we could not tell them apart at n=47, so we took the cheap one."
2. The cheap configuration is significantly worse → the expensive one wins and the cost
   is justified by a measured difference.
3. The cheap configuration is significantly *better* → it wins under the rule, which
   already asks only that it not be worse.

At n=47 the first outcome is the most likely, and the rule says plainly what happens then.

## Label independence

The hand labels are committed before any judge configuration runs. The comparison records
the label file's content hash and the commit that last modified it, and refuses to run
against a label file with uncommitted changes. So the ordering is checkable from the
repository rather than asserted here.
