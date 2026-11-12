# Judge configuration comparison

Rubric `judge-v2`. Labels: `eval/judge/holdout-pairs.json`, sha256 `e372c34d548a`, committed in `c404bb4a`.

93 pairs judged, 93 labelled, so 93 compared.

## The selection criteria

`eval/judge/DECISION-RULE-V2.md`, committed before any `judge-v2` configuration judged a pair. A configuration is eligible only if its false-flag rate is at most 25%; among eligible configurations the order is lowest missed-ungrounded, then lowest false-flag, then cheapest. If none is eligible, none is adopted and no groundedness score is published.

| configuration | missed-ungrounded | false-flag | eligible |
|---|---|---|---|
| `gpt-5` / Default | 4/25 = 16.0% [6, 35] | 22/68 = 32.4% [22, 44] | **no** - false-flag over the 25% ceiling |
| `gpt-5` / Low | 4/25 = 16.0% [6, 35] | 17/68 = 25.0% [16, 36] | yes |
| `gpt-5-mini` / Default | 11/25 = 44.0% [27, 63] | 17/68 = 25.0% [16, 36] | yes |

**missed-ungrounded** is over the pairs the labels call ungrounded; **false-flag** is over the pairs the labels call grounded. Two different denominators, because the two rates answer questions about two different sets of drafts - averaging them is what the v1 rule did, and it named as winner the configuration most likely to certify the failure the measurement exists to catch.

Intervals are Wilson at 95%. The ceiling is applied to the **point estimate**, as the rule says in terms: at these denominators the intervals are wide enough that testing the bound instead would let almost anything through.

This table reports the rates and eligibility. **It does not name a winner.** The third tiebreak is cost, which is null for any model absent from the pricing file, and a selection that silently depended on how complete that file happened to be would be worse than three ordered comparisons a reader can make from the rows above.

### McNemar, restricted to the ungrounded-labelled pairs

| configuration | baseline only correct | this only correct | p | |
|---|---|---|---|---|
| `gpt-5` / Low | 2 | 2 | 1.0000 | not significant |
| `gpt-5-mini` / Default | 8 | 1 | 0.0391 | **significant** |

The comparison the primary criterion rests on, and the only one the rule asks for: restricted to pairs the labels call ungrounded, so it speaks to missed-ungrounded alone. The all-pairs version below mixes both error directions.

## Reported, not decisive

Overall agreement, its interval and Cohen's kappa describe each configuration. They are not used to select, because averaging the two error directions is what produced the v1 result.

| configuration | agreement | 95% CI | kappa | grounded/ungrounded | output tokens | cost |
|---|---|---|---|---|---|---|
| `gpt-5` / Default | 72.0% | 62-80% | 0.42 | 50/43 | 161,834 | USD 1.7532 |
| `gpt-5` / Low | 77.4% | 68-85% | 0.51 | 55/38 | 53,941 | USD 0.6743 |
| `gpt-5-mini` / Default | 69.9% | 60-78% | 0.29 | 62/31 | 72,995 | USD 0.1729 |

Agreement is with the hand labels, which are one person's reading of the rubric rather than ground truth. A configuration agreeing less does not thereby judge worse; it judges differently from the labeller.

### Against the baseline (`gpt-5` / Default), all labelled pairs

| configuration | baseline only correct | this only correct | p | |
|---|---|---|---|---|
| `gpt-5` / Low | 2 | 7 | 0.1797 | not significant |
| `gpt-5-mini` / Default | 12 | 10 | 0.8318 | not significant |

McNemar's exact test on discordant pairs, two-sided, alpha 0.05. Only items where exactly one configuration matched the label carry information; items both matched, or both missed, are excluded by construction.
