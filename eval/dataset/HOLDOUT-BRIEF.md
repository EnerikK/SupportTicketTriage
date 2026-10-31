# Authoring brief — the held-out query set

**Read this file and the existing dataset. Read nothing else.** The blindness rule
below is the entire reason this set is worth having, and it is cheap to break by
accident.

---

## Why this set exists

The evaluation harness turns a *query ticket* into a drafted reply, grounded in
resolved tickets retrieved from a 240-ticket corpus. A human then reads each
draft beside the evidence it was given and labels whether the draft is supported
by that evidence. Those labels are used to check whether an automated judge
agrees with a person.

The existing 60 query tickets cannot be used for that check, because they were
read closely while the automated judge's instructions were being written. A set
scored well by a judge whose rules were derived from it proves nothing. This set
is the replacement: queries nobody has looked at while writing those rules.

---

## The blindness rule

**Do not read, open, grep, or ask about any of the following while authoring:**

- `eval/judge/` — anything in it
- `src/SupportTicketTriage.Infrastructure/Ai/PrecedentRules.cs`
- `src/SupportTicketTriage.Infrastructure/Ai/GenerationPrompt.cs`
- `tools/SupportTicketTriage.Eval/Judging/` — anything in it
- `DECISIONS.MD` entries ADR-029, ADR-031, ADR-033, ADR-034, ADR-035

**Why, in one sentence:** someone who knows the rules a draft is scored against
will unconsciously write tickets that produce drafts those rules handle cleanly,
and the resulting figure would measure the rules against their own author rather
than against anything independent.

If you find yourself wondering "would this one be judged fairly" — that is the
thought the rule exists to prevent. Write the ticket a customer would send.

You **should** read `eval/dataset/tickets.json`. It is the style reference.

---

## What to produce

One new file, `eval/dataset/judge-holdout.json`, same shape as `tickets.json` but
containing **query-role tickets only**. It does not repeat the corpus; the harness
seeds the corpus from `tickets.json` and takes only the queries from here.

```json
{
  "version": "1.0",
  "categories": ["Billing", "Account & Login", "Technical Fault",
                 "Shipping & Delivery", "Returns & Refunds", "Product Question"],
  "tickets": [
    {
      "id": "q-ho-billing-charged-twice-same-day",
      "role": "query",
      "category": "Billing",
      "priority": "High",
      "subject": "Two payments for one order",
      "body": "I ordered once on Tuesday but there are two payments on my statement for the same amount.",
      "traits": []
    }
  ]
}
```

**`relevantIds` is deliberately absent.** Those are retrieval relevance labels and
this set is not used to measure retrieval. Omit the field entirely rather than
writing `[]` with nothing considered — an empty list that was never thought about
and an empty list that means "nothing applies" should not look identical.

---

## Count and distribution

**105 queries**, which yields roughly 90 drafts after the ones that legitimately
produce no draft.

| category | count |
|---|---|
| Billing | 18 |
| Account & Login | 18 |
| Technical Fault | 17 |
| Shipping & Delivery | 18 |
| Returns & Refunds | 17 |
| Product Question | 17 |

Traits, across the whole set:

| trait | count | what it means here |
|---|---|---|
| *(none)* | 75 | an ordinary, clearly-worded customer problem |
| `near-duplicate` | 18 | closely resembles a corpus ticket, but differs in one material detail |
| `hard-negative` | 12 | plausible for this product, but the corpus contains nothing that resolves it |

**Do not include `malformed` or `injection` tickets.** The main dataset already
carries them and they test the redaction and guardrail paths, not this. They
produce no draft, so here they would only shrink the set.

A handful of tickets may include realistic personal details (a name, an order
number, an address fragment) — tag those `pii`, in addition to any other trait.
Aim for about 6. Redaction is tested elsewhere; this is for realism.

---

## What makes a good item

Write what a real person would send to a support desk. Specifically:

- **Vary the length.** Some are one line. Some are four sentences with background
  nobody asked for. A set where every ticket is two tidy sentences is not realistic
  and will not stress anything.
- **Vary the competence.** Some customers state the problem precisely. Others
  describe a symptom, guess at a cause, and are wrong about the cause.
- **Vary the temperature.** Most are neutral. A few are annoyed. One or two are
  apologetic about bothering you.
- **Let them include things the support desk cannot verify.** Customers assert
  what they did, what they saw, and what they were told. That is normal and it
  should be in the set.
- **Vary what they want.** A refund, an explanation, a replacement, a timescale,
  or just acknowledgement.

`near-duplicate` means it reads like a corpus ticket until one detail differs —
a different timeframe, a different payment method, an action already taken. Find
the corpus ticket first, then change one thing that matters.

`hard-negative` means a believable problem for this product with nothing in the
corpus that answers it. Do not make it absurd; absurd is easy. Make it the kind
of question a support desk genuinely has no precedent for yet.

---

## Rules

1. **Ids must not collide** with any id in `tickets.json`. Use the `q-ho-` prefix
   (`q-ho-<category>-<short-slug>`) so the two sets stay distinguishable forever.
2. **No ticket may restate an existing query.** Check subjects and bodies against
   the 60 in `tickets.json`. Similar situations are fine; same ticket is not.
3. **Every ticket needs a plausible resolution path in the product's world** —
   except the `hard-negative` ones, where the absence is the point.
4. `priority` is one of `Low`, `Medium`, `High`. Use judgement; it is not scored.
5. `category` must be exactly one of the six listed, spelled as above.

---

## When finished

Validate before handing it back:

```bash
python -c "
import json,io,collections
d=json.load(io.open('eval/dataset/judge-holdout.json',encoding='utf-8'))
t=d['tickets']
m=json.load(io.open('eval/dataset/tickets.json',encoding='utf-8'))['tickets']
existing={x['id'] for x in m}
ids=[x['id'] for x in t]
assert len(ids)==len(set(ids)), 'duplicate ids within the file'
assert not (set(ids) & existing), 'ids collide with tickets.json'
assert all(x['role']=='query' for x in t), 'non-query role present'
assert all('relevantIds' not in x for x in t), 'relevantIds should be omitted'
print(len(t),'queries')
print('by category:',dict(collections.Counter(x['category'] for x in t)))
print('traits     :',dict(collections.Counter(tr for x in t for tr in (x.get('traits') or ['<none>']))))
"
```

Report the counts. Do not run the harness, and do not generate any drafts — the
drafts are produced in a later step and read by a human before any judge sees
them.
