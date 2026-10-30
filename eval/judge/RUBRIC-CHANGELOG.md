# Rubric changes, and why each was made

Every change to `judge-v2` after it was first written, with the evidence that prompted it.
Kept because a rubric that quietly improves between runs is indistinguishable from one
tuned until the judge agrees with it.

The governing rule is in `DECISION-RULE-V2.md`: a dev-check mismatch is either **rubric
ambiguity** (fix the wording, log it here) or **judge error** (record it, change nothing).
The wording is never iterated toward a better dev score.

---

## 2026-10-30 — precedence between the account rule and the eligibility rule

**Prompted by** `q-returns-decided-against-it` in the first dev check.

**The ambiguity.** Two clauses genuinely conflicted. *"The item is perfectly good"* is the
customer reporting what they saw, and the rubric says the customer is authoritative about
their own experience, restatable directly. The same sentence also bears on **eligibility**,
and the rubric says eligibility claims need attribution or resolution support. Both
readings were available and the text gave no way to choose.

**The change.** A precedence rule rather than a ruling on this case:

> Where both apply, the second wins. An account may be restated directly, but once a draft
> uses it as the basis for an eligibility, a cause or an outcome, attribution is required.

**What makes this not score-tuning.** The expected verdict for
`q-returns-decided-against-it` is **unchanged at ungrounded**, so the mismatch survives the
fix. A change made to raise the dev score would have resolved it the other way. The fix
makes the rubric decidable; it does not make the judge right.

**Version.** `judge-v2` is not bumped. It has produced no published figure, and no labels
have been collected under it - the 46 development labels are `judge-v1`. A version exists
to stop an agreement figure being compared across rubrics, and there is no such figure yet.

---

## Post-hoc corrections to the dev-check expectations, same date

Not rubric changes. These are cases where the expectation was derived wrongly and the
judge was right, **noticed after seeing the verdicts**, which is why they are logged
separately and why both scores are reported.

| case | expected | correct | why the expectation was wrong |
|---|---|---|---|
| `q-shipping-says-delivered-but-nothing-here` | grounded | **ungrounded** | The draft says *"**When this happens**, a location trace **is** raised"* against a past-tense resolution (*"was raised"*). That is the generality test firing correctly. Its twin `q-parcel-missing` says *"**In similar cases**…"*, which is licensed framing - same source, opposite verdicts, and the derivation only checked one. |
| `q-billing-bank-took-extra` | grounded | **ungrounded** | The draft asserts *"does not appear on our invoice, which shows the exact amount we requested"*, a system-state claim the derivation never examined. It was derived from the headline phrase rather than by auditing every claim. |

Both scores are reported side by side wherever the dev check is quoted: **against the
frozen expectations**, and **corrected**. A single corrected number would hide that the
correction happened after the verdicts were visible.

## Why this file is not enough on its own

This changelog is written by hand, which means it records what someone
remembered to record. The precedence rule above was added between two dev-check
runs that both carry the label `judge-v2`, and nothing in either run's output
could have told them apart — reconstructing which text produced which took
reading the order of edits against the order of builds.

Bumping to `judge-v3` for every wording change is the wrong fix. The version is a
label a human chooses, and it should move when the *contract* moves — when a
published figure or a set of labels depends on it. judge-v2 was amended twice in
one afternoon and neither amendment deserved a bump, because nothing depended on
it yet.

So the label and the identity are now separate things. Every verdict file records
`rubricSha256` and `generationPromptSha256`, computed at run time from the prompt
text itself:

- `GroundednessPrompt.Sha256` and `GenerationPrompt.Sha256`, over the text with
  the per-request delimiter replaced by a fixed placeholder — the delimiter is
  random by design under ADR-002, so hashing the rendered prompt naively would
  produce a fresh hash every run and identify nothing.
- Nullable on the record, because the three v1 comparison artifacts predate the
  mechanism and genuinely have no hash. Claiming non-null and deserialising to
  null would be a worse record than an honest blank.

The label stays a decision. The hash is a fact.
