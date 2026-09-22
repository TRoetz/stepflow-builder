using Newtonsoft.Json.Linq;

namespace StepFunctionsApp.Rules
{
    // ═══════════════════════════════════════════════════════════════════════════════
    // NAMED RULE ARTIFACTS — rules as first-class, persistent project artifacts.
    //
    // A NamedRule is a versionable unit of decision logic that lives in the rule store
    // (rules.json by default) independently of any flow, so it can be reviewed, reused
    // and shipped inside solution packages like every other project artifact:
    //
    //   kind        definition body                              engine backing
    //   ──────────  ───────────────────────────────────────────  ─────────────────────
    //   choice      { conditions:[{expression,next?}],           (in-flow Choice states;
    //                defaultNext? }                               exported as an artifact)
    //   jsonata     { expression }                               (transform://jsonata logic)
    //   sql         { expression, requiredParameters?,           rule://<name> via
    //                isBlocking? }                                RuleEngineService
    //   ms-rules    { rules:[{ruleName,expression,               rules://<name> via
    //                  errorMessage?,successEvent?}]              MicrosoftRulesEngineService
    //   ai-decision { question?, systemPrompt?, provider?,       (ai:// decision config)
    //                confidenceThreshold? }
    //
    // Flow-derived artifacts are named "<FlowName>/<StateLabel>" so a package's rules
    // section reads as the project's rule catalog.
    // ═══════════════════════════════════════════════════════════════════════════════

    public static class RuleKinds
    {
        public const string Choice = "choice";
        public const string Jsonata = "jsonata";
        public const string Sql = "sql";
        public const string MsRules = "ms-rules";
        public const string AiDecision = "ai-decision";

        public static readonly IReadOnlyList<string> All = new[] { Choice, Jsonata, Sql, MsRules, AiDecision };

        public static bool IsKnown(string? kind) =>
            !string.IsNullOrWhiteSpace(kind) && All.Contains(kind!, StringComparer.OrdinalIgnoreCase);
    }

    public sealed class NamedRule
    {
        /// <summary>Unique identity. Flow-derived artifacts use "&lt;FlowName&gt;/&lt;StateLabel&gt;".</summary>
        public string Name { get; set; } = "";

        /// <summary>One of RuleKinds: choice | jsonata | sql | ms-rules | ai-decision.</summary>
        public string Kind { get; set; } = RuleKinds.Choice;

        public string? Description { get; set; }

        /// <summary>Kind-specific body — see the kind table in this file's header comment.</summary>
        public JToken Definition { get; set; } = new JObject();

        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    }
}
