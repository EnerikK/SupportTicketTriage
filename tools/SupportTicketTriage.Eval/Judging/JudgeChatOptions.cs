using Microsoft.Extensions.AI;
using OpenAI.Chat;

namespace SupportTicketTriage.Eval.Judging;

/// How hard the judge is told to think.
///
/// `Default` means the parameter is not sent at all, so the provider's own
/// default applies. It is a distinct value rather than a null elsewhere
/// because "we did not set it" is a configuration worth recording: a run
/// whose provenance says `default` is reproducible, and one that says nothing
/// is a guess about what the API did that day.
public enum JudgeReasoningEffort
{
    Default,
    Low,
    Medium,
    High,
}

/// The one place in this repository that touches an experimental OpenAI API.
///
/// `ChatCompletionOptions.ReasoningEffortLevel` is marked `OPENAI001`, and
/// `TreatWarningsAsErrors` is on, so it cannot be used without a suppression.
/// The suppression lives here and nowhere else: this file exists so that the
/// blast radius of the experimental surface is one small, named function
/// rather than a project-wide `NoWarn` that would silently cover whatever
/// else the SDK marks experimental next.
///
/// `Microsoft.Extensions.AI` 10.10.0 has no `ChatOptions.ReasoningEffort`, so
/// the raw-options escape hatch is the only route. If a later version adds a
/// first-class property, this file should disappear rather than grow.
public static class JudgeChatOptions
{
    public static ChatOptions? For(JudgeReasoningEffort effort)
    {
        if (effort == JudgeReasoningEffort.Default)
        {
            // No options at all rather than empty options, so nothing about
            // the request differs from the unset case. The smoke check
            // compares token counts between this and Low, and it can only
            // mean something if this branch really sends nothing.
            return null;
        }

        return new ChatOptions { RawRepresentationFactory = _ => RawOptions(effort) };
    }

#pragma warning disable OPENAI001 // Experimental: ChatReasoningEffortLevel.
    private static ChatCompletionOptions RawOptions(JudgeReasoningEffort effort) =>
        new()
        {
            ReasoningEffortLevel = effort switch
            {
                JudgeReasoningEffort.Low => ChatReasoningEffortLevel.Low,
                JudgeReasoningEffort.Medium => ChatReasoningEffortLevel.Medium,
                JudgeReasoningEffort.High => ChatReasoningEffortLevel.High,
                _ => throw new ArgumentOutOfRangeException(nameof(effort), effort, null),
            },
        };
#pragma warning restore OPENAI001
}
