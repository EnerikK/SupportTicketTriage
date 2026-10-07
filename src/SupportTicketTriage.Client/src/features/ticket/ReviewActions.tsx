import { useState } from 'react'
import type { TicketDraft, TicketReview } from '../../api/client'
import { useGenerateDraft, useSubmitReview } from '../../api/queries'

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
  if (review) {
    return <RecordedDecision review={review} />
  }

  if (!draft) {
    return draftEligible ? (
      <GenerateDraft ticketId={ticketId} />
    ) : (
      <p className="status">
        Nothing to review. This ticket was routed for manual triage, so no draft is generated.
      </p>
    )
  }

  if (!draft.citationsValid) {
    return (
      <p className="status">
        Nothing to review. The draft was withheld because its citations did not validate.
      </p>
    )
  }

  return <ReviewForm ticketId={ticketId} draft={draft} />
}

function RecordedDecision({ review }: { review: TicketReview }) {
  const approved = review.decision === 'Approved'

  return (
    <div className="panel">
      <p>
        <strong className={approved ? 'state--ready' : 'state--rejected'}>
          {review.decision}
        </strong>
        {approved && review.wasEdited && <span className="muted"> · edited before approval</span>}
      </p>

      {approved && review.finalText && <p className="draft__text">{review.finalText}</p>}
      {!approved && review.rejectionReason && (
        <p className="evidence__resolution">{review.rejectionReason}</p>
      )}

      <p className="muted">Reviews are final — a ticket is reviewed once.</p>
    </div>
  )
}

function GenerateDraft({ ticketId }: { ticketId: string }) {
  const generate = useGenerateDraft(ticketId)

  return (
    <div className="panel">
      <button type="button" onClick={() => generate.mutate()} disabled={generate.isPending}>
        {generate.isPending ? 'Generating…' : 'Generate a draft'}
      </button>
      {generate.error && (
        <p className="status status--error" role="alert">
          {generate.error.message}
        </p>
      )}
    </div>
  )
}

function ReviewForm({ ticketId, draft }: { ticketId: string; draft: TicketDraft }) {
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
    submit.mutate({
      decision: 'Approved',
      finalText: edited ? text.trim() : null,
      rejectionReason: null,
    })
  }

  function reject() {
    submit.mutate({ decision: 'Rejected', finalText: null, rejectionReason: reason.trim() })
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

      {submit.error && (
        <p className="status status--error" role="alert">
          {submit.error.message}
        </p>
      )}
    </div>
  )
}
