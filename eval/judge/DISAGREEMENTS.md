# The 20 cases where the labels say grounded and `gpt-5` at default effort says not
Generated from committed artifacts: `pairs-to-label.json` (labels), `verdicts-gpt5-default.json`, `verdicts-gpt5-low.json`, `verdicts-gpt5mini-default.json`.

The judge reports the shortest quote from the draft it considers unsupported. Read that against the cited sources below it and decide who is right. If the judge is catching real unsupported claims, the problem is the labels or the rubric, not the judge.

**20 cases.** The other two configurations' verdicts are shown for contrast.

---

## 1. `q-billing-auto-renewal-surprise`

**Ticket:** Money taken for another year with no heads up

**Judge flags:** `annual`

**Other configs:** gpt-5/low = False (`annual charge`); gpt-5-mini = False (`annual charge`)

**Your note:** Source 1 says reminders go out "fourteen days before the charge" and describes delivery into the customer's junk folder by their mail provider. The account-review language is an offer to help.

### Draft

> Thank you for reaching out. Our records show that renewal reminders are sent fourteen days before the annual charge. However, in some cases, these reminders may be filtered into your junk or spam folder by your mail provider. If you did not receive a reminder, please check those folders. If the reminder was missed, we can look into your account to see if the reminder was sent and discuss possible next steps. Please let us know if you would like us to review your account details further.

### Cited source 1 — No reminder before the annual charge

> Renewal reminders go out fourteen days before the charge. The message was delivered but filtered into the customer's junk folder, which their mail provider confirmed. We refunded the term, turned off auto-renew, and added a second reminder at seven days for this account.

---

## 2. `q-billing-charged-after-stopping`

**Ticket:** Stopped my plan and you took payment anyway

**Judge flags:** `It appears that your cancellation may not have been fully confirmed`

**Other configs:** gpt-5/low = False (`payments continuing`); gpt-5-mini = True

**Your note:** Source 1 supports an unconfirmed cancellation leaving the plan active. Source 2 explicitly says "treated it as cancelled from the original date, refunded the charge taken since", supporting the promise to refund payments since the intended cancellation date.

### Draft

> It appears that your cancellation may not have been fully confirmed, which can result in the plan remaining active and payments continuing. This has happened in similar cases where the cancellation was started but not completed on the final confirmation screen. We recommend checking if your cancellation was fully confirmed. If not, we can assist in properly cancelling your plan and refunding any payments taken since your intended cancellation date. Please let us know if you would like us to proceed with this for you.

### Cited source 1 — Subscription renewed after I cancelled

> The cancellation had been started but not confirmed, so the plan stayed active. We cancelled it properly and refunded the renewal in full; refunds reach the original card within five working days.

### Cited source 2 — Cancelled last month but still billed

> The cancellation was started but never confirmed on the final screen, so the plan stayed active. We treated it as cancelled from the original date, refunded the charge taken since, and closed the subscription.

---

## 3. `q-billing-more-people-more-cost`

**Ticket:** Cost rose after the team grew

**Judge flags:** `colleagues`

**Other configs:** gpt-5/low = True; gpt-5-mini = True

**Your note:** Source 1 explicitly supports per-seat billing, seats added on different days, proration from each start date, and the following invoice settling to the flat per-seat rate.

### Draft

> The increase in your invoice after adding more colleagues is due to the plan being charged per seat. Since the additional seats were added on different days, each was prorated from its own start date rather than from the start of the billing cycle. Your following invoice will settle to the flat per-seat rate for all seats. This explains the odd amount increase you noticed. If you have any further questions, please let us know.

### Cited source 1 — Invoice higher after adding team members

> The plan is charged per seat, and the three seats were added on different days, so each was prorated from its own start date rather than from the start of the cycle. The following invoice settles to the flat per-seat rate for all seats.

---

## 4. `q-billing-bank-took-extra`

**Ticket:** Amount debited does not match the listed price

**Judge flags:** `slightly higher`

**Other configs:** gpt-5/low = False (`slightly higher`); gpt-5-mini = True

**Your note:** Source 1 directly describes conversion at the bank's own rate, a foreign transaction fee, and a bank charge absent from the invoice, which shows the amount requested.

### Draft

> The amount that left your account being slightly higher than the price shown at checkout is likely due to your bank converting the charge from the store currency at its own exchange rate and possibly adding a foreign transaction fee. This difference is a charge from your bank and does not appear on our invoice, which shows the exact amount we requested.

