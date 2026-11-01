# Hold-out set — authoring provenance

> **Do not read this before the hand-labelling pass is finished.**
>
> This file records what each non-ordinary query was built from and what it was
> built to do. A labeller who has read it knows which drafts are supposed to be
> ungrounded before reading them, which is the one thing the hold-out set exists
> to avoid. It is analysis-only ground truth: use it when interpreting the
> results, not when producing them.

Companion to `judge-holdout.json`. Authored blind to `DECISIONS.MD` ADR-029/031/
033/034/035, `eval/judge/`, `PrecedentRules.cs`, `GenerationPrompt.cs` and
`tools/SupportTicketTriage.Eval/Judging/`, per `HOLDOUT-BRIEF.md`.

`top-1` columns are cosine similarity against the 240-ticket retrieval corpus,
measured with `text-embedding-3-small` over `Redact(subject + "\n\n" + body)` —
the same deployment and the same input retrieval builds. Gate B floor is 0.60.

---

## Near-duplicates (18)

Each was written by taking one corpus ticket and changing a single detail that
changes the answer. The source is the ticket the author worked from, which is
not always the ticket retrieval actually ranks first.

### `q-ho-billing-new-card-still-refused`

- **Source** `billing-card-expired`
- **Detail flipped** The customer has already done the thing the corpus resolution prescribes - the replacement card is on the account - and the payment failed anyway.
- **Measured** top-1 0.602 against `billing-card-expired` — clears Gate B

### `q-ho-billing-reminder-came-after-charge`

- **Source** `billing-renewal-reminder-missed`
- **Detail flipped** The reminder was received, not filtered into junk. It arrived two days after the charge instead of fourteen days before it.
- **Measured** top-1 0.619 against `billing-renewal-unexpected` — clears Gate B  _(ranks a different ticket first than the source)_

### `q-ho-billing-code-entered-before-paying`

- **Source** `billing-discount-code-not-applied`
- **Detail flipped** The code was entered before the payment step and visibly applied to the basket, which is the opposite of the corpus case.
- **Measured** top-1 0.668 against `billing-discount-code-not-applied` — clears Gate B

### `q-ho-account-codes-refused-clock-checked`

- **Source** `login-mfa-codes-rejected`
- **Detail flipped** The clock-drift cause is explicitly ruled out by the customer before they write in.
- **Measured** top-1 0.754 against `login-mfa-codes-rejected` — clears Gate B

### `q-ho-account-locked-after-vpn`

- **Source** `login-locked-after-travel`
- **Detail flipped** The unfamiliar-location trigger is a corporate VPN while sitting at home, not foreign travel.
- **Measured** top-1 0.644 against `login-locked-after-travel` — clears Gate B

### `q-ho-account-closed-three-weeks-ago`

- **Source** `login-reactivate-closed-account`
- **Detail flipped** Roughly twenty-one days, inside the thirty-day window, rather than 'some months ago' and past erasure.
- **Measured** top-1 0.593 against `login-reactivate-closed-account` — **fails** Gate B

### `q-ho-fault-new-records-not-searchable`

- **Source** `fault-search-stale-results`
- **Detail flipped** Index lag on additions rather than on deletions: new records missing instead of deleted ones persisting.
- **Measured** top-1 0.658 against `fault-search-stale-results` — clears Gate B

### `q-ho-fault-export-disagrees-with-screen`

- **Source** `fault-report-numbers-mismatch`
- **Detail flipped** The screen is self-consistent and the export is the outlier, inverting which artefact is wrong.
- **Measured** top-1 0.661 against `fault-report-numbers-mismatch` — clears Gate B

### `q-ho-fault-export-missing-a-column`

- **Source** `fault-export-empty-file`
- **Detail flipped** Rows are present and a column is absent, so it is column selection carried into the export rather than a row filter.
- **Measured** top-1 0.685 against `fault-export-empty-file` — clears Gate B

### `q-ho-shipping-neighbour-despite-instruction`

