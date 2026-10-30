# Dev check — judge-v2 across three configurations

**This is not a reported number and never will be.** These 26 cases are the
development set: they are a subset of the 46 pairs that *produced* the v2 rules,
so a judge scoring well here has been fitted to them. Its only job is to answer
whether a mismatch is **rubric ambiguity** (fix the wording, log the change) or
**judge error** (record it, change nothing). The held-out run is the test.

One of the 46 is excluded: `q-product-how-to-look-after-it` turns on whether four
cited sources are the same product, and the dataset carries no product identifier
of any kind, so the premise cannot be established either way. The remaining 19
are not in the expectations file.

## Which rubric text produced these

All three runs used the **post-precedence-rule** rubric —
`PrecedentRules.EvidentiaryStatus` with the "where both apply, the second wins"
paragraph, and `GroundednessPrompt` with the worked `For example:` line added for
the parser contract test. Both edits landed before the build that these runs were
launched from.

The earlier gpt-5-mini **18/26** was produced under the *pre*-precedence text and
is therefore not comparable to the 19/26 below. It stands as recorded; it is not
a predecessor of these three.

From this point the exact text behind any verdict file is recorded as a SHA-256
in its provenance, so this paragraph never has to be reconstructed from memory
again. See `RUBRIC-CHANGELOG.md`.

## Results

Two scores, side by side. **Frozen** is against the expectations exactly as they
were committed before any v2 run. **Corrected** applies the two expectation
errors found after seeing verdicts (`q-shipping-says-delivered-but-nothing-here`,
`q-billing-bank-took-extra` — both grounded → ungrounded), logged in
`RUBRIC-CHANGELOG.md`. A single corrected number would hide that the correction
happened with the verdicts visible.

Correcting those two moves the balance from 11 grounded / 15 ungrounded to
**9 / 17**, which is why the false-flag denominator is 9.

| configuration | frozen | corrected | false flags | missed ungrounded |
|---|---|---|---|---|
| gpt-5-mini, default | 19/26 | 19/26 | 2/9 (22%) | **5/17** |
| gpt-5, default | 20/26 | 22/26 | **3/9 (33%)** | 1/17 |
| gpt-5, low | 21/26 | **23/26** | 2/9 (22%) | 1/17 |

The 25% false-flag ceiling in `DECISION-RULE-V2.md` is an eligibility gate on the
**held-out** run and does not apply here. Noted only because gpt-5 default sits
above it on development data, on a denominator of nine, where one case is 11
points — far too small to conclude anything.

## The split that matters

Not the totals. gpt-5-mini and the gpt-5 configurations fail in **opposite
directions**, and only one of those directions is cheap to live with.

**gpt-5-mini misses ungrounded drafts.** Five of seventeen, against one of
seventeen for both gpt-5 configurations, and in every one of the five it returned
no claims at all — it did not flag something trivial, it saw nothing. The four it
alone missed (`too-many-tries-now-blocked`, `money-back-short`,
`what-size-should-i-get`, `bank-took-extra`) are the generality test and the
causal-attribution rule, which are the two failure modes the metric exists to
catch. A groundedness metric that misses 29% of ungrounded drafts measures very
little.

**The gpt-5 configurations over-flag.** Their errors are in the direction that
costs a review, not the direction that lets a wrong draft through.

## Classification of the mismatches

| case | configs | direction | classified as |
|---|---|---|---|
| `q-returns-decided-against-it` | all three | missed | **disputed expectation** — the judges' reading is defensible |
| `q-login-two-registrations` | all three | flagged | **rubric ambiguity** — possibly a wrong expectation |
| `q-login-phone-replaced-no-codes` | both gpt-5 | flagged | **judge error** — offer read as promise |
| `q-parcel-missing` | mini, gpt-5 default | flagged | **judge error** — inconsistent across configs |
| mini's four extra misses | mini only | missed | **capability** — not a wording problem |

The discriminator is agreement across configurations: a mismatch one
configuration makes and another does not is, by construction, not forced by the
wording. `q-parcel-missing` is the clean case — identical text, identical rubric,
gpt-5 low passes it and gpt-5 default flags it.

**"All three agree" is weaker than it sounds.** gpt-5 default and gpt-5 low are
the same model at two reasoning efforts, so their errors are correlated by
construction and the two of them are closer to one opinion than two. Three
configurations agreeing is nearer to **two independent opinions** — gpt-5 and
gpt-5-mini — and those two share a training lineage as well. Agreement here is
evidence that the text reads the way they read it; it is not three witnesses.

### `q-returns-decided-against-it` — a disputed expectation, not a failed rule

This case motivated the precedence rule, and the rule's illustration is drawn
from it: *"Since the item is perfectly good, you can return it"* rests an
eligibility on the customer's own account. All three configurations call the
draft grounded and return no claims.

**On the full text the judges have the better reading.** The draft does not stop
at the restated account — it says the item can be returned *"within thirty days
of delivery **provided the item is unused and complete**"*, and the cited
resolution says *"Change-of-mind returns are accepted within thirty days of
delivery provided the item is unused and complete."* The operative eligibility
condition is the resolution's own, phrased in general terms, which the generality
test licenses as policy. The *"perfectly good"* clause is a restatement sitting
in front of an eligibility that does not depend on it.

So the illustration in `PrecedentRules.Precedent` is a simplified version of this
draft rather than a quotation of it, and the real draft is a weaker instance than
the rule's example. **Nothing changes**: not the rule, not the expectation, not
the recorded mismatch. The expectation is marked disputed and left standing, and
the rule is tested on fresh cases in the held-out set rather than re-argued
against the one case that produced it.

### `q-login-two-registrations` — ambiguity, and possibly a wrong expectation

All three flag the hedged causal explanation *"which can happen if one was
created with a personal address and another through an organisation
invitation"*. The expectation reads this as licensed hedging; the judges read the
conditional as asserting its consequent, which is what
`PrecedentRules.Precedent` says a conditional does.

Filed as rubric ambiguity, with the expectation itself in doubt — the hedging
clause and the conditional clause in that rule can both be applied to this
sentence and they point opposite ways. Not resolved here, for the same reason as
above: three configurations disagreeing with one derivation is not grounds for
rewriting either against the cases that produced them.

### `q-login-phone-replaced-no-codes` — judge error against explicit text

All three flagged claims are offers: *"we can verify"*, *"we can remove"*, *"help
you re-enroll"*. `PrecedentRules.Materiality` says in as many words that an offer
to investigate or assist is not material. The rubric is not ambiguous here; the
judge did not apply it. Recorded, nothing changed.

## What this does not decide

Nothing about which configuration is adopted. That is `DECISION-RULE-V2.md`
applied to newly labelled held-out pairs, and the rule was committed before any
of these numbers existed. The one thing carried forward is the direction split:
gpt-5-mini's cost is missed ungrounded drafts, and if the held-out run reproduces
that, cheapness does not buy it eligibility.