### Cited source 1 — Charged slightly more than the price shown

> The invoice is issued in the store currency and the customer's bank converted it at its own rate, adding a foreign transaction fee. The difference is the bank's charge and does not appear on our invoice, which shows the exact amount we requested.

---

## 5. `q-login-phone-replaced-no-codes`

**Ticket:** New handset means I cannot pass the security step

**Judge flags:** `codes are tied`

**Other configs:** gpt-5/low = True; gpt-5-mini = True

**Your note:** Source 2 directly supports device-bound enrolments, the app-backup exception, recovery-detail verification, removal of the old enrolment, and enrolment on the new handset.

### Draft

> It looks like your authenticator codes are tied to your old device and do not transfer automatically to a new phone unless you used the app's own backup feature. To resolve this, we can verify your identity using your account's recovery details, after which we can remove the old authenticator enrolment and help you re-enroll on your new handset. Please provide the necessary verification details so we can assist you further.

### Cited source 1 — Lost my phone, cannot get past two-factor

> Identity was verified against the last two order numbers and the registered billing postcode, after which two-factor was reset and the customer enrolled a new device and stored recovery codes.

### Cited source 2 — Changed phone and cannot complete sign in

> Authenticator enrolments are tied to the device and do not transfer with a phone migration unless the app's own backup was used. Identity was verified against the account's recovery details, the old enrolment was removed, and the customer re-enrolled on the new handset.

---

## 6. `q-login-too-many-tries-now-blocked`

**Ticket:** Blocked after guessing my password too often

**Judge flags:** `multiple incorrect password attempts`

**Other configs:** gpt-5/low = False (`due to multiple incorrect password attempts`); gpt-5-mini = False (`due to multiple incorrect password attempts.`)

**Your note:** Source 1's subject identifies failed sign-in attempts. Source 2 explicitly supports automatic release after thirty minutes, immediate clearing after a successful reset, and support intervention for repeating locks indicating automated attempts.

### Draft

> Your account has been temporarily locked due to multiple incorrect password attempts. This lockout is automatic and will clear itself after thirty minutes, so you can try signing in again then without needing to contact support. If you reset your password successfully, the lock will clear immediately. Support intervention is only necessary if the lockout happens repeatedly, which could indicate an automated sign-in attempt. Please wait for the lock to clear and try again, or reset your password if needed.

### Cited source 1 — Account locked after failed sign in attempts

> Lockouts clear automatically after thirty minutes. We confirmed no sign-ins from unrecognised locations and advised a password change as a precaution.

### Cited source 2 — How long does the lock last

> The automatic lock releases itself after thirty minutes with no action required, and a successful reset clears it immediately. Support intervention is only needed when the lock repeats, which indicates an automated sign-in attempt against the address.

---

## 7. `q-login-two-registrations`

**Ticket:** My records appear to be in two places

**Judge flags:** `you have two accounts`

**Other configs:** gpt-5/low = False (`you have two accounts`); gpt-5-mini = True

**Your note:** Source 1 directly describes two accounts created through a personal address and an organisation invitation, linked history, and closing the unused sign-in after confirmation. The proposed help follows that documented action.

### Draft

> It sounds like you have two accounts with your history split between them, which can happen if one was created with a personal address and another through an organisation invitation. In a similar case, the records were linked so the history appears together, and the unused sign-in was closed after confirmation. If you'd like, we can help you confirm and link your accounts in the same way.

### Cited source 1 — I seem to have two accounts

> Two accounts existed, one created with a personal address and one through an organisation invitation. The records were linked so history appears together, and the unused sign-in was closed after confirmation.

---

## 8. `q-login-joined-but-empty`

**Ticket:** Took up an invite and there is nothing to see

**Judge flags:** `We recommend checking if you are signed in with the correct account.`

**Other configs:** gpt-5/low = True; gpt-5-mini = False (`We recommend checking if you are signed in with the correct account.`)

**Your note:** Source 1 directly supports invitation acceptance under a different personal account, membership attaching there, moving it to the correct account, and the workspace appearing immediately.

### Draft

> It sounds like you may have accepted the invitation while signed in under a different personal account, which means the membership is attached to that account rather than the one you intended. We recommend checking if you are signed in with the correct account. If this is the case, we can move the membership to the correct account so the shared area appears for you immediately.

### Cited source 1 — Accepted an invitation but see nothing