- **Source** `shipping-left-with-neighbour`
- **Detail flipped** The no-neighbour preference had already been set on the account, which is the corpus ticket's remedy.
- **Measured** top-1 0.668 against `shipping-left-with-neighbour` — clears Gate B

### `q-ho-shipping-address-not-found-but-correct`

- **Source** `shipping-apartment-number-missing`
- **Detail flipped** The flat number did reach the label. A broken entryphone is the real cause and the customer says so.
- **Measured** top-1 0.743 against `shipping-apartment-number-missing` — clears Gate B

### `q-ho-shipping-strike-delay`

- **Source** `shipping-delayed-weather`
- **Detail flipped** Industrial action rather than weather, and the customer asks for the expedited charge back rather than an explanation.
- **Measured** top-1 0.606 against `shipping-next-day-not-next-day` — clears Gate B  _(ranks a different ticket first than the source)_

### `q-ho-returns-faulty-postage-not-refunded`

- **Source** `returns-refund-less-postage`
- **Detail flipped** Fault rather than change of mind, which reverses the outbound-postage rule the corpus resolution turns on.
- **Measured** top-1 0.704 against `returns-refund-less-postage` — clears Gate B

### `q-ho-returns-returned-the-free-one`

- **Source** `returns-bundle-discount-recalculated`
- **Detail flipped** The item returned was the free one under the three-for-two, which the customer reasons should change nothing.
- **Measured** top-1 0.755 against `returns-bundle-discount-recalculated` — clears Gate B

### `q-ho-returns-bought-just-before-november`

- **Source** `returns-window-extended-christmas`
- **Detail flipped** Purchased 24 October, before the 1 November start of the extended window, so the extension does not apply.
- **Measured** top-1 0.695 against `returns-window-extended-christmas` — clears Gate B

### `q-ho-product-between-sizes-runs-small`

- **Source** `product-runs-small`
- **Detail flipped** Combines product-fit-between-sizes with this one and asks whether the two stack into sizing up twice.
- **Measured** top-1 0.618 against `product-fit-between-sizes` — clears Gate B  _(ranks a different ticket first than the source)_

### `q-ho-product-colour-already-faded`

- **Source** `product-colour-fade-washing`
- **Detail flipped** Post-purchase and already faded after three washes, which the corpus resolution classes as a fault, rather than a pre-purchase concern.
- **Measured** top-1 0.550 against `product-colour-fade-washing` — **fails** Gate B

### `q-ho-product-spares-for-discontinued`

- **Source** `product-discontinued-replacement`
- **Detail flipped** Asks whether the successor model's spare part fits the older discontinued one, crossing into product-spare-parts-availability.
- **Measured** top-1 0.612 against `product-compatibility-older-model` — clears Gate B  _(ranks a different ticket first than the source)_

---

## Hard negatives (18)

Each is a believable request for this product that the corpus cannot resolve.

The first twelve were written for plausibility alone and all score below the 0.60
retrieval floor, so they are refused before a draft is attempted. The last six were
written **deliberately to clear 0.60** - anchored on a dense corpus cluster, often
mirroring one ticket's opening almost verbatim, with the unanswerable turn in a late
clause. They are similarity-engineered rather than naturally sampled, and that is the
point: they are the only queries in the set that put tempting-but-insufficient evidence
in front of the drafting stage. Treat **hard negatives that received a draft** as its
own metric; the first twelve cannot contribute to it.
The adjacent material is what retrieval is expected to surface anyway — the
temptation the draft has to decline.

### `q-ho-billing-payment-terms-request`

- **Adjacent corpus material** billing-po-number-missing, billing-bank-transfer-unmatched, billing-invoice-copy-request
- **Why none of it resolves the ticket** All concern invoice mechanics for customers already on account. Nothing in the corpus grants credit terms or describes an application for them.
- **Measured** top-1 0.512 against `billing-consolidated-invoices` — fails Gate B

### `q-ho-billing-entity-change`

- **Adjacent corpus material** billing-invoice-wrong-address, billing-vat-number-update, login-account-merge-request
- **Why none of it resolves the ticket** Each changes a field on an existing billing profile. None transfers a live subscription to a different legal entity.
- **Measured** top-1 0.531 against `billing-invoice-wrong-address` — fails Gate B

