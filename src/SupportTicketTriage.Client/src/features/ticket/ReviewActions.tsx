import { useEffect, useRef, useState } from 'react'
import type { TicketDraft, TicketReview } from '../../api/client'
import { useGenerateDraft, useSubmitReview } from '../../api/queries'
import { Alert } from '../../ui/Alert'

interface Props {
  ticketId: string
  draft: TicketDraft | null
  review: TicketReview | null
  draftEligible: boolean | null
}

/// There is no send action here, and there is none anywhere else either: the
/// API exposes no path that delivers a reply to a customer. Approving records
/// a decision and resolves the ticket, which is what makes it available as
/// precedent for future drafts. Nothing leaves the system.
export function ReviewActions({ ticketId, draft, review, draftEligible }: Props) {
  // Whether the panel below appeared because the reviewer just submitted, as
  // opposed to this ticket already having been reviewed when the page
  // loaded. Only the first case may move focus: submitting replaces the form
  // with a result and drops focus to the document, which tells a screen
  // reader user nothing happened at all. Focusing on arrival instead would
  // yank focus out of wherever the reader was on every navigation to an
  // already-reviewed ticket, which is the louder bug of the two.
  const [justSubmitted, setJustSubmitted] = useState(false)

  const onSubmitted = () => setJustSubmitted(true)

  if (review) {
    return <RecordedDecision review={review} takeFocus={justSubmitted} />
  }

  if (!draft) {
    if (draftEligible === true) {
      return <GenerateDraft ticketId={ticketId} onSubmitted={onSubmitted} />
    }

    // Three states, not two. `null` means no routing decision exists at all,
    // and reporting that as "routed for manual triage" asserts a gate
    // decision that was never made - worse than saying nothing, and exactly
    // the kind of smoothing-over ADR-023 forbids. DraftPanel already drew
    // this distinction; this branch tested truthiness and did not.
    //
    // Only the gated ticket gets the manual form. An untriaged one is offered
    // triage elsewhere on the page, and resolving it by hand first would make
    // triage refuse it for good, leaving it unclassified.
    if (draftEligible === false) {
      return (
        <>
          <p className="status">
            Nothing to review. This ticket was routed for manual triage, so no draft is generated.
          </p>
          <ManualResolutionForm ticketId={ticketId} onSubmitted={onSubmitted} />
        </>
      )
    }

    return (
      <p className="status">
        Nothing to review. This ticket has not been triaged, so nothing has decided whether it can be
        drafted.
      </p>
    )
  }

  // The explanation stays above the form: a withheld draft is named, not
  // smoothed over, and the form is for writing past it, never for approving it.
  if (!draft.citationsValid) {
    return (
      <>
        <p className="status">
          Nothing to review. The draft was withheld because its citations did not validate.
        </p>
        <ManualResolutionForm ticketId={ticketId} onSubmitted={onSubmitted} />
      </>
    )
  }

  return <ReviewForm ticketId={ticketId} draft={draft} onSubmitted={onSubmitted} />
}

function RecordedDecision({ review, takeFocus }: { review: TicketReview; takeFocus: boolean }) {
  const manual = review.decision === 'ResolvedManually'
  // Approval and manual resolution both resolve the ticket with the
  // reviewer's final text; only a rejection resolves nothing.
  const resolved = review.decision === 'Approved' || manual
  const panel = useRef<HTMLDivElement>(null)

  useEffect(() => {
    if (takeFocus) {
      panel.current?.focus()
    }
  }, [takeFocus])

  // tabIndex -1 makes the panel focusable programmatically without putting it
  // in the tab order. Moving focus here is itself the announcement - a
  // screen reader reads the container it lands in - which is why this needs
  // no live region on top.
  return (
    <div className="panel" ref={panel} tabIndex={-1}>
      <p>
        <strong className={resolved ? 'state--ready' : 'state--rejected'}>
          {manual ? 'Resolved manually' : review.decision}
        </strong>
        {manual && <span className="muted"> · written by hand</span>}
        {review.decision === 'Approved' && review.wasEdited && (
          <span className="muted"> · edited before approval</span>
        )}
      </p>

      {resolved && review.finalText && <p className="draft__text">{review.finalText}</p>}
      {!resolved && review.rejectionReason && (
        <p className="evidence__resolution">{review.rejectionReason}</p>
      )}

      <p className="muted">Reviews are final — a ticket is reviewed once.</p>
    </div>
  )
}

