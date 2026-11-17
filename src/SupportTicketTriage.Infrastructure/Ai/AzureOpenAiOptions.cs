namespace SupportTicketTriage.Infrastructure.Ai;

public sealed class AzureOpenAiOptions
{
    public const string SectionName = "AzureOpenAi";

    public string? Endpoint { get; set; }
    public string? ApiKey { get; set; }
    public string? EmbeddingDeployment { get; set; }
    public string? ChatDeployment { get; set; }

    /// The sampling temperature sent to the chat deployment, or null to send
    /// none. A property of the deployment, not of the call: reasoning models
    /// reject a temperature outright, so the same code must send one to
    /// `gpt-4.1-mini` and omit it for a reasoning-model successor.
    /// See <see cref="ParseTemperature"/> for the configured form.
    public float? ChatTemperature { get; set; } = DefaultChatTemperature;

    /// Zero, because classification is a fixed choice from a closed list and
    /// drafting is evaluated against a baseline: sampling would add only
    /// variance between identical inputs.
    public const float DefaultChatTemperature = 0f;

    /// The value that means "send no temperature".
    public const string OmitTemperature = "omit";

    /// Reads the configured temperature. Absent keeps the default; `omit`
    /// sends none; anything else must be a number in [0, 2].
    ///
    /// An unreadable value throws rather than falling back. Falling back would
    /// quietly send 0 to a reasoning model that rejects it, and every call
    /// would fail as "unclassified" with the cause buried in a log.
    /// Invariant culture for the same reason as the routing floors (ADR-009).
    public static float? ParseTemperature(string? configured)
    {
        if (string.IsNullOrWhiteSpace(configured))
        {
            return DefaultChatTemperature;
        }

        if (string.Equals(configured.Trim(), OmitTemperature, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (float.TryParse(
                configured,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out var value)
            && value is >= 0f and <= 2f)
        {
            return value;
        }

        throw new InvalidOperationException(
            $"AzureOpenAi:ChatTemperature must be a number from 0 to 2, or '{OmitTemperature}' to send none. Got '{configured}'.");
    }

    /// Settings that contradict each other, which would otherwise start the API
    /// in a state nobody chose: AI quietly off because half the credentials
    /// are missing, or a client built against an endpoint that cannot work.
    /// Values are described, never echoed, because the field most likely to
    /// hold the wrong thing is the one holding the key (ADR-044).
    ///
    /// A deployment named with no endpoint and no key is deliberately absent.
    /// It is how the integration tests run the real services against a faked
    /// model, and in a real deployment it is reported by `/health` instead of
    /// stopping startup.
    public IReadOnlyList<string> Validate()
    {
        var problems = new List<string>();
        var hasEndpoint = !string.IsNullOrWhiteSpace(Endpoint);
        var hasKey = !string.IsNullOrWhiteSpace(ApiKey);

        if (hasEndpoint && !hasKey)
        {
            problems.Add($"{SectionName}:Endpoint is set but {SectionName}:ApiKey is not.");
        }

        if (hasKey && !hasEndpoint)
        {
            problems.Add($"{SectionName}:ApiKey is set but {SectionName}:Endpoint is not.");
        }

        if (hasEndpoint
            && !(Uri.TryCreate(Endpoint, UriKind.Absolute, out var endpoint) && endpoint.Scheme == Uri.UriSchemeHttps))
        {
            problems.Add($"{SectionName}:Endpoint is not an absolute https:// URL.");
        }

        // The portal shows the key and the endpoint side by side, and the
        // endpoint has been pasted into the key field before (CI guards the
        // same mistake in its secrets).
        if (hasKey
            && (ApiKey!.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || ApiKey.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
        {
            problems.Add($"{SectionName}:ApiKey is a URL. The endpoint has probably been pasted into the key field.");
        }

        if (HasCredentials
            && string.IsNullOrWhiteSpace(EmbeddingDeployment)
            && string.IsNullOrWhiteSpace(ChatDeployment))
        {
            problems.Add(
                $"{SectionName}:Endpoint and {SectionName}:ApiKey are set, but neither "
                + $"{SectionName}:EmbeddingDeployment nor {SectionName}:ChatDeployment is, so nothing would use them.");
        }

        return problems;
    }

    private bool HasCredentials =>
        !string.IsNullOrWhiteSpace(Endpoint) && !string.IsNullOrWhiteSpace(ApiKey);

    // The two deployments are configured independently because they fail
    // independently: an embedding deployment without a chat one still gives
    // working retrieval, which is the state this project was in until now.
    public bool IsEmbeddingConfigured =>
        HasCredentials && !string.IsNullOrWhiteSpace(EmbeddingDeployment);

    public bool IsChatConfigured =>
        HasCredentials && !string.IsNullOrWhiteSpace(ChatDeployment);
}
