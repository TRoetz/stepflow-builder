using System.ComponentModel;
using ModelContextProtocol.Server;
using Newtonsoft.Json;
using StepFunctionsApp.Rules;

namespace StepFunctionsApp.Mcp
{
    // ═══════════════════════════════════════════════════════════════════════════════
    // MCP RULE TOOLS — manage named rule artifacts (the project's rule catalog).
    // Kinds: choice | jsonata | sql | ms-rules | ai-decision. The sql and ms-rules kinds
    // are engine-backed: saving one makes rule://<name> / rules://<name> flow resources
    // work immediately; the others document decision logic flows carry inline.
    // ═══════════════════════════════════════════════════════════════════════════════

    [McpServerToolType]
    public class RuleTools
    {
        private readonly NamedRuleManager _rules;

        public RuleTools(NamedRuleManager rules) => _rules = rules;

        [McpServerTool]
        [Description(
"""
List all named rule artifacts (the project's rule catalog). Each entry has name, kind (choice | jsonata | sql | ms-rules | ai-decision), description and the full definition body.
""")]
        public string ListRules() => JsonConvert.SerializeObject(_rules.List(), McpJson.Settings);

        [McpServerTool]
        [Description(
"""
Get one named rule artifact by name (flow-derived artifacts are named '<FlowName>/<StateLabel>'). Returns {error} when unknown.
""")]
        public string GetRule([Description("Rule name, e.g. 'FeeTypeRouting' or 'Council Fee Capture/ValidateFeeType'.")] string name)
        {
            var rule = _rules.Get(name);
            return rule == null ? Error($"Rule '{name}' not found.") : JsonConvert.SerializeObject(rule, McpJson.Settings);
        }

        [McpServerTool]
        [Description(
"""
Create or replace a named rule artifact. ruleJson is the full rule: { "name": "...", "kind": "choice|jsonata|sql|ms-rules|ai-decision", "description"?, "definition": {...} }. Definition bodies — choice: {"conditions":[{"expression","next"?}],"defaultNext"?}; jsonata: {"expression"}; sql: {"expression","requiredParameters"?,"isBlocking"?} (registered as rule://<name>); ms-rules: {"rules":[{"ruleName","expression","errorMessage"?,"successEvent"?}]} (registered as rules://<name>); ai-decision: {"question"?,"systemPrompt"?,"provider"?,"confidenceThreshold"?}.
""")]
        public string SaveRule([Description("The full rule JSON document.")] string ruleJson)
        {
            try
            {
                var rule = JsonConvert.DeserializeObject<NamedRule>(ruleJson);
                if (rule == null) return Error("ruleJson did not deserialize to a rule.");
                return JsonConvert.SerializeObject(_rules.Save(rule), McpJson.Settings);
            }
            catch (Exception ex) when (ex is ArgumentException or ArgumentNullException or JsonException)
            {
                return Error(ex.Message);
            }
        }

        [McpServerTool]
        [Description("Delete a named rule artifact by name and unregister it from its engine. Returns {error} when unknown.")]
        public string DeleteRule([Description("Rule name to delete.")] string name)
        {
            var deleted = _rules.Delete(name);
            return deleted ? JsonConvert.SerializeObject(new { deleted = name }, McpJson.Settings) : Error($"Rule '{name}' not found.");
        }

        private static string Error(string message) =>
            JsonConvert.SerializeObject(new { error = message }, McpJson.Settings);
    }
}
