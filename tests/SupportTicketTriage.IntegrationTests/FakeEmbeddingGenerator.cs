using Microsoft.Extensions.AI;
using SupportTicketTriage.Infrastructure.Persistence;

namespace SupportTicketTriage.IntegrationTests;

/// Stands in for Azure OpenAI so the embedding path is deterministic and so
/// tests can assert exactly what text crossed the AI boundary.
public sealed class FakeEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
{
    private readonly List<string> _received = [];

    public IReadOnlyList<string> Received
    {
        get
        {
            lock (_received)
            {
                return _received.ToList();
            }
        }
    }

    public bool ShouldThrow { get; set; }

    public void Reset()
    {
        lock (_received)
        {
            _received.Clear();
        }

        ShouldThrow = false;
    }

    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var inputs = values.ToList();

        lock (_received)
        {
            _received.AddRange(inputs);
        }

        if (ShouldThrow)
        {
            throw new InvalidOperationException("Simulated Azure OpenAI failure.");
        }

        var results = inputs
            .Select(_ => new Embedding<float>(new float[TicketEmbedding.Dimensions]))
            .ToList();

        return Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(results));
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}