> The invitation was accepted while signed in under a different personal account, so the membership attached to that account rather than the intended one. The membership was moved to the correct account and the workspace appeared immediately.

---

## 9. `q-fault-callbacks-stopped`

**Ticket:** Our system is no longer being told about changes

**Judge flags:** `to prevent this from happening again`

**Other configs:** gpt-5/low = False (`to prevent this from happening again.`); gpt-5-mini = True

**Your note:** Source 1 explicitly supports the documented circuit breaker after twenty-four consecutive hours of server errors, re-enabling delivery after recovery, and replaying retained events.

### Draft

> It appears that event callbacks to your system were suspended automatically after your endpoint returned server errors for twenty-four consecutive hours, as per our documented circuit breaker. Once your endpoint recovered, delivery was re-enabled and the retained events were replayed. Please check your endpoint's availability and error responses to prevent this from happening again.

### Cited source 1 — We stopped receiving event callbacks

> Delivery was suspended automatically after the endpoint returned server errors for twenty-four consecutive hours, which is the documented circuit breaker. Once the endpoint recovered, delivery was re-enabled and the retained events were replayed.

---

## 10. `q-parcel-missing`

**Ticket:** Tracking says delivered but I have no parcel

**Judge flags:** `by mistake`

**Other configs:** gpt-5/low = True; gpt-5-mini = True

**Your note:** Both sources document delivery to a neighbouring property and recovery. Source 1 explicitly supports a location trace and a free replacement if recovery fails within three working days.

### Draft

> Thank you for reaching out. It appears that the courier may have delivered your parcel to a neighbouring property by mistake. In similar cases, a location trace is raised and the parcel is recovered. If the parcel cannot be located within three working days, a replacement is sent at no cost. Please allow us some time to investigate and recover your parcel.

### Cited source 1 — Parcel marked delivered but nothing arrived

> The courier had delivered to a neighbouring property. A location trace was raised and the parcel recovered; where recovery fails within three working days a replacement is sent at no cost.

### Cited source 2 — Tracking says delivered but nothing came

> The carrier's proof of delivery placed the scan at a neighbouring property on the same street. The parcel was recovered from there. Where a scan cannot be located within three working days the order is treated as lost and replaced.

---

## 11. `q-shipping-says-delivered-but-nothing-here`

**Ticket:** Marked as handed over but I have nothing

**Judge flags:** `by mistake`

**Other configs:** gpt-5/low = False (`the parcel is recovered`); gpt-5-mini = False (`we can initiate a location trace for you.`)

**Your note:** Both sources document a neighbouring-property delivery and recovery. Source 1 explicitly supports the trace and free-replacement policy after three working days. Checking neighbours and requesting contact are advice/offers to help.

### Draft

> We understand your concern about the package being marked as delivered but not found at your property. In similar cases, the courier sometimes delivers to a neighbouring property by mistake. When this happens, a location trace is raised and the parcel is recovered. If recovery fails within three working days, a replacement is sent at no cost. We recommend checking with your immediate neighbours to see if the parcel was left with them. If you still cannot locate the parcel, please let us know so we can initiate a location trace for you.

### Cited source 1 — Parcel marked delivered but nothing arrived

> The courier had delivered to a neighbouring property. A location trace was raised and the parcel recovered; where recovery fails within three working days a replacement is sent at no cost.

### Cited source 2 — Tracking says delivered but nothing came

> The carrier's proof of delivery placed the scan at a neighbouring property on the same street. The parcel was recovered from there. Where a scan cannot be located within three working days the order is treated as lost and replaced.

---

## 12. `q-shipping-postage-more-than-quoted`

**Ticket:** Carriage cost exceeded the published rate

**Judge flags:** `higher than the figure shown on our delivery information page`

**Other configs:** gpt-5/low = False (`delivery information page`); gpt-5-mini = True

**Your note:** Source 1 directly supports standard-parcel pricing, the oversized-item supplement, and displaying it against the item instead of only in the basket total.

### Draft

> The carriage cost you were billed for was higher than the figure shown on our delivery information page because your order included an oversized item that carries a large-item supplement. This supplement is now shown against the item rather than only appearing in the basket total, which may explain the difference in the charge you saw. We hope this clarifies the additional cost you encountered.

### Cited source 1 — Delivery charge more than advertised

> The quoted rate applies to standard parcels, and the order included an oversized item carrying a large-item supplement. The supplement is now shown against the item rather than appearing only in the basket total.

---

