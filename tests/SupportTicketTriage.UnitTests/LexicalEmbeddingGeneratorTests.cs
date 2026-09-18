using SupportTicketTriage.Eval.Embedding;

namespace SupportTicketTriage.UnitTests;

public class LexicalEmbeddingGeneratorTests
{
    private const int Dimensions = 1536;

    private readonly LexicalEmbeddingGenerator _generator = new(Dimensions);

    private static double Cosine(float[] a, float[] b)
    {
        double dot = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
        }

        return dot;
    }

    [Fact]
    public void Vectorise_IsDeterministic()
    {
        // String.GetHashCode is randomised per process; a vector that changed
        // between runs would make every evaluation unreproducible.
        Assert.Equal(
            _generator.Vectorise("payment declined at checkout"),
            _generator.Vectorise("payment declined at checkout"));
    }

    [Fact]
    public void Vectorise_ProducesTheConfiguredWidth()
    {
        Assert.Equal(Dimensions, _generator.Vectorise("anything").Length);
    }

    [Fact]
    public void Vectorise_ReturnsUnitVectors()
    {
        var vector = _generator.Vectorise("refund not received after nine days");

        Assert.Equal(1.0, Math.Sqrt(Cosine(vector, vector)), precision: 5);
    }

    [Fact]
    public void Vectorise_IsCaseAndPunctuationInsensitive()
    {
        Assert.Equal(
            _generator.Vectorise("Charged twice!"),
            _generator.Vectorise("charged   twice"));
    }

    [Fact]
    public void Vectorise_PlacesSharedWordingCloserThanUnrelatedText()
    {
        var query = _generator.Vectorise("charged twice for the same order");
        var related = _generator.Vectorise("two charges for one order on my card");
        var unrelated = _generator.Vectorise("how should I clean this without damaging the coating");

        Assert.True(
            Cosine(query, related) > Cosine(query, unrelated),
            "Shared wording should score closer than unrelated text.");
    }

    [Fact]
    public void Vectorise_HandlesTextWithNoUsableTokens()
    {
        var vector = _generator.Vectorise("!!! ... ???");

        Assert.Equal(Dimensions, vector.Length);
        Assert.All(vector, v => Assert.Equal(0, v));
    }

    [Fact]
    public async Task GenerateAsync_StampsTheStandInModelName()
    {
        var embeddings = await _generator.GenerateAsync(["first", "second"]);

        Assert.Equal(2, embeddings.Count);
        Assert.All(embeddings, e => Assert.Equal(LexicalEmbeddingGenerator.ModelName, e.ModelId));
    }
}