### `q-ho-account-hand-over-to-colleague`

- **Adjacent corpus material** login-account-merge-request, login-duplicate-accounts, login-admin-removed-access, login-wrong-email-on-account
- **Why none of it resolves the ticket** Merging two accounts held by the same person is the nearest thing present, and it is explicitly conditioned on both being the same person. Ownership transfer is absent.
- **Measured** top-1 0.502 against `login-account-deletion-request` — fails Gate B

### `q-ho-account-security-key-instead`

- **Adjacent corpus material** login-mfa-new-phone, login-mfa-sms-not-arriving, login-authenticator-app-reinstalled
- **Why none of it resolves the ticket** The corpus knows exactly three second factors: authenticator app, SMS and recovery codes. Hardware keys and passkeys appear nowhere.
- **Measured** top-1 0.517 against `login-mfa-new-phone` — fails Gate B

### `q-ho-fault-scheduled-export-to-sftp`

- **Adjacent corpus material** fault-export-timeout-large, fault-webhook-not-firing
- **Why none of it resolves the ticket** The first moves large exports to a background job that emails a link, the second is event delivery. Neither is scheduled file delivery to customer infrastructure.
- **Measured** top-1 0.427 against `fault-export-timeout-large` — fails Gate B

### `q-ho-fault-screen-reader-problems`

- **Adjacent corpus material** fault-mobile-layout-broken, fault-browser-unsupported, fault-fonts-not-rendering
- **Why none of it resolves the ticket** Rendering and client-capability faults, which is the nearest shape. No assistive-technology precedent exists anywhere in the 240.
- **Measured** top-1 0.416 against `login-sso-domain-mismatch` — fails Gate B

### `q-ho-shipping-locker-delivery`

- **Adjacent corpus material** shipping-po-box-rejected, shipping-collect-from-depot, shipping-delivery-window-request
- **Why none of it resolves the ticket** Collection from a depot after a failed attempt exists; choosing a locker or pickup point at checkout does not.
- **Measured** top-1 0.541 against `shipping-delivery-window-request` — fails Gate B

### `q-ho-shipping-take-old-one-away`

- **Adjacent corpus material** returns-large-item-collection, shipping-damaged-in-transit
- **Why none of it resolves the ticket** Collection of a large item exists, but only as a return of our own goods. Removal of the customer's old unit on delivery has no precedent.
- **Measured** top-1 0.472 against `shipping-collect-from-depot` — fails Gate B

### `q-ho-returns-bought-from-reseller`

- **Adjacent corpus material** returns-warranty-claim-process, product-forwarded-listing-injection
- **Why none of it resolves the ticket** The warranty ticket says claims in the first year are handled 'by us directly as the seller', which is precisely the premise that fails here. Tempting and wrong.
- **Measured** top-1 0.501 against `shipping-delivered-to-wrong-address` — fails Gate B

### `q-ho-returns-dispose-instead-of-return`

- **Adjacent corpus material** returns-large-item-collection, product-recyclable-packaging, returns-faulty-item
- **Why none of it resolves the ticket** Packaging recycling and large-item collection both land nearby. Take-back, or a refund without the goods coming back, does not exist.
- **Measured** top-1 0.577 against `product-recyclable-packaging` — fails Gate B

### `q-ho-product-humid-climate`

- **Adjacent corpus material** product-oven-safe-limits, product-storage-advice, product-expected-lifespan
- **Why none of it resolves the ticket** Temperature ratings and storage conditions are published; an operating environment range covering humidity or altitude is not.
- **Measured** top-1 0.390 against `product-oven-safe-limits` — fails Gate B

### `q-ho-product-different-plug-voltage`

- **Adjacent corpus material** shipping-restricted-item-country, product-country-of-origin, product-compatibility-older-model
- **Why none of it resolves the ticket** Country appears in the corpus only as a shipping restriction and a manufacturing origin, never as a product variant.
- **Measured** top-1 0.355 against `product-which-model-for-use-case` — fails Gate B

