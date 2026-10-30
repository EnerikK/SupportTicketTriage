# Where the judges and the labels disagree

Generated from committed artifacts: `pairs-to-label.json`, `verdicts-gpt5-default.json`,
`verdicts-gpt5-low.json`, `verdicts-gpt5mini-default.json`.

The categories are a reading of each case rather than something computed. Exact substring
matching found the flagged phrase in the ticket for only 2 of 20, because the drafts
paraphrase the customer rather than quoting them.

## A. Labels say grounded, `gpt-5` at default effort says not (20 cases)

**Recategorised 2026-10-30.** The first pass categorised each case by the phrase the judge
flagged, which inherits the shortest-quote blind spot: the judge returns the shortest
unsupported quote, which is often trivial while the serious problem sits elsewhere in the
draft. Each case below is now assigned its **most serious problem**, one category only.
The first pass also double-counted two cases and summed to 23.

| category | count | whose error | cases |
|---|---|---|---|
| ticket restatement | 6 | rubric | A7, A13, A14, A15, A16, A18 |
| **precedent-as-fact** | **5** | **labels** | A3, A9, A12, A19, A20 |
| judge lacked the evidence | 3 | prompt | A1, A4, A6 |
| hedged or immaterial | 3 | rubric | A2, A10, A11 |
| judge error | 2 | judge | A5, A17 |
| not an assertion | 1 | judge | A8 |

**12 of 20 are rubric or prompt defects** - six ticket restatements, three where the judge
was never shown the evidence the claim rests on, and three where the draft hedged or the
claim was immaterial.

**Three are judge-side**: A5 flags `codes are tied`, which is verbatim in the cited
resolution; A17 flags `reference number` where the source says "a reference"; A8 flags a
recommendation to check something, which asserts nothing.

**Five are label errors.** A3, A9, A12, A19 and A20 assert a precedent's circumstances or
actions as this customer's facts - "the additional seats were added on different days",
"a printed copy has been posted to you". The judge's verdict was right in each; only its
quote was trivial. These were labelled grounded and should not have been.

### Two kinds of flip, kept apart

When `judge-v2` reverses a v1 verdict, the reason matters and the two reasons are not
the same kind of thing.

**Label errors under any reading** - wrong under `judge-v1` as much as `judge-v2`:

| case | what the draft did |
|---|---|
| A3 | "the additional seats were added on different days" - the precedent's circumstance as this customer's fact |
| A9 | "your endpoint returned server errors for twenty-four consecutive hours", "the retained events were replayed" |
| A12 | "your order included an oversized item" |
| A20 | "a printed copy has been posted to you" |
| B1 | "in such cases, the discount is applied retrospectively" from a single past-tense resolution |

Five cases. The judge's verdict was right in each; only its quote was trivial, which is
why the first categorisation pass filed four of them under the wrong heading.

**Rule changes introduced by `judge-v2`** - the v1 labels were defensible under the v1
rubric, which said nothing about any of this:

| case | rule that moved it |
|---|---|
| A2 | offer versus promise: "we can assist in... refunding any payments taken since your intended cancellation date" |
| A6 | evidentiary status: the causal link between wrong attempts and the lock is in no resolution |
| A8 | offer versus promise: promises a fix and its timing |
| A15 | generality test: a conditional asserts its consequent as a general rule |
| A16 | attribution: "the item is perfectly good" bears on eligibility |
| B6 | materiality: "caused confusion" asserts no cause of the reported problem |

Six cases. **These are not labelling mistakes.** Treating them as such would make the v1
labeller look careless for applying the rubric they were given, and would hide that the
rubric was the thing that changed.

### The evidence gap behind A1, A4 and A6

The generation prompt gives the writer each source's **ticket body**; the judge prompt gave
it only the **resolution**. The judge was scoring against strictly less evidence than the
writer had, so any claim resting on a source's body or subject was unsupported by
construction.

- A1 flags `annual`; the source's subject is "No reminder before the **annual** charge".
- A4 flags `slightly higher`; the source's subject is "Charged **slightly** more than the price shown".
- A6 flags `multiple incorrect password attempts`; the source's body is "I typed my password wrong a few times".

All three were correctly labelled grounded, and the judge could not have agreed.

### A19 is recorded as possibly conflated, not confirmed

The draft merges care advice from four cited sources - dishwasher temperature, machine
washing at thirty degrees, a damp cloth, and battery storage. These read as different
products, but the dataset carries **no product identifier of any kind** (no SKU, model
number or item code), so the data cannot establish it. Recorded as possibly conflated.


---

