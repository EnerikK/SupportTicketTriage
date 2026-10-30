# Judge configuration comparison

Rubric `judge-v1`. Labels: `eval/judge/pairs-to-label.json`, sha256 `dd5b9cfc3c2e`, committed in `ff6eaf44`.

47 pairs judged, 46 labelled, so 46 compared.

Judged but deliberately unlabelled, and therefore excluded: `q-product-safe-for-young-user`. An unlabelled pair is dropped rather than guessed; counting it either way would put invented data into the figure this table exists to produce.

| configuration | agreement | 95% CI | kappa | grounded/ungrounded | output tokens | cost |
|---|---|---|---|---|---|---|
| `gpt-5` / Default | 54.3% | 40-68% | 0.12 | 5/42 | 69,920 | USD 0.7348 |
| `gpt-5` / Low | 67.4% | 53-79% | 0.36 | 13/34 | 26,246 | USD 0.2981 |
| `gpt-5-mini` / Default | 69.6% | 55-81% | 0.39 | 24/23 | 30,785 | USD 0.0687 |

Agreement is with the hand labels, which are one person's reading of the rubric rather than ground truth. A configuration agreeing less does not thereby judge worse; it judges differently from the labeller.

## Against the baseline (`gpt-5` / Default)

| configuration | baseline only correct | this only correct | p | |
|---|---|---|---|---|
| `gpt-5` / Low | 2 | 8 | 0.1094 | not significant |
| `gpt-5-mini` / Default | 6 | 13 | 0.1671 | not significant |

McNemar's exact test on discordant pairs, two-sided, alpha 0.05. Only items where exactly one configuration matched the label carry information; items both matched, or both missed, are excluded by construction.
