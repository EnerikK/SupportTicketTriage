using System.Diagnostics;
using Microsoft.Extensions.AI;

namespace SupportTicketTriage.Eval.Instrumentation;

/// Times every chat call and records what the provider said it cost.
///
/// **Why a decorator rather than changing the services.** Both chat-calling
/// services read only `response.Text` and then let the `ChatResponse` go out of
/// scope, and both return `bool` or a domain record, so a caller cannot learn
/// what a call cost. The alternatives were to widen those return types - which
/// ripples into the API endpoints for a number only the harness wants - or to
/// pass a collector into the services, which puts evaluation concerns into
/// production constructors. Sitting underneath `IChatClient` reaches the same
/// data without either, and `DelegatingChatClient` is the framework's own seam
/// for it. [[ADR-001]] chose Microsoft.Extensions.AI partly because it "gives a
/// middleware pipeline that gives cost tracking, retry, timeouts and
/// OpenTelemetry a single home"; this is the first withdrawal against that.
///
/// It lives in the harness and not in Infrastructure on purpose. Production
/// code is unchanged by slice 5b, and instrumenting the running application is
/// Phase 6's job, where it arrives as OpenTelemetry rather than as a bespoke
/// recorder. The same pattern promotes into Infrastructure at that point.
///
/// **The stage is injected, not inferred.** One `IChatClient` serves both
/// classification and generation, so a single decorator could not tell which
/// stage a call belonged to. The harness wraps the client twice, once per
/// stage, which is possible because it already builds the two services
/// separately. The alternative - an `AsyncLocal` holding the "current stage" -
/// was rejected: ambient state that the decorator reads and the caller writes
/// is exactly the kind of action-at-a-distance that makes a latency figure
/// impossible to defend when it comes out wrong.
public sealed class RecordingChatClient(
    IChatClient inner,
    AiStage stage,
    AiCallRecorder recorder) : DelegatingChatClient(inner)
{
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        // GetTimestamp/GetElapsedTime rather than a Stopwatch instance: it is
        // the same monotonic clock with no allocation per call, which matters
        // at 300 calls a run only in the sense that the measurement should not
        // perturb what it measures.
        var started = Stopwatch.GetTimestamp();

        try
        {
            var response = await base.GetResponseAsync(messages, options, cancellationToken);
            recorder.RecordSuccess(stage, Stopwatch.GetElapsedTime(started), response.Usage);
            return response;
        }
        catch
        {
            // The services above catch everything non-cancellation and report a
            // failure, so without this the time a failed call spent would
            // vanish. Rethrown unchanged - this decorator observes, it does not
            // decide what a failure means.
            recorder.RecordFailure(stage, Stopwatch.GetElapsedTime(started));
            throw;
        }
    }
}