## 13. `q-shipping-need-different-address`

**Ticket:** Order needs to go somewhere else

**Judge flags:** `to your new address`

**Other configs:** gpt-5/low = True; gpt-5-mini = True

**Your note:** Source 1 explicitly supports the seller being unable to alter the carrier-held label, carrier redirection before the first attempt, and pre-dispatch address changes in the order screen.

### Draft

> Once a parcel is with the carrier, we cannot alter the address on the label. However, the carrier's own redirection service can divert the parcel before the first delivery attempt. If you have not already done so, you may want to contact the carrier to arrange for the parcel to be redirected to your new address. For future orders, address changes can be made in the order screen before dispatch.

### Cited source 1 — Need to change the address on a dispatched order

> Once a parcel is with the carrier the address on the label cannot be altered by us, but the carrier's own redirection service can divert it before the first delivery attempt. The customer redirected it successfully. Address changes before dispatch are made in the order screen.

---

## 14. `q-refund-missing`

**Ticket:** Approved refund still not in my account

**Judge flags:** `over a week ago`

**Other configs:** gpt-5/low = True; gpt-5-mini = False (`approved over a week ago`)

**Your note:** Sources 1 and 3 explicitly support three to five working days depending on the bank. Source 2 documents an expired-card refund held in suspense and released by the bank after receiving the acquirer reference.

### Draft

> Thank you for reaching out. Refunds typically take three to five working days to appear in your account after being processed, depending on your bank's schedule. Occasionally, it may take longer if the refund was issued to a card that has since expired, in which case the bank may hold the funds in suspense until released. If your refund was approved over a week ago and you still have not received it, please check with your bank to see if they require any additional information or if the refund is being held. If you need further assistance, please let us know. We are here to help.

### Cited source 1 — Refund confirmed but nothing in my account

> The refund was released the same day and the acquirer reference was provided. Card refunds clear on the issuing bank's own schedule, commonly three to five working days and occasionally longer across a weekend. The funds appeared on day four.

### Cited source 2 — Refund was approved but has not reached my account

> The refund had been issued to a card that had since expired, so the bank held it in suspense. The customer's bank released it once given the acquirer reference, which we supplied.

### Cited source 3 — How long does a refund take to show up

> Refunds are released when the return scans in at the warehouse and take three to five working days to appear, depending on the bank.

---

## 15. `q-returns-money-back-short`

**Ticket:** Amount returned to me is too small

**Judge flags:** `less than the original amount you paid`

**Other configs:** gpt-5/low = False (`may be less than the original amount you paid`); gpt-5-mini = True

**Your note:** Source 1 directly supports a basket-wide promotional discount, refunding the returned line's discounted share, matching what was actually paid for that item, and providing an allocation breakdown.

### Draft

> Thank you for reaching out. The amount refunded may be less than the original amount you paid if a promotional discount was applied across your whole basket. When you return one item, only that item's discounted share is refunded, which matches what you actually paid for the returned item. We can provide a breakdown showing how the discount was allocated if that would help clarify the refund amount. Please let us know if you would like this information.

### Cited source 1 — Refunded less than I paid

> A promotional discount had been applied across the whole basket, and returning one line removed only that line's discounted share. The refund matched what was actually paid for the returned item. A breakdown was supplied showing the allocation.

---

## 16. `q-returns-decided-against-it`

**Ticket:** Nothing wrong with it, I just do not want it

**Judge flags:** `you have simply changed your mind`

**Other configs:** gpt-5/low = True; gpt-5-mini = False (`the item is perfectly good`)

**Your note:** Source 1 explicitly states the thirty-day unused-and-complete return conditions and a refund of goods plus original standard postage, less the return-label cost.

### Draft

> Thank you for your message. Since you have simply changed your mind and the item is perfectly good, you can return it within thirty days of delivery provided the item is unused and complete. The refund will cover the goods and the original standard postage, less the cost of the return label. Please ensure the item meets these conditions before returning it. If you need further assistance with the return process, please let us know.

### Cited source 1 — Simply do not want it any more

> Change-of-mind returns are accepted within thirty days of delivery provided the item is unused and complete. The refund covers the goods and the original standard postage, less the cost of the return label.

---

## 17. `q-returns-how-do-i-send-back`

**Ticket:** Process for sending an item back to you

**Judge flags:** `reference number`

**Other configs:** gpt-5/low = False (`reference number`); gpt-5-mini = True

