using System.Text.Json;
using System.Text.Json.Serialization;

namespace SupportTicketTriage.Eval.Dataset;

public sealed record EvalDataset(
    string Version,
    IReadOnlyList<string> Categories,
    IReadOnlyList<EvalTicket> Tickets)
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public IEnumerable<EvalTicket> Corpus => Tickets.Where(t => t.Role == TicketRole.Corpus);

    public IEnumerable<EvalTicket> Queries => Tickets.Where(t => t.Role == TicketRole.Query);

    /// This dataset's corpus, with its queries replaced by another file's.
    ///
    /// The held-out judge set is queries only, deliberately. Appending ~105
    /// queries to tickets.json instead would bump the dataset version and
    /// invalidate both committed baselines - and because recall@k is a mean
    /// over scored queries, it would change the headline retrieval figures
    /// too. That would reset the one part of the evaluation that is fully
    /// established in order to validate something else ([[ADR-036]]).
    ///
    /// Validation runs over the combined set rather than either half, so an id
    /// in the holdout file that collides with a corpus ticket fails here
    /// rather than seeding two tickets and silently retrieving the wrong one.
    public EvalDataset WithQueriesFrom(EvalDataset holdout)
    {
        var combined = new EvalDataset(
            Version,
            Categories,
            [.. Corpus, .. holdout.Queries]);

        combined.Validate();
        return combined;
    }

    public static async Task<EvalDataset> LoadAsync(string path, CancellationToken ct = default)
    {
        await using var stream = File.OpenRead(path);
        var dataset = await JsonSerializer.DeserializeAsync<EvalDataset>(stream, SerializerOptions, ct)
            ?? throw new InvalidOperationException($"Dataset at {path} is empty.");

        dataset.Validate();
        return dataset;
    }

    /// A dataset that references ids which do not exist would silently depress
    /// recall and look like a retrieval problem, so it fails loudly instead.
    public void Validate()
    {
        var ids = Tickets.Select(t => t.Id).ToList();

        var duplicates = ids.GroupBy(id => id).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (duplicates.Count > 0)
        {
            throw new InvalidOperationException($"Duplicate ticket ids: {string.Join(", ", duplicates)}");
        }

        var known = ids.ToHashSet();
        var corpusIds = Corpus.Select(t => t.Id).ToHashSet();

        foreach (var query in Queries)
        {
            foreach (var relevant in query.RelevantIds)
            {
                if (!known.Contains(relevant))
                {
                    throw new InvalidOperationException(
                        $"Query '{query.Id}' references unknown ticket '{relevant}'.");
                }

                if (!corpusIds.Contains(relevant))
                {
                    throw new InvalidOperationException(
                        $"Query '{query.Id}' references '{relevant}', which is not a corpus ticket.");
                }
            }
        }

        foreach (var corpus in Corpus.Where(t => string.IsNullOrWhiteSpace(t.Resolution)))
        {
            throw new InvalidOperationException(
                $"Corpus ticket '{corpus.Id}' has no resolution, so it could never be retrieved.");
        }
    }
}

public sealed record EvalTicket(
    string Id,
    TicketRole Role,
    string Category,
    string Priority,
    string Subject,
    string Body)
{
    public string? Resolution { get; init; }

    public IReadOnlyList<string> RelevantIds { get; init; } = [];

    /// Free-form markers such as near-duplicate, hard-negative, pii, injection,
    /// malformed or ambiguous. Used to report coverage, not to alter scoring.
    public IReadOnlyList<string> Traits { get; init; } = [];
}

public enum TicketRole
{
    Corpus,
    Query,
}
