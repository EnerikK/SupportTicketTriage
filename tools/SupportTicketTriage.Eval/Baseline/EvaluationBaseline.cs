using System.Text.Json;
using System.Text.Json.Serialization;
using SupportTicketTriage.Eval.Reporting;

namespace SupportTicketTriage.Eval.Baseline;

/// The versions a baseline's numbers are only meaningful against.
///
/// A gate that compared metrics without comparing these would answer the wrong
/// question. A prompt edit that lifts classification accuracy is an
/// improvement; a redaction bump makes recall figures non-comparable rather
/// than merely different ([[ADR-005]]); moving a routing floor changes
/// `NotEligible`, which is the denominator of citation validity, so a retune
/// would surface as a citation regression. In each case the honest response is
/// "this baseline no longer applies", not "the system got worse" - and that is
/// only possible if the baseline says what it was built from.
public sealed record BaselineProvenance(
    string DatasetVersion,
    string EmbeddingModel,
    string? ChatModel,
    string ClassificationPromptVersion,
    string GenerationPromptVersion,
    string RedactionVersion,
    double MinimumClassificationScore,
    double MinimumSimilarity,
    string? PricingVersion,
    DateTimeOffset GeneratedAt);

/// The numbers a run is allowed to be judged on.
///
/// Nullable throughout because a baseline can legitimately be written from a
/// run that skipped generation or had no chat deployment, and an absent metric
/// must read as "not covered" rather than as zero.
public sealed record BaselineMetrics(
    IReadOnlyDictionary<int, double> RecallAtK,
    double? ClassificationAccuracy,
    double? ClassificationMacroF1,
    double? CitationValidityRate,
    decimal? CostTotal,
    decimal? CostPerIngestedTicket,
    decimal? CostPerDraftedTicket);

/// What a run is compared against.
///
/// **Deliberately not a serialised `EvaluationReport`.** The previous baseline
/// was exactly that - a full report dump - and it rotted: six properties were
/// added to the report afterwards, and deserialising the committed file into
/// today's record yields nulls for two non-nullable lists and throws. A dump
/// also makes the gate's scope implicit, so "what are we actually guarding"
/// can only be answered by reading the comparison code.
///
/// This record is the answer to that question in one place. Everything in it is
/// gated; everything gated is in it.
///
/// **What is deliberately absent.**
///
/// *Latency*, because [[ADR-020]] argues it out: it tracks Azure's load rather
/// than this code, and a gate that fires on weather gets ignored, which costs
/// the gates that work.
///
/// *The gate calibration tables*, because they are tuning evidence rather than
/// a quality signal. Gate A's separation moved sevenfold between two identical
/// runs; gating it would fire constantly and mean nothing.
///
/// *Per-category F1*, because it moved 3.0pp across identical runs while
/// macro-F1 moved 0.3pp. The aggregate is the stable one, and gating the
/// components would fire on nothing but the clock.
public sealed record EvaluationBaseline(
    string Version,
    BaselineProvenance Provenance,
    BaselineMetrics Metrics,
    /// How far each metric may move before the gate calls it a regression.
    /// Here rather than in a file of their own so that loosening a tolerance
    /// and moving a number arrive in the same diff.
    BaselineTolerances Tolerances)
{
    /// Bumped when the shape changes, so a gate meeting an older file can say
    /// so rather than silently reading nulls - which is precisely how the
    /// previous baseline failed.
    public const string CurrentVersion = "2";

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static EvaluationBaseline From(
        EvaluationReport report,
        BaselineTolerances? tolerances = null) =>
        new(
            Version: CurrentVersion,
            Provenance: new BaselineProvenance(
                report.DatasetVersion,
                report.EmbeddingModel,
                report.ChatModel,
                report.ClassificationPromptVersion,
                report.GenerationPromptVersion,
                report.RedactionVersion,
                report.MinimumClassificationScore,
                report.MinimumSimilarity,
                report.Cost?.PricingVersion,
                report.GeneratedAt),
            Metrics: new BaselineMetrics(
                RecallAtK: report.RecallAtK
                    .OrderBy(kv => kv.Key)
                    .ToDictionary(kv => kv.Key, kv => kv.Value),
                ClassificationAccuracy: report.Classification?.Accuracy,
                ClassificationMacroF1: report.Classification?.MacroF1,
                CitationValidityRate: report.Citations?.ValidityRate,
                CostTotal: report.Cost?.Total,
                CostPerIngestedTicket: report.Cost?.PerIngestedTicket,
                CostPerDraftedTicket: report.Cost?.PerDraftedTicket),
            Tolerances: tolerances ?? BaselineTolerances.Default);

    public async Task WriteAsync(string path, CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(this, Json), ct);
    }

    /// Loads and validates, or throws naming what is wrong.
    ///
    /// The same shape as the dataset and pricing loaders, for the same reason:
    /// a baseline that loaded despite being malformed would not fail, it would
    /// quietly compare against something other than what the file says.
    public static async Task<EvaluationBaseline> LoadAsync(string path, CancellationToken ct = default)
    {
        await using var stream = File.OpenRead(path);
        var baseline = await JsonSerializer.DeserializeAsync<EvaluationBaseline>(stream, Json, ct)
            ?? throw new InvalidOperationException($"Baseline at {path} deserialised to null.");

        baseline.Validate();
        return baseline;
    }

    public void Validate()
    {
        if (Version != CurrentVersion)
        {
            throw new InvalidOperationException(
                $"Baseline is version '{Version}', this harness writes and reads version "
                + $"'{CurrentVersion}'. Regenerate it with --write-baseline rather than "
                + "comparing against a file whose fields may mean something else.");
        }

        if (Provenance is null || Metrics is null)
        {
            throw new InvalidOperationException("Baseline is missing its provenance or its metrics.");
        }

        if (string.IsNullOrWhiteSpace(Provenance.DatasetVersion))
        {
            throw new InvalidOperationException("Baseline records no dataset version.");
        }

        if (Tolerances is null)
        {
            throw new InvalidOperationException("Baseline records no tolerances.");
        }

        Tolerances.Validate();

        if (Metrics.RecallAtK.Count == 0)
        {
            throw new InvalidOperationException(
                "Baseline records no recall figures. Retrieval is the one thing every run "
                + "measures, so a baseline without it is guarding nothing.");
        }
    }
}
