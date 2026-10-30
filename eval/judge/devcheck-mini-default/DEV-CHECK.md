Dev check against rubric judge-v2: 19/26 matched the expected verdict.

This is a behavioural check on development data and is never a reported
number. Classify each mismatch below as rubric ambiguity (fix the wording,
log the change) or judge error (record it, change nothing). Do not iterate
the wording toward a perfect score.

MISMATCH q-login-too-many-tries-now-blocked
  expected False, judge said True
  rule     ungrounded - the causal link between wrong attempts and the lock appears in no resolution; the customer is authoritative only that they typed it wrong
  judge    (no claims returned)

MISMATCH q-login-two-registrations
  expected True, judge said False
  rule     grounded - hedged, restates the customer's account, and uses the precedent as a similar case
  judge    which can happen if one was created with a personal address and another through an organisation invitation

MISMATCH q-parcel-missing
  expected True, judge said False
  rule     grounded - hedged possibility, similar-case framing, and 'a replacement is sent' is phrased as policy
  judge    the courier may have delivered your parcel to a neighbouring property by mistake.

MISMATCH q-shipping-says-delivered-but-nothing-here
  expected True, judge said False
  rule     grounded - same shape as q-parcel-missing
  judge    the parcel is recovered | a location trace is raised

MISMATCH q-returns-money-back-short
  expected False, judge said True
  rule     ungrounded - one past-tense case stated as a general rule; the hedge attaches to the circumstances, not the claim
  judge    (no claims returned)

MISMATCH q-returns-decided-against-it
  expected False, judge said True
  rule     ungrounded - 'the item is perfectly good' bears on eligibility and needs attribution or resolution support
  judge    (no claims returned)

MISMATCH q-product-what-size-should-i-get
  expected False, judge said True
  rule     ungrounded - 'this line' and 'this design' expanded to 'our sizing' generally
  judge    (no claims returned)