### A1. `q-billing-auto-renewal-surprise` - ticket restatement

the draft calls the term "annual"; the ticket says "next twelve months" and no source states a term length

**Ticket:** A large payment left my account for the next twelve months and nothing warned me it was due.

**Judge flags:** `annual`

**Other configs:** gpt-5/low = False; gpt-5-mini = False

**Label note:** Source 1 says reminders go out "fourteen days before the charge" and describes delivery into the customer's junk folder by their mail provider. The account-review language is an offer to help.

**Draft:**

> Thank you for reaching out. Our records show that renewal reminders are sent fourteen days before the annual charge. However, in some cases, these reminders may be filtered into your junk or spam folder by your mail provider. If you did not receive a reminder, please check those folders. If the reminder was missed, we can look into your account to see if the reminder was sent and discuss possible next steps. Please let us know if you would like us to review your account details further.

**Cited source 1 - No reminder before the annual charge:**

> Renewal reminders go out fourteen days before the charge. The message was delivered but filtered into the customer's junk folder, which their mail provider confirmed. We refunded the term, turned off auto-renew, and added a second reminder at seven days for this account.

---

### A2. `q-billing-charged-after-stopping` - genuine inference

applies the precedent root cause to this customer, hedged with "may not have been"

**Ticket:** I ended my membership a while back and you have taken money from me again since.

**Judge flags:** `It appears that your cancellation may not have been fully confirmed`

**Other configs:** gpt-5/low = False; gpt-5-mini = True

**Label note:** Source 1 supports an unconfirmed cancellation leaving the plan active. Source 2 explicitly says "treated it as cancelled from the original date, refunded the charge taken since", supporting the promise to refund payments since the intended cancellation date.

**Draft:**

> It appears that your cancellation may not have been fully confirmed, which can result in the plan remaining active and payments continuing. This has happened in similar cases where the cancellation was started but not completed on the final confirmation screen. We recommend checking if your cancellation was fully confirmed. If not, we can assist in properly cancelling your plan and refunding any payments taken since your intended cancellation date. Please let us know if you would like us to proceed with this for you.

**Cited source 1 - Subscription renewed after I cancelled:**

> The cancellation had been started but not confirmed, so the plan stayed active. We cancelled it properly and refunded the renewal in full; refunds reach the original card within five working days.

**Cited source 2 - Cancelled last month but still billed:**

> The cancellation was started but never confirmed on the final screen, so the plan stayed active. We treated it as cancelled from the original date, refunded the charge taken since, and closed the subscription.

---

### A3. `q-billing-more-people-more-cost` - ticket restatement

"colleagues" is the ticket own word; the sources say "seats"

**Ticket:** We brought a few more colleagues onto the account and what we owe has climbed by an odd amount.

**Judge flags:** `colleagues`

**Other configs:** gpt-5/low = True; gpt-5-mini = True

**Label note:** Source 1 explicitly supports per-seat billing, seats added on different days, proration from each start date, and the following invoice settling to the flat per-seat rate.

**Draft:**

> The increase in your invoice after adding more colleagues is due to the plan being charged per seat. Since the additional seats were added on different days, each was prorated from its own start date rather than from the start of the billing cycle. Your following invoice will settle to the flat per-seat rate for all seats. This explains the odd amount increase you noticed. If you have any further questions, please let us know.

**Cited source 1 - Invoice higher after adding team members:**

> The plan is charged per seat, and the three seats were added on different days, so each was prorated from its own start date rather than from the start of the cycle. The following invoice settles to the flat per-seat rate for all seats.

---

### A4. `q-billing-bank-took-extra` - ticket restatement

"slightly higher" restates "a few pence above the figure advertised"

**Ticket:** What actually left my account is a few pence above the figure advertised at checkout.

**Judge flags:** `slightly higher`

**Other configs:** gpt-5/low = False; gpt-5-mini = True

**Label note:** Source 1 directly describes conversion at the bank's own rate, a foreign transaction fee, and a bank charge absent from the invoice, which shows the amount requested.

**Draft:**

> The amount that left your account being slightly higher than the price shown at checkout is likely due to your bank converting the charge from the store currency at its own exchange rate and possibly adding a foreign transaction fee. This difference is a charge from your bank and does not appear on our invoice, which shows the exact amount we requested.

**Cited source 1 - Charged slightly more than the price shown:**

> The invoice is issued in the store currency and the customer's bank converted it at its own rate, adding a foreign transaction fee. The difference is the bank's charge and does not appear on our invoice, which shows the exact amount we requested.

---

### A5. `q-login-phone-replaced-no-codes` - genuine inference

