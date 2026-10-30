using System.Text.Json;
using System.Text.Json.Serialization;

namespace SupportTicketTriage.Eval.Pricing;

/// What one deployment costs per million tokens.
///
/// `OutputPerMillionTokens` is nullable because an embedding deployment has no
/// output tokens to charge for. Null means "this model cannot produce output
/// tokens", which is a different statement from zero - and if a chat model's
/// output price were ever null, pricing it at nothing would understate every
/// generation call in the run, so the validator rejects that case rather than
/// letting it through as free.
/// `RetrievedOn` and `DeploymentType` are per model, not per file.
///
/// They were file-level until pricing 1.1, and the single date became a
/// fiction the moment one rate was added without re-checking the others: it
/// claimed a freshness for all three that only one of them had. A rate is
/// retrieved on its own day, from its own meter.
///
/// `DeploymentType` matters because Azure meters the same model differently -
/// Global Standard and Data Zone differ by roughly a tenth - so a rate is only
/// interpretable next to the meter it came from. It is the SKU name as Azure
/// reports it (`GlobalStandard`, `DataZoneStandard`), so it can be checked
/// against `az cognitiveservices account deployment list` rather than being a
/// label this file invents.
public sealed record ModelPrice(
    string Model,
    decimal InputPerMillionTokens,
    decimal? OutputPerMillionTokens,
    DateOnly RetrievedOn,
    string DeploymentType);

/// The versioned pricing assumptions a cost figure rests on.
///
/// **Why a file and not constants in code.** The specification is explicit that
/// "pricing assumptions live in an explicit, versioned file, never inside
/// application code", and the reason is that prices are somebody else's data:
/// they change without any change to this repository, they vary by region and
/// by agreement, and a cost number is only interpretable next to the prices and
/// the date it was computed from. `Version` is stamped into every report for
/// the same reason `DatasetVersion` is - so two runs that disagree can be told
/// apart by what changed.
///
/// **Decimal, not double.** These are money. The usual reason applies, and so
/// does a local one: this repository has now been bitten four times by a double
/// that was not the number it printed, and a price is the last place to invite
/// that again.
public sealed record ModelPricing(
    string Version,
    string Currency,
    string Source,
    /// False until a human has checked these figures against the actual Azure
    /// portal for the subscription in question. The harness still computes with
    /// unverified prices - the arithmetic is the same - but the report carries a
    /// warning and a baseline is refused, mirroring how the lexical stand-in is
    /// handled. An unverified cost figure is a development aid, not a result.
    bool Verified,
    IReadOnlyList<ModelPrice> Models)
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static async Task<ModelPricing> LoadAsync(string path, CancellationToken ct = default)
    {
        await using var stream = File.OpenRead(path);
        var pricing = await JsonSerializer.DeserializeAsync<ModelPricing>(stream, Json, ct)
            ?? throw new InvalidOperationException($"Pricing file at {path} deserialised to null.");

        pricing.Validate();
        return pricing;
    }

    /// Throws on anything that would silently produce a wrong number.
    ///
    /// The dataset loader does the same, for the same reason: a malformed
    /// pricing file that loaded anyway would not fail, it would quietly report
    /// a cost that is wrong by an unknown factor, and nothing downstream could
    /// tell. Failing at load is the only point where the problem is still
    /// obvious.
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Version))
        {
            throw new InvalidOperationException("Pricing file has no version.");
        }

        if (string.IsNullOrWhiteSpace(Currency))
        {
            throw new InvalidOperationException("Pricing file has no currency.");
        }

        if (Models.Count == 0)
        {
            throw new InvalidOperationException("Pricing file lists no models.");
        }

        var duplicates = Models
            .GroupBy(m => m.Model, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (duplicates.Count > 0)
        {
            throw new InvalidOperationException(
                $"Pricing file prices these models more than once: {string.Join(", ", duplicates)}. "
                + "Which price applied would depend on list order, which is not a decision this "
                + "file should be making.");
        }

        foreach (var model in Models)
        {
            if (string.IsNullOrWhiteSpace(model.Model))
            {
                throw new InvalidOperationException("Pricing file has an entry with no model name.");
            }

            if (model.InputPerMillionTokens < 0 || model.OutputPerMillionTokens < 0)
            {
                throw new InvalidOperationException(
                    $"Pricing for '{model.Model}' is negative.");
            }

            // A rate with no meter is not interpretable: the same model costs
            // about a tenth more on one Azure deployment type than another, so
            // a figure without one could be either and the report would not
            // know which it had published.
            if (string.IsNullOrWhiteSpace(model.DeploymentType))
            {
                throw new InvalidOperationException(
                    $"Pricing for '{model.Model}' names no deployment type. Azure meters Global "
                    + "Standard and Data Zone differently, so a rate without its meter cannot be "
                    + "checked against the deployment it is supposed to price.");
            }
        }
    }

    /// The price for a deployment, or null if this file does not name it.
    ///
    /// Null rather than a throw, because the caller has the context to decide:
    /// a run against the lexical stand-in legitimately has no price, while a
    /// run against a real deployment that is missing from this file is a
    /// reporting gap the cost metric surfaces as unpriced calls.
    public ModelPrice? For(string model) =>
        Models.FirstOrDefault(m => string.Equals(m.Model, model, StringComparison.OrdinalIgnoreCase));
}