**Your note:** Every procedural step is directly stated in source 1: start from the account order, obtain a printable label and reference, use original packaging where possible, and hand in at any carrier drop-off point.

### Draft

> To return an item, please start the return process from the order in your account area. This will generate a printable label and a reference number. Pack the item in its original packaging if possible, and drop it off at any of the carrier's drop-off points. If you need further assistance, feel free to ask.

### Cited source 1 — How do I send something back

> A return is started from the order in the account area, which produces a printable label and a reference. The item should be packed in its original packaging where possible and handed in at any of the carrier's drop-off points.

---

## 18. `q-returns-bought-as-present`

**Ticket:** Returning something given to me

**Judge flags:** `you do not have any paperwork or proof of purchase`

**Other configs:** gpt-5/low = True; gpt-5-mini = False (`you do not have any paperwork or proof of purchase`)

**Your note:** Source 1 explicitly supports a gift return using the packing-note reference, identifying the order without revealing its price, and no return being possible without any reference.

### Draft

> If the item was given to you as a gift and you do not have any paperwork or proof of purchase, you can return it using the gift reference found on the packing note. This reference identifies the order without revealing the price paid. However, if you do not have any reference, the item cannot be matched and a return is not possible.

### Cited source 1 — Returning a present without the paperwork

> A gift can be returned using the gift reference on the packing note, which identifies the order without revealing what was paid. Without any reference the item cannot be matched and no return is possible.

---

## 19. `q-product-how-to-look-after-it`

**Ticket:** Keeping it in good condition

**Judge flags:** `help prevent spoilage`

**Other configs:** gpt-5/low = False (`help prevent spoilage`); gpt-5-mini = True

**Your note:** Source 3 supports the damp cloth, mild detergent, cleaner restrictions, and no immersion. Source 4 supports dry storage away from sunlight and half charge. Source 1 supports hand washing and the dishwasher/temperature warning. Source 2 supports thirty-degree fabric washing and avoiding tumble drying. The final care summary restates these damage-prevention instructions.

### Draft

> To keep your item in good condition, you should clean it with a damp cloth and mild detergent. Avoid using solvent-based or abrasive cleaners, as these can damage the surface coating irreversibly and void the guarantee. Also, do not immerse the item in water since it is only splash resistant, not waterproof. For storage, keep it dry and away from direct sunlight to prevent material degradation. If storing for a long time, leave the battery at roughly half charge to preserve its capacity. When washing, hand wash with warm water rather than using a dishwasher, as dishwasher use voids the finish guarantee and the coating degrades above 60 degrees. If the item has fabric, machine wash the outer fabric at 30 degrees on a cool cycle, but avoid tumble drying as it degrades the coating. These steps will help prevent spoilage and maintain your item in good condition.  
> 
> If you have any other questions, feel free to ask!

### Cited source 1 — How should I clean this without damaging it

> The coating degrades above sixty degrees, so hand washing with warm water is recommended and dishwasher use voids the finish guarantee. Care instructions were sent.

### Cited source 2 — Can this go in the washing machine

> The outer fabric is machine washable at thirty degrees but the trim is not colourfast above that, so a cool cycle is required. Tumble drying degrades the coating and is not recommended at any setting.

### Cited source 3 — Best way to clean this

> A damp cloth with mild detergent is sufficient. Solvent-based and abrasive cleaners damage the surface coating irreversibly and void the guarantee. It should not be immersed, as the seal is splash resistant rather than waterproof.

### Cited source 4 — How should I store it when not in use

> It should be stored dry and away from direct sunlight, which degrades the material over extended periods. For long storage the battery should be left at roughly half charge rather than full or empty, which preserves capacity.

---

## 20. `q-product-no-instructions-in-box`

**Ticket:** Nothing telling me how to assemble it

**Judge flags:** `to you`

**Other configs:** gpt-5/low = False (`to you`); gpt-5-mini = True

**Your note:** Source 1 directly states the omitted production-batch instructions, downloadable manual, posted printed copy, and confirmation against the parts list that no parts were missing.

### Draft

> We apologize for the inconvenience. It appears that printed instructions were omitted from a production batch. However, the full manual is available for download from the product page. Additionally, a printed copy has been posted to you. Please be assured that all parts are present as confirmed against the parts list.

### Cited source 1 — No instructions in the box

> Printed instructions were omitted from a production batch. The full manual is downloadable from the product page and a printed copy was posted. No parts were missing, which was confirmed against the parts list.
