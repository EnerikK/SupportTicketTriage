using Microsoft.Extensions.AI;

namespace SupportTicketTriage.Eval.Embedding;

/// A deterministic lexical stand-in for a real embedding deployment.
///
/// It hashes tokens into a fixed-width vector and L2-normalises, so texts that
/// share wording land near each other. That is enough to exercise the harness
/// end to end and to make a mislabelled dataset visible, but it captures no
/// semantics: paraphrases with no shared vocabulary look unrelated.
///
/// Numbers produced with this generator are development aids. They are never a
/// baseline, which is why the model name it reports is unmistakable.
public sealed class LexicalEmbeddingGenerator(int dimensions) : IEmbeddingGenerator<string, Embedding<float>>
{
    public const string ModelName = "fake-lexical-v1";

    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var embeddings = values
            .Select(value => new Embedding<float>(Vectorise(value)) { ModelId = ModelName })
            .ToList();

        return Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(embeddings));
    }

    public float[] Vectorise(string text)
    {
        var vector = new float[dimensions];

        foreach (var token in Tokenise(text))
        {
            // Sublinear term weighting would need a second pass; raw counts are
            // enough here and keep the mapping trivial to reason about.
            vector[(int)(Fnv1A(token) % (uint)dimensions)] += 1f;
        }

        Normalise(vector);
        return vector;
    }

    private static IEnumerable<string> Tokenise(string text)
    {
        var token = new System.Text.StringBuilder();

        foreach (var c in text)
        {
            if (char.IsLetterOrDigit(c))
            {
                token.Append(char.ToLowerInvariant(c));
            }
            else if (token.Length > 0)
            {
                yield return token.ToString();
                token.Clear();
            }
        }

        if (token.Length > 0)
        {
            yield return token.ToString();
        }
    }

    // String.GetHashCode is randomised per process, so vectors would differ
    // between runs and nothing would be reproducible. FNV-1a is stable.
    private static uint Fnv1A(string token)
    {
        const uint offsetBasis = 2166136261;
        const uint prime = 16777619;

        var hash = offsetBasis;
        foreach (var c in token)
        {
            hash ^= c;
            hash *= prime;
        }

        return hash;
    }

    private static void Normalise(float[] vector)
    {
        double sumOfSquares = 0;
        foreach (var v in vector)
        {
            sumOfSquares += v * v;
        }

        if (sumOfSquares == 0)
        {
            return;
        }

        var magnitude = (float)Math.Sqrt(sumOfSquares);
        for (var i = 0; i < vector.Length; i++)
        {
            vector[i] /= magnitude;
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}
