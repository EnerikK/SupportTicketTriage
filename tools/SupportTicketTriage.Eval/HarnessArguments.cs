namespace SupportTicketTriage.Eval;

/// The harness's command line, parsed and checked against a closed set.
///
/// **Why this is a type rather than a local function in `Program`.** It used to
/// be the latter, which made it unreachable from a test, and what it did when
/// it met a flag it did not recognise was to ignore it silently. That is
/// tolerable for a tool a human watches and intolerable for one a regression
/// gate depends on: a mistyped `--compare-baseline` would be accepted, do
/// nothing, and exit zero, and CI would report success for a check that never
/// ran. A gate that cannot fail is the failure mode this whole phase exists to
/// avoid.
///
/// So the vocabulary is closed and declared here, an unknown flag is an error
/// naming what was expected, and the whole thing is testable without starting
/// a database.
public sealed class HarnessArguments
{
    /// Every flag the harness accepts. A flag that takes no value is still
    /// listed - the distinction between "unknown" and "valueless" is what the
    /// old parser could not make.
    public static readonly IReadOnlySet<string> Recognised =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "help",
            "connection",
            "dataset",
            "output",
            "pricing",
            "reset",
            "skip-generation",
            "write-baseline",
            "compare-baseline",
            "write-logic-baseline",
            "export-judge-pairs",
            "judge-labels",
            "judge-verdicts",
            "judge-effort",
        };

    private readonly Dictionary<string, string?> _values;

    private HarnessArguments(Dictionary<string, string?> values) => _values = values;

    public bool Has(string name) => _values.ContainsKey(name);

    public string? Value(string name) => _values.GetValueOrDefault(name);

    /// Parses, or throws naming the flag that was not understood.
    ///
    /// Bare words that are not preceded by a flag are ignored, as before - the
    /// harness has no positional arguments, and rejecting them would turn a
    /// stray shell-expanded path into a failure for no benefit. Only `--`
    /// prefixed tokens are vocabulary.
    public static HarnessArguments Parse(string[] args)
    {
        var parsed = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var unknown = new List<string>();

        for (var i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            var key = args[i][2..];
            var hasValue = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal);
            var value = hasValue ? args[++i] : null;

            if (!Recognised.Contains(key))
            {
                unknown.Add($"--{key}");
                continue;
            }

            parsed[key] = value;
        }

        if (unknown.Count > 0)
        {
            throw new ArgumentException(
                $"Unrecognised argument(s): {string.Join(", ", unknown)}. "
                + $"Recognised: {string.Join(", ", Recognised.Order().Select(f => $"--{f}"))}. "
                + "Run with --help for usage.");
        }

        return new HarnessArguments(parsed);
    }
}