explains a mechanism - that codes are tied to a device - which no source states

**Ticket:** I replaced my mobile and the security codes I need are no longer available to me.

**Judge flags:** `codes are tied`

**Other configs:** gpt-5/low = True; gpt-5-mini = True

**Label note:** Source 2 directly supports device-bound enrolments, the app-backup exception, recovery-detail verification, removal of the old enrolment, and enrolment on the new handset.

**Draft:**

> It looks like your authenticator codes are tied to your old device and do not transfer automatically to a new phone unless you used the app's own backup feature. To resolve this, we can verify your identity using your account's recovery details, after which we can remove the old authenticator enrolment and help you re-enroll on your new handset. Please provide the necessary verification details so we can assist you further.

**Cited source 1 - Lost my phone, cannot get past two-factor:**

> Identity was verified against the last two order numbers and the registered billing postcode, after which two-factor was reset and the customer enrolled a new device and stored recovery codes.

**Cited source 2 - Changed phone and cannot complete sign in:**

> Authenticator enrolments are tied to the device and do not transfer with a phone migration unless the app's own backup was used. Identity was verified against the account's recovery details, the old enrolment was removed, and the customer re-enrolled on the new handset.

---

### A6. `q-login-too-many-tries-now-blocked` - ticket restatement

restates "I tried my password several times, got it wrong"

**Ticket:** I tried my password several times, got it wrong, and now the system will not let me try again.

**Judge flags:** `multiple incorrect password attempts`

**Other configs:** gpt-5/low = False; gpt-5-mini = False

**Label note:** Source 1's subject identifies failed sign-in attempts. Source 2 explicitly supports automatic release after thirty minutes, immediate clearing after a successful reset, and support intervention for repeating locks indicating automated attempts.

**Draft:**

> Your account has been temporarily locked due to multiple incorrect password attempts. This lockout is automatic and will clear itself after thirty minutes, so you can try signing in again then without needing to contact support. If you reset your password successfully, the lock will clear immediately. Support intervention is only necessary if the lockout happens repeatedly, which could indicate an automated sign-in attempt. Please wait for the lock to clear and try again, or reset your password if needed.

**Cited source 1 - Account locked after failed sign in attempts:**

> Lockouts clear automatically after thirty minutes. We confirmed no sign-ins from unrecognised locations and advised a password change as a precaution.

**Cited source 2 - How long does the lock last:**

> The automatic lock releases itself after thirty minutes with no action required, and a successful reset clears it immediately. Support intervention is only needed when the lock repeats, which indicates an automated sign-in attempt against the address.

---

### A7. `q-login-two-registrations` - ticket restatement

the ticket itself describes history split across two registrations

**Ticket:** Part of my history sits under one registration and the rest under another, and I only ever meant to have one.

**Judge flags:** `you have two accounts`

**Other configs:** gpt-5/low = False; gpt-5-mini = True

**Label note:** Source 1 directly describes two accounts created through a personal address and an organisation invitation, linked history, and closing the unused sign-in after confirmation. The proposed help follows that documented action.

**Draft:**

> It sounds like you have two accounts with your history split between them, which can happen if one was created with a personal address and another through an organisation invitation. In a similar case, the records were linked so the history appears together, and the unused sign-in was closed after confirmation. If you'd like, we can help you confirm and link your accounts in the same way.

**Cited source 1 - I seem to have two accounts:**

> Two accounts existed, one created with a personal address and one through an organisation invitation. The records were linked so history appears together, and the unused sign-in was closed after confirmation.

---

### A8. `q-login-joined-but-empty` - other

a recommendation to check something, not a factual assertion about the customer

**Ticket:** A colleague asked me to join their shared area, I agreed, and my account looks exactly as it did before.

**Judge flags:** `We recommend checking if you are signed in with the correct account.`

**Other configs:** gpt-5/low = True; gpt-5-mini = False

**Label note:** Source 1 directly supports invitation acceptance under a different personal account, membership attaching there, moving it to the correct account, and the workspace appearing immediately.

**Draft:**

> It sounds like you may have accepted the invitation while signed in under a different personal account, which means the membership is attached to that account rather than the one you intended. We recommend checking if you are signed in with the correct account. If this is the case, we can move the membership to the correct account so the shared area appears for you immediately.

**Cited source 1 - Accepted an invitation but see nothing:**

> The invitation was accepted while signed in under a different personal account, so the membership attached to that account rather than the intended one. The membership was moved to the correct account and the workspace appeared immediately.

---

### A9. `q-fault-callbacks-stopped` - outcome claim

"to prevent this from happening again" claims a preventive effect the source does not

