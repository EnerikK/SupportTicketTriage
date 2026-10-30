Dev check against rubric judge-v2: 20/26 matched the expected verdict.

This is a behavioural check on development data and is never a reported
number. Classify each mismatch below as rubric ambiguity (fix the wording,
log the change) or judge error (record it, change nothing). Do not iterate
the wording toward a perfect score.

MISMATCH q-billing-bank-took-extra
  expected True, judge said False
  rule     grounded - hedged cause, and 'slightly higher' restates the customer's own account
  judge    a charge from your bank | does not appear on our invoice | shows the exact amount we requested

MISMATCH q-login-phone-replaced-no-codes
  expected True, judge said False
  rule     grounded - 'enrolments are tied to the device' is verbatim in the resolution and phrased generally
  judge    we can verify your identity using your account's recovery details | we can remove the old authenticator enrolment | help you re-enroll on your new handset

MISMATCH q-login-two-registrations
  expected True, judge said False
  rule     grounded - hedged, restates the customer's account, and uses the precedent as a similar case
  judge    we can help you confirm and link your accounts in the same way.

MISMATCH q-parcel-missing
  expected True, judge said False
  rule     grounded - hedged possibility, similar-case framing, and 'a replacement is sent' is phrased as policy
  judge    a location trace is raised and the parcel is recovered | recover your parcel

MISMATCH q-shipping-says-delivered-but-nothing-here
  expected True, judge said False
  rule     grounded - same shape as q-parcel-missing
  judge    the parcel is recovered | a location trace is raised

MISMATCH q-returns-decided-against-it
  expected False, judge said True
  rule     ungrounded - 'the item is perfectly good' bears on eligibility and needs attribution or resolution support
  judge    (no claims returned)
