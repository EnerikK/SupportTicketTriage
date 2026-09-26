using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace SupportTicketTriage.IntegrationTests;

/// Stands in for the Azure OpenAI chat deployment so the classification path
/// is deterministic and so tests can assert exactly what text crossed the AI
/// boundary.
public sealed class FakeChatClient : IChatClient
{
    private readonly List<List<ChatMessage>> _received = [];

    public IReadOnlyList<List<ChatMessage>> Received
    {
        get
        {
            lock (_received)
            {
                return _received.ToList();
            }
        }
    }

    /// What the model "replies". Defaults to a well-formed classification so a
    /// test only has to set it when exercising a failure path.
    public string ResponseText { get; set; } =
        """{"category": "Billing", "priority": "High", "score": 0.91}""";

    public bool ShouldThrow { get; set; }

    public void Reset()
    {
        lock (_received)
        {
            _received.Clear();
        }

        ResponseText = """{"category": "Billing", "priority": "High", "score": 0.91}""";
        ShouldThrow = false;
    }

    /// Every message of every call, flattened. Used to assert that no raw
    /// identifier reached the model on any path.
    public string AllText => string.Join("\n", Received.SelectMany(m => m).Select(m => m.Text));

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        lock (_received)
        {
            _received.Add(messages.ToList());
        }

        if (ShouldThrow)
        {
            throw new InvalidOperationException("Simulated Azure OpenAI failure.");
        }

        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, ResponseText)));
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await GetResponseAsync(messages, options, cancellationToken);

        foreach (var message in response.Messages)
        {
            yield return new ChatResponseUpdate(message.Role, message.Text);
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}