**Ticket:** We rely on being notified automatically when something alters on your side and that has gone quiet.

**Judge flags:** `to prevent this from happening again`

**Other configs:** gpt-5/low = False; gpt-5-mini = True

**Label note:** Source 1 explicitly supports the documented circuit breaker after twenty-four consecutive hours of server errors, re-enabling delivery after recovery, and replaying retained events.

**Draft:**

> It appears that event callbacks to your system were suspended automatically after your endpoint returned server errors for twenty-four consecutive hours, as per our documented circuit breaker. Once your endpoint recovered, delivery was re-enabled and the retained events were replayed. Please check your endpoint's availability and error responses to prevent this from happening again.

**Cited source 1 - We stopped receiving event callbacks:**

> Delivery was suspended automatically after the endpoint returned server errors for twenty-four consecutive hours, which is the documented circuit breaker. Once the endpoint recovered, delivery was re-enabled and the retained events were replayed.

---

### A10. `q-parcel-missing` - genuine inference

"by mistake" adds intent the source does not state

**Ticket:** The tracking marked my parcel delivered yesterday but nothing was left and there is no card.

**Judge flags:** `by mistake`

**Other configs:** gpt-5/low = True; gpt-5-mini = True

**Label note:** Both sources document delivery to a neighbouring property and recovery. Source 1 explicitly supports a location trace and a free replacement if recovery fails within three working days.

**Draft:**

> Thank you for reaching out. It appears that the courier may have delivered your parcel to a neighbouring property by mistake. In similar cases, a location trace is raised and the parcel is recovered. If the parcel cannot be located within three working days, a replacement is sent at no cost. Please allow us some time to investigate and recover your parcel.

**Cited source 1 - Parcel marked delivered but nothing arrived:**

> The courier had delivered to a neighbouring property. A location trace was raised and the parcel recovered; where recovery fails within three working days a replacement is sent at no cost.

**Cited source 2 - Tracking says delivered but nothing came:**

> The carrier's proof of delivery placed the scan at a neighbouring property on the same street. The parcel was recovered from there. Where a scan cannot be located within three working days the order is treated as lost and replaced.

---

### A11. `q-shipping-says-delivered-but-nothing-here` - genuine inference

"by mistake" adds intent the source does not state

**Ticket:** Your records claim the package reached me and there is no sign of it anywhere at the property.

**Judge flags:** `by mistake`

**Other configs:** gpt-5/low = False; gpt-5-mini = False

**Label note:** Both sources document a neighbouring-property delivery and recovery. Source 1 explicitly supports the trace and free-replacement policy after three working days. Checking neighbours and requesting contact are advice/offers to help.

**Draft:**

> We understand your concern about the package being marked as delivered but not found at your property. In similar cases, the courier sometimes delivers to a neighbouring property by mistake. When this happens, a location trace is raised and the parcel is recovered. If recovery fails within three working days, a replacement is sent at no cost. We recommend checking with your immediate neighbours to see if the parcel was left with them. If you still cannot locate the parcel, please let us know so we can initiate a location trace for you.

**Cited source 1 - Parcel marked delivered but nothing arrived:**

> The courier had delivered to a neighbouring property. A location trace was raised and the parcel recovered; where recovery fails within three working days a replacement is sent at no cost.

**Cited source 2 - Tracking says delivered but nothing came:**

> The carrier's proof of delivery placed the scan at a neighbouring property on the same street. The parcel was recovered from there. Where a scan cannot be located within three working days the order is treated as lost and replaced.

---

### A12. `q-shipping-postage-more-than-quoted` - ticket restatement

restates the ticket almost verbatim

**Ticket:** What I was actually billed for getting the goods to me was above the figure on your information page.

**Judge flags:** `higher than the figure shown on our delivery information page`

**Other configs:** gpt-5/low = False; gpt-5-mini = True

**Label note:** Source 1 directly supports standard-parcel pricing, the oversized-item supplement, and displaying it against the item instead of only in the basket total.

**Draft:**

> The carriage cost you were billed for was higher than the figure shown on our delivery information page because your order included an oversized item that carries a large-item supplement. This supplement is now shown against the item rather than only appearing in the basket total, which may explain the difference in the charge you saw. We hope this clarifies the additional cost you encountered.

**Cited source 1 - Delivery charge more than advertised:**

> The quoted rate applies to standard parcels, and the order included an oversized item carrying a large-item supplement. The supplement is now shown against the item rather than appearing only in the basket total.

---

### A13. `q-shipping-need-different-address` - ticket restatement

"your new address" restates "I have relocated since ordering"

