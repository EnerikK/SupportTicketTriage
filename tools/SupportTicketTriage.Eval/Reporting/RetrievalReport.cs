using System.Text;
using System.Text.Json;
using SupportTicketTriage.Eval.Embedding;

namespace SupportTicketTriage.Eval.Reporting;

public sealed record RetrievalReport(
    string DatasetVersion,
    string EmbeddingModel,
    bool EmbeddingModelIsReal,
    DateTimeOffset GeneratedAt,
    int CorpusCount,
    int QueryCount,
    int ScoredQueryCount,
    int UnscoredQueryCount,
    IReadOnlyDictionary<int, double> RecallAtK,
    IReadOnlyDictionary<string, int> TraitCoverage)
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public async Task WriteJsonAsync(string path, CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(this, Json), ct);
    }

    public async Task WriteMarkdownAsync(string path, CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, ToMarkdown(), ct);
    }

    public string ToMarkdown()
    {
        var md = new StringBuilder();

        md.AppendLine("# Retrieval evaluation");
        md.AppendLine();

        if (!EmbeddingModelIsReal)
        {
            md.AppendLine(
                $"> **These numbers are not a baseline.** They were produced with `{EmbeddingModel}`, a "
                + "deterministic lexical stand-in, not a real embedding deployment. It matches on shared "
                + "wording and captures no semantics, so paraphrases score as unrelated. The figures below "
                + "show the harness works; they say nothing about retrieval quality.");
            md.AppendLine();
        }

        md.AppendLine($"- Dataset version: `{DatasetVersion}`");
        md.AppendLine($"- Embedding model: `{EmbeddingModel}`");
        md.AppendLine($"- Generated: {GeneratedAt:u}");
        md.AppendLine($"- Corpus tickets: {CorpusCount}");
        md.AppendLine($"- Queries: {QueryCount} ({ScoredQueryCount} scored, {UnscoredQueryCount} unlabelled)");
        md.AppendLine();

        md.AppendLine("## Recall@k");
        md.AppendLine();
        md.AppendLine("| k | mean recall |");
        md.AppendLine("|---|---|");
        foreach (var (k, recall) in RecallAtK.OrderBy(kv => kv.Key))
        {
            md.AppendLine($"| {k} | {recall:P1} |");
        }

        md.AppendLine();
        md.AppendLine(
            "Relevance labels were written by hand alongside the dataset, so recall partly measures our own "
            + "labelling rather than retrieval alone. Hard negatives and near-duplicates reduce that "
            + "circularity but do not remove it.");
        md.AppendLine();

        if (TraitCoverage.Count > 0)
        {
            md.AppendLine("## Dataset composition");
            md.AppendLine();
            md.AppendLine("| trait | tickets |");
            md.AppendLine("|---|---|");
            foreach (var (trait, count) in TraitCoverage.OrderBy(kv => kv.Key))
            {
                md.AppendLine($"| {trait} | {count} |");
            }

            md.AppendLine();
        }

        return md.ToString();
    }

    /// A baseline is what CI compares against, so it must never be written from
    /// a stand-in embedder. Refusing here is cheaper than discovering later
    /// that the gate has been guarding a meaningless number.
    public void EnsureCanBeBaseline()
    {
        if (!EmbeddingModelIsReal)
        {
            throw new InvalidOperationException(
                $"Refusing to write a baseline produced with '{EmbeddingModel}'. "
                + "A baseline requires a real embedding deployment.");
        }
    }

    public static bool IsRealEmbeddingModel(string model) => model != LexicalEmbeddingGenerator.ModelName;
}