function GenerateDraft({ ticketId, onSubmitted }: { ticketId: string; onSubmitted: () => void }) {
  const generate = useGenerateDraft(ticketId)

  // When generation fails - Azure unreachable, or the corpus moved on so
  // nothing is retrievable - the reviewer can still write the reply. Without
  // this, an outage recreates exactly the dead end manual resolution exists
  // to remove. Retrying stays available above it.
  return (
    <>
      <div className="panel">
        <button type="button" onClick={() => generate.mutate()} disabled={generate.isPending}>
          {generate.isPending ? 'Generating…' : 'Generate a draft'}
        </button>
        {generate.error && <Alert>{generate.error.message}</Alert>}
      </div>
      {generate.error && <ManualResolutionForm ticketId={ticketId} onSubmitted={onSubmitted} />}
    </>
  )
}

/// For a ticket with no draft a reviewer can judge. The reviewer's reply
/// resolves the ticket and joins the corpus as a precedent marked as written
/// by hand. Like every other action here, it records a decision; nothing is
/// sent to anyone.
///
/// The textarea always starts empty. When a withheld draft exists the API
/// does not serve its text, and the server would not accept it as a manual
/// resolution's text even if it did.
function ManualResolutionForm({
  ticketId,
  onSubmitted,
}: {
  ticketId: string
  onSubmitted: () => void
}) {
  const [text, setText] = useState('')
  const submit = useSubmitReview(ticketId)
  const canResolve = text.trim().length > 0

  function resolve() {
    submit.mutate(
      { decision: 'ResolvedManually', finalText: text.trim(), rejectionReason: null },
      { onSuccess: onSubmitted },
    )
  }

  return (
    <div className="panel review">
      <label className="review__label" htmlFor="manual-reply">
        Your reply
      </label>
      <p className="muted" id="manual-reply-help">
        Write the reply yourself. It resolves the ticket and becomes a precedent marked as written
        by hand.
      </p>
      <textarea
        id="manual-reply"
        className="review__text"
        rows={8}
        value={text}
        aria-describedby="manual-reply-help"
        onChange={(event) => setText(event.target.value)}
        disabled={submit.isPending}
      />

      <div className="review__actions">
        <button
          type="button"
          className="button button--approve"
          onClick={resolve}
          disabled={submit.isPending || !canResolve}
        >
          Resolve with this reply
        </button>
      </div>

      {submit.error && <Alert>{submit.error.message}</Alert>}
    </div>
  )
}

function ReviewForm({
  ticketId,
  draft,
  onSubmitted,
}: {
  ticketId: string
  draft: TicketDraft
  onSubmitted: () => void
}) {
  const original = draft.draftText ?? ''
  const [text, setText] = useState(original)
  const [reason, setReason] = useState('')
  const submit = useSubmitReview(ticketId)

  // Compared on trimmed text, the same rule the server applies when it decides
  // whether to record the review as edited.
  const edited = text.trim() !== original.trim()
  const canApprove = text.trim().length > 0
  const canReject = reason.trim().length > 0

  function approve() {
    // Approving as written sends no text at all and lets the server store the
    // draft's own. Sending it back would be the client asserting what the
    // draft said, which is the server's record to keep.
    submit.mutate(
      {
        decision: 'Approved',
        finalText: edited ? text.trim() : null,
        rejectionReason: null,
      },
      // Per-call rather than on the hook, because the hook's own onSuccess
      // is the cache invalidation and is shared by every caller. This one is
      // about what the reviewer in front of us just did.
      { onSuccess: onSubmitted },
    )
  }

  function reject() {
    submit.mutate(
      { decision: 'Rejected', finalText: null, rejectionReason: reason.trim() },
      { onSuccess: onSubmitted },
    )
  }

  return (
    <div className="panel review">
      <label className="review__label" htmlFor="reply">
        Reply {edited && <span className="muted">· edited</span>}
      </label>
      <textarea
        id="reply"
        className="review__text"
        rows={8}
        value={text}
        onChange={(event) => setText(event.target.value)}
        disabled={submit.isPending}
      />

      {edited && (
        <button type="button" className="link-button" onClick={() => setText(original)}>
          Revert to the generated draft
        </button>
      )}

      <label className="review__label" htmlFor="reason">
        Reason — required to reject
      </label>
      <textarea
        id="reason"
        className="review__reason"
        rows={2}
        value={reason}
        onChange={(event) => setReason(event.target.value)}
        disabled={submit.isPending}
      />

      <div className="review__actions">
        <button
          type="button"
          className="button button--approve"
          onClick={approve}
          disabled={submit.isPending || !canApprove}
        >
          {edited ? 'Approve edited reply' : 'Approve as written'}
        </button>
        <button
          type="button"
          className="button button--reject"
          onClick={reject}
          disabled={submit.isPending || !canReject}
        >
          Reject
        </button>
      </div>

      {submit.error && <Alert>{submit.error.message}</Alert>}
    </div>
  )
}