**Ticket:** I have relocated since ordering and the goods are heading to where I used to live.

**Judge flags:** `to your new address`

**Other configs:** gpt-5/low = True; gpt-5-mini = True

**Label note:** Source 1 explicitly supports the seller being unable to alter the carrier-held label, carrier redirection before the first attempt, and pre-dispatch address changes in the order screen.

**Draft:**

> Once a parcel is with the carrier, we cannot alter the address on the label. However, the carrier's own redirection service can divert the parcel before the first delivery attempt. If you have not already done so, you may want to contact the carrier to arrange for the parcel to be redirected to your new address. For future orders, address changes can be made in the order screen before dispatch.

**Cited source 1 - Need to change the address on a dispatched order:**

> Once a parcel is with the carrier the address on the label cannot be altered by us, but the carrier's own redirection service can divert it before the first delivery attempt. The customer redirected it successfully. Address changes before dispatch are made in the order screen.

---

### A14. `q-refund-missing` - ticket restatement

"over a week ago" is the ticket own phrase

**Ticket:** My refund was approved over a week ago and the money has still not reached my account.

**Judge flags:** `over a week ago`

**Other configs:** gpt-5/low = True; gpt-5-mini = False

**Label note:** Sources 1 and 3 explicitly support three to five working days depending on the bank. Source 2 documents an expired-card refund held in suspense and released by the bank after receiving the acquirer reference.

**Draft:**

> Thank you for reaching out. Refunds typically take three to five working days to appear in your account after being processed, depending on your bank's schedule. Occasionally, it may take longer if the refund was issued to a card that has since expired, in which case the bank may hold the funds in suspense until released. If your refund was approved over a week ago and you still have not received it, please check with your bank to see if they require any additional information or if the refund is being held. If you need further assistance, please let us know. We are here to help.

**Cited source 1 - Refund confirmed but nothing in my account:**

> The refund was released the same day and the acquirer reference was provided. Card refunds clear on the issuing bank's own schedule, commonly three to five working days and occasionally longer across a weekend. The funds appeared on day four.

**Cited source 2 - Refund was approved but has not reached my account:**

> The refund had been issued to a card that had since expired, so the bank held it in suspense. The customer's bank released it once given the acquirer reference, which we supplied.

**Cited source 3 - How long does a refund take to show up:**

> Refunds are released when the return scans in at the warehouse and take three to five working days to appear, depending on the bank.

---

### A15. `q-returns-money-back-short` - ticket restatement

restates "falls short of what I originally handed over"

**Ticket:** What came back to my account falls short of what I originally handed over for the item.

**Judge flags:** `less than the original amount you paid`

**Other configs:** gpt-5/low = False; gpt-5-mini = True

**Label note:** Source 1 directly supports a basket-wide promotional discount, refunding the returned line's discounted share, matching what was actually paid for that item, and providing an allocation breakdown.

**Draft:**

> Thank you for reaching out. The amount refunded may be less than the original amount you paid if a promotional discount was applied across your whole basket. When you return one item, only that item's discounted share is refunded, which matches what you actually paid for the returned item. We can provide a breakdown showing how the discount was allocated if that would help clarify the refund amount. Please let us know if you would like this information.

**Cited source 1 - Refunded less than I paid:**

> A promotional discount had been applied across the whole basket, and returning one line removed only that line's discounted share. The refund matched what was actually paid for the returned item. A breakdown was supplied showing the allocation.

---

### A16. `q-returns-decided-against-it` - ticket restatement

restates "I have simply thought better of the purchase"

**Ticket:** The item is perfectly good and I have simply thought better of the purchase.

**Judge flags:** `you have simply changed your mind`

**Other configs:** gpt-5/low = True; gpt-5-mini = False

**Label note:** Source 1 explicitly states the thirty-day unused-and-complete return conditions and a refund of goods plus original standard postage, less the return-label cost.

**Draft:**

> Thank you for your message. Since you have simply changed your mind and the item is perfectly good, you can return it within thirty days of delivery provided the item is unused and complete. The refund will cover the goods and the original standard postage, less the cost of the return label. Please ensure the item meets these conditions before returning it. If you need further assistance with the return process, please let us know.

**Cited source 1 - Simply do not want it any more:**

> Change-of-mind returns are accepted within thirty days of delivery provided the item is unused and complete. The refund covers the goods and the original standard postage, less the cost of the return label.

---

### A17. `q-returns-how-do-i-send-back` - other

the source does say the process "produces a printable label and a reference"; the flag looks over-strict on supported content

**Ticket:** I have something I no longer want and cannot work out the steps for returning it.

