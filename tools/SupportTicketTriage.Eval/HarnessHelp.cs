namespace SupportTicketTriage.Eval;

/// The harness usage text.
///
/// Lifted out of Program.cs so a test can read it. It used to be an inline
/// string mirrored by a hand-maintained list in the test project, which made
/// three copies of the same facts - and the one invariant that mattered went
/// unguarded: a flag documented here but missing from
/// <see cref="HarnessArguments.Recognised"/> advertises something the parser
/// rejects. That is exactly what shipped with the judge flags, and the test
/// could not see it because it was comparing the allowlist against another
/// copy of the allowlist.
public static class HarnessHelp
{
    public const string Text =
        """
        Retrieval evaluation harness.

          --help                  Show this text
          --connection <string>   PostgreSQL connection string (required)
          --dataset <path>        Dataset JSON (default: eval/dataset/tickets.json)
          --holdout-queries <path>
                                  Take the corpus from --dataset but the queries
                                  from <path>. For producing drafts to be labelled
                                  by hand against a query set the judge rubric was
                                  not written from. Baseline writing is refused in
                                  this mode.
          --output <dir>          Report directory (default: eval/results)
          --reset                 Delete existing tickets before seeding
          --skip-generation       Score classification but not drafts (saves the
                                  generation and citation calls)
          --pricing <path>        Token pricing (default: eval/pricing/model-pricing.json);
                                  costs are reported as not scored without it
          --export-judge-pairs <p> Write each valid draft with the sources it
                                  cited, for hand-labelling. ADR-018 requires
                                  those labels to come from a human: the figure
                                  exists to validate an LLM judge against
                                  something that is not one.
          --judge-pairs-from <p>  Judge an exported pair file and exit, without
                                  seeding or regenerating anything. The only
                                  correct way to run several configurations
                                  against one set of hand labels: generation is
                                  nondeterministic, so re-running the pipeline
                                  per configuration judges different drafts
          --judge-limit <n>       Judge only the first n pairs. For smoke checks
          --judge-verdicts <path> Write what the judge said, with the tokens it
                                  spent saying it. One file per configuration;
                                  kept out of the labelling file so a verdict
                                  never sits beside the blank a human is filling
          --judge-effort <level>  default|low|medium|high. Reasoning effort for
                                  the judge. Recorded in run provenance, because
                                  the same model at two efforts is two judges
          --dev-check <path>      Run the judge against development pairs with
                                  expected verdicts from <path> and exit. A
                                  behavioural check, never a reported number -
                                  these pairs produced the rules and cannot
                                  also test them. Needs --judge-pairs-from
          --compare-judges <list> Comma-separated verdict files. Builds the
                                  comparison table from committed artifacts and
                                  exits; the first file is the baseline every
                                  other is tested against. Requires
                                  --judge-labels
          --judge-labels <path>   Read hand labels back and report the judge's
                                  agreement and Cohen's kappa. Without this the
                                  groundedness score is withheld rather than
                                  printed.
          --compare-baseline [p]  Compare this run against a baseline and exit
                                  non-zero on regression beyond tolerance
                                  (default: eval/baseline/evaluation-baseline.json)
          --write-logic-baseline  Write eval/baseline/lexical-logic-baseline.json
                                  from a stand-in run; guards retrieval logic,
                                  not retrieval quality
          --write-baseline        Write eval/baseline/evaluation-baseline.json;
                                  refused for stand-in embedders and for costs
                                  computed from unverified pricing

        Deployments are read from the environment, not from arguments, so the key
        stays out of shell history.

          AzureOpenAi__Endpoint
          AzureOpenAi__ApiKey
          AzureOpenAi__EmbeddingDeployment   retrieval; without it the run falls
                                             back to the lexical stand-in, which
                                             cannot produce a baseline
          AzureOpenAi__ChatDeployment        classification and citation validity;
                                             without it those are reported as not
                                             measured rather than scored zero
          AzureOpenAi__JudgeDeployment       groundedness. Must be a different and
                                             stronger deployment than the drafting
                                             one - a model scoring its own output
                                             exhibits self-preference, so the same
                                             name is refused rather than honoured.
                                             AZURE_OPENAI_CHAT_DEPLOYMENT_STRONGER
                                             is read as an alias, since that is the
                                             name .env already carries
        """;
}