### `q-ho-billing-instalment-plan-after-return`

- **Adjacent corpus material** billing-refund-pending-bank, billing-direct-debit-returned, returns-refund-timing-nearduplicate
- **Why none of it resolves the ticket** Every refund-timing ticket in the corpus assumes the money went out on a card or a gift balance and comes back the same way. A third-party instalment provider that keeps collecting after the refund is settled appears nowhere, and none of the retrieved tickets says who stops it.
- **Measured** top-1 0.616 against `billing-refund-pending-bank` — clears Gate B

### `q-ho-account-cannot-pass-verification`

- **Adjacent corpus material** login-mfa-lost-device, login-mfa-backup-codes-lost, login-mfa-new-phone
- **Why none of it resolves the ticket** login-mfa-lost-device is almost the same ticket and names the verification route: the last two order numbers and the registered billing postcode. This customer has no orders and did not set the account up, so the corpus's only identity route is unavailable and no alternative exists in it.
- **Measured** top-1 0.747 against `login-mfa-lost-device` — clears Gate B

### `q-ho-fault-restore-deleted-records`

- **Adjacent corpus material** fault-search-stale-results, fault-changes-not-saving, fault-data-reverted-after-save
- **Why none of it resolves the ticket** The corpus discusses deletions only as an indexing problem (deleted records still appearing) and overwrites only as a concurrency problem. Restoring deleted data from a backup is not described anywhere, and no retention or backup policy is stated.
- **Measured** top-1 0.662 against `fault-search-stale-results` — clears Gate B

### `q-ho-shipping-someone-elses-parcel`

- **Adjacent corpus material** shipping-left-with-neighbour, shipping-delivered-to-wrong-address, shipping-partial-delivery
- **Why none of it resolves the ticket** The corpus covers our parcel reaching the wrong person in several ways. The mirror image - a stranger's parcel reaching our customer, and what they should do with it - has no precedent, and the wrong-address resolution is about recovering our own goods.
- **Measured** top-1 0.616 against `shipping-left-with-neighbour` — clears Gate B

### `q-ho-returns-sent-wrong-item-back`

- **Adjacent corpus material** returns-received-not-processed, returns-lost-on-way-back, returns-tracking-shows-delivered-no-refund
- **Why none of it resolves the ticket** returns-received-not-processed opens almost identically and resolves an unmatched parcel by matching it to an order. Here the parcel is matched perfectly well; the goods inside are simply not ours, and nothing in the corpus returns a customer's own property to them.
- **Measured** top-1 0.654 against `returns-received-not-processed` — clears Gate B

### `q-ho-product-commercial-use-guarantee`

- **Adjacent corpus material** product-warranty-length-query, product-expected-lifespan, product-difference-between-models
- **Why none of it resolves the ticket** The warranty ticket states the term and the exclusions (wear, accidental damage, consumables) without ever conditioning cover on how the item is used. product-expected-lifespan quotes a figure for 'domestic use' and product-difference-between-models mentions continuous duty, which together look like an answer and are not one.
- **Measured** top-1 0.617 against `product-warranty-length-query` — clears Gate B

---

## PII overlay (6)

Realism only; redaction is tested elsewhere. Placed 4 ordinary / 1 near-duplicate
/ 1 hard-negative and one per category, so it is confounded with neither.

| id | scenario trait | category | details carried |
|---|---|---|---|
| `q-ho-billing-refund-to-dead-card` | ordinary | Billing | order number, card last four, email |
| `q-ho-account-typo-in-registered-email` | ordinary | Account & Login | two email addresses, one of them the typo |
| `q-ho-fault-requests-start-failing` | ordinary | Technical Fault | source IP, contact email |
| `q-ho-shipping-neighbour-despite-instruction` | near-duplicate | Shipping & Delivery | house numbers, street name, email |
| `q-ho-returns-bought-from-reseller` | hard-negative | Returns & Refunds | full name, email |
| `q-ho-product-what-is-in-it` | ordinary | Product Question | email, phone number, a health detail |