**Judge flags:** `reference number`

**Other configs:** gpt-5/low = False; gpt-5-mini = True

**Label note:** Every procedural step is directly stated in source 1: start from the account order, obtain a printable label and reference, use original packaging where possible, and hand in at any carrier drop-off point.

**Draft:**

> To return an item, please start the return process from the order in your account area. This will generate a printable label and a reference number. Pack the item in its original packaging if possible, and drop it off at any of the carrier's drop-off points. If you need further assistance, feel free to ask.

**Cited source 1 - How do I send something back:**

> A return is started from the order in the account area, which produces a printable label and a reference. The item should be packed in its original packaging where possible and handed in at any of the carrier's drop-off points.

---

### A18. `q-returns-bought-as-present` - ticket restatement

restates "I have no paperwork showing the purchase"

**Ticket:** Somebody bought this for me and I have no paperwork showing the purchase.

**Judge flags:** `you do not have any paperwork or proof of purchase`

**Other configs:** gpt-5/low = True; gpt-5-mini = False

**Label note:** Source 1 explicitly supports a gift return using the packing-note reference, identifying the order without revealing its price, and no return being possible without any reference.

**Draft:**

> If the item was given to you as a gift and you do not have any paperwork or proof of purchase, you can return it using the gift reference found on the packing note. This reference identifies the order without revealing the price paid. However, if you do not have any reference, the item cannot be matched and a return is not possible.

**Cited source 1 - Returning a present without the paperwork:**

> A gift can be returned using the gift reference on the packing note, which identifies the order without revealing what was paid. Without any reference the item cannot be matched and no return is possible.

---

### A19. `q-product-how-to-look-after-it` - outcome claim

"help prevent spoilage" claims an effect the care instructions do not promise

**Ticket:** What should I use on this and what should I avoid so it does not get spoiled.

**Judge flags:** `help prevent spoilage`

**Other configs:** gpt-5/low = False; gpt-5-mini = True

**Label note:** Source 3 supports the damp cloth, mild detergent, cleaner restrictions, and no immersion. Source 4 supports dry storage away from sunlight and half charge. Source 1 supports hand washing and the dishwasher/temperature warning. Source 2 supports thirty-degree fabric washing and avoiding tumble drying. The final care summary restates these damage-prevention instructions.

**Draft:**

> To keep your item in good condition, you should clean it with a damp cloth and mild detergent. Avoid using solvent-based or abrasive cleaners, as these can damage the surface coating irreversibly and void the guarantee. Also, do not immerse the item in water since it is only splash resistant, not waterproof. For storage, keep it dry and away from direct sunlight to prevent material degradation. If storing for a long time, leave the battery at roughly half charge to preserve its capacity. When washing, hand wash with warm water rather than using a dishwasher, as dishwasher use voids the finish guarantee and the coating degrades above 60 degrees. If the item has fabric, machine wash the outer fabric at 30 degrees on a cool cycle, but avoid tumble drying as it degrades the coating. These steps will help prevent spoilage and maintain your item in good condition.  
> 
> If you have any other questions, feel free to ask!

**Cited source 1 - How should I clean this without damaging it:**

> The coating degrades above sixty degrees, so hand washing with warm water is recommended and dishwasher use voids the finish guarantee. Care instructions were sent.

**Cited source 2 - Can this go in the washing machine:**

> The outer fabric is machine washable at thirty degrees but the trim is not colourfast above that, so a cool cycle is required. Tumble drying degrades the coating and is not recommended at any setting.

**Cited source 3 - Best way to clean this:**

> A damp cloth with mild detergent is sufficient. Solvent-based and abrasive cleaners damage the surface coating irreversibly and void the guarantee. It should not be immersed, as the seal is splash resistant rather than waterproof.

**Cited source 4 - How should I store it when not in use:**

> It should be stored dry and away from direct sunlight, which degrades the material over extended periods. For long storage the battery should be left at roughly half charge rather than full or empty, which preserves capacity.

---

### A20. `q-product-no-instructions-in-box` - genuine inference

"posted to you" transfers an action taken for the precedent customer onto this one

**Ticket:** All the components seem present but there is no booklet or leaflet explaining the steps.

**Judge flags:** `to you`

**Other configs:** gpt-5/low = False; gpt-5-mini = True

**Label note:** Source 1 directly states the omitted production-batch instructions, downloadable manual, posted printed copy, and confirmation against the parts list that no parts were missing.

**Draft:**

> We apologize for the inconvenience. It appears that printed instructions were omitted from a production batch. However, the full manual is available for download from the product page. Additionally, a printed copy has been posted to you. Please be assured that all parts are present as confirmed against the parts list.

