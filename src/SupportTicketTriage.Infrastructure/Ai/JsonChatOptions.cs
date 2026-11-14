using Microsoft.Extensions.AI;

namespace SupportTicketTriage.Infrastructure.Ai;

/// The options both pipeline stages send: a JSON response, and the chat
/// deployment's configured temperature.
///
/// One function rather than two object initialisers, because the two stages
/// must agree. A temperature omitted for classification but still sent for
/// drafting would make every draft fail on a reasoning model while
/// classification looked healthy.
public static class JsonChatOptions
{
    public static ChatOptions For(float? temperature) => new()
    {
        // Null leaves the property unset, so no temperature reaches the
        // request at all - which is what a reasoning model requires.
        Temperature = temperature,
        ResponseFormat = ChatResponseFormat.Json,
    };
}