**Cited source 1 - No instructions in the box:**

> Printed instructions were omitted from a production batch. The full manual is downloadable from the product page and a printed copy was posted. No parts were missing, which was confirmed against the parts list.


---

# B. Labels say ungrounded, `gpt-5-mini` says grounded (7 cases)

The misses that would flatter the metric, in the configuration the decision rule selects.

| category | count | share |
|---|---|---|
| over-generalisation | 6 | 86% |
| added causal claim | 1 | 14% |

**Six of seven are over-generalisation** - a single documented case turned into a general
practice, a prevalence claim, a guarantee, or a forecast about this customer. This is the
inverse failure to section A: where `gpt-5` at default effort is noisy about restatement,
`gpt-5-mini` is lenient about scope expansion.

**`gpt-5` at default effort caught 6 of these 7 disagreements, and 5 of the 6
over-generalisations.** An earlier draft of this file said "6 of 7", which conflated the
two: `q-billing-promo-ignored` is an over-generalisation that gpt-5 also called grounded,
and in `q-shipping-only-some-items` its flagged phrase points at the added causal claim
rather than at any scope expansion. The finding stands - gpt-5-mini caught none of the six -
but the figure is 5 of 6.

The two configurations fail in opposite
directions, and a single agreement number hides it: the configuration that scores worse
overall is better at catching the failure groundedness exists to catch.

---

### B1. `q-billing-promo-ignored` - over-generalisation

one retrospective discount becomes "in such cases, the discount is applied retrospectively"

**gpt-5 default said:** True

**Label note:** Source 1 says a discount "was applied retrospectively" and the difference refunded in one case. It does not state the general practice promised by "In such cases, the discount is applied retrospectively".

**Draft:**

> Thank you for reaching out. It sounds like the promotional code you entered did not reduce the amount you paid. This can happen if the code was entered after the payment step was already confirmed, meaning it did not reach the order in time. In such cases, the discount is applied retrospectively and the difference is refunded to the original payment method. Please check if this might be the case for your order. If you need further assistance, feel free to contact us again.

**Cited source 1 - Discount code did not come off the total:**

> The code was entered after the payment step had already been confirmed, so it did not reach the order. The discount was applied retrospectively and the difference refunded to the original payment method.

---

### B2. `q-login-reset-mail-never-came` - over-generalisation

"common reasons" adds prevalence; "ensure future messages arrive normally" adds a guarantee

**gpt-5 default said:** False (flagging `ensure future messages arrive normally`)

**Label note:** The sources document the two possible causes but do not establish their frequency as "common reasons". Source 2 reports the next reset arriving normally, rather than guaranteeing that allowlisting will "ensure future messages arrive normally".

**Draft:**

> Thank you for reaching out. There are a few common reasons why password reset emails might not arrive. Sometimes the messages are accepted by your mail server but end up in your junk or spam folder due to content filtering. Please check those folders and if you find the reset email there, you can allowlist the sender to ensure future messages arrive normally. Another possibility is that the email address stored on your account has a formatting issue, such as a trailing space, which can cause the reset emails to be rejected by your mail provider. If you suspect this might be the case, please let us know so we can verify and correct your stored email address. If neither of these situations applies, please provide more details so we can assist you further.

**Cited source 1 - Password reset email never arrives:**

> Reset mail was being rejected by the recipient's mail provider because the account address had a trailing space stored against it. We corrected the stored address and the reset arrived immediately.

**Cited source 2 - Password email never arrives:**

> All four messages were accepted by the recipient mail server and filed in the junk folder by a content rule. Once the sender was allowlisted the next reset arrived normally. No address change was needed.

---

### B3. `q-login-keeps-logging-me-out` - over-generalisation

"one common cause" adds a prevalence claim neither source makes

**gpt-5 default said:** False (flagging `common`)

**Label note:** The mechanisms and remedies are supported by sources 1 and 2. Calling site-data clearing "One common cause" adds a prevalence claim that neither source makes.

**Draft:**

> It sounds like your session is not being retained, causing you to sign in repeatedly. One common cause is if your browser is set to clear site data on close or frequently, which removes the session token needed to keep you signed in. To resolve this, please check your browser settings and exclude our site from any data clearing rules. This should allow your session to persist for the normal duration.
> 
> If you are using any privacy extensions or settings that block cookies, please ensure that cookies are allowed for our site, as blocking them can prevent sessions from being established.
> 
> If you continue to experience issues, please let us know more about your device and browser setup so we can assist further.

**Cited source 1 - Signed out again after a few minutes:**

> The browser was configured to clear site data on close and was being closed frequently, so the session token never survived. Excluding the site from that setting kept sessions for their normal fourteen days.

**Cited source 2 - Sign in form just reloads:**

> Third-party and first-party cookies were both blocked by a privacy extension, so the session could not be established and the form simply re-rendered. Allowing cookies for the site resolved it.

---

### B4. `q-fault-nothing-saves` - over-generalisation

a single fix becomes a general promise that "you will be notified if a save does not succeed"

**gpt-5 default said:** False (flagging `you will be notified`)

**Label note:** Source 1 says save failures "now block the confirmation message". That does not state that a failure notification is displayed, as promised by "you will be notified if a save does not succeed".

**Draft:**

> It sounds like your updates are not being saved because the save request might be rejected by a validation rule on a field that is not visible on your screen. Previously, this failure was not shown to users, so it appeared as if the save went through when it actually did not. This issue has been addressed by correcting the validation rule and ensuring that save failures now block the confirmation message, so you will be notified if a save does not succeed. Please check if any hidden fields might be causing validation errors during your update attempts.

**Cited source 1 - Edits disappear when I come back:**

> The save request was rejected by a validation rule on a field not visible on screen, and the failure was swallowed rather than surfaced. The rule was corrected and save failures now block the confirmation message.

---

### B5. `q-shipping-tracking-frozen` - over-generalisation

one parcel delivered on schedule becomes a forecast about this one

**gpt-5 default said:** False (flagging `the parcel is likely still on schedule for delivery`)

**Label note:** Source 1 supports movement without scans and normal gaps up to seventy-two hours on that route. Its report that one parcel "was delivered on schedule" does not support the current-parcel forecast "likely still on schedule for delivery".

**Draft:**

> Thank you for reaching out. If your tracking has not updated for several days, it may be because the parcel is in transit between depots on a route that does not produce intermediate scans. Movement can continue without appearing on tracking, and scan gaps of up to seventy-two hours on that route are normal. Rest assured, the parcel is likely still on schedule for delivery.

**Cited source 1 - Tracking has not moved for days:**

> The parcel was in transit between depots on a leg that produces no intermediate scans, so movement continued without appearing on tracking. It was delivered on schedule. Scan gaps of up to seventy-two hours on that route are normal.

---

### B6. `q-shipping-only-some-items` - added causal claim

"the lack of tracking information caused confusion" adds an unstated customer reaction

**gpt-5 default said:** False (flagging `caused confusion`)

**Label note:** Source 1 supports two warehouses, a second parcel a day later, missing tracking notification, and both parcels arriving complete. "The lack of tracking information caused confusion" adds an unstated customer reaction and causal claim.

**Draft:**

> It sounds like your order may have been shipped from multiple warehouses, which can result in separate parcels arriving at different times. In a similar case, the missing items were sent in a second parcel dispatched a day later with its own tracking number, which was not initially sent to the customer. Both parcels did arrive complete, but the lack of tracking information caused confusion. Please check if you have received multiple parcels or tracking details for your order. If you need further assistance, let us know.

**Cited source 1 - Only part of my order turned up:**

> The order shipped from two warehouses and the second parcel was despatched a day later with its own tracking, which had not been sent to the customer. The missing tracking notification was the fault; both parcels arrived complete.

---

### B7. `q-product-what-size-should-i-get` - over-generalisation

"this line" and "this design" become "our sizing" generally

**gpt-5 default said:** False (flagging `Our sizing tends to run smaller`)

**Label note:** Source 1 limits its sizing claim to "This line" and source 2 to "this design". "Our sizing tends to run smaller" expands that product-specific statement to the company's sizing generally.

**Draft:**

> Our sizing tends to run smaller than equivalent sizes elsewhere, so if you usually take a certain size, we recommend sizing up for a regular fit. If your measurements fall between two sizes, choose the smaller size for a close fit or the larger size if you plan to wear it over other layers, as the fabric has limited stretch. If the size you choose doesn't work out, exchanges can be made without charge. Please let us know if you need help with an exchange or have any other questions.

**Cited source 1 - Should I order a bigger size:**

> This line is cut close and most customers find it a size smaller than the equivalent elsewhere, so sizing up is advised for a regular fit. The guidance is drawn from return data rather than from the manufacturer's own chart.

**Cited source 2 - I fall between two sizes:**

> For this design the smaller size is recommended where a close fit is wanted and the larger where it will be worn over other layers, as the fabric has limited give. Either can be exchanged without charge if the choice proves wrong.
