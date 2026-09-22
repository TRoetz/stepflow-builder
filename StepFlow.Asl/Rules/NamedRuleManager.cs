using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StepFunctionsApp.StepFunctions;

namespace StepFunctionsApp.Rules
{
    /// <summary>
    /// Facade over the named rule store that keeps the execution engines in sync:
    ///   sql      → RuleEngineService.RegisterRule(name, …)     so rule://&lt;name&gt; works
    ///   ms-rules → MicrosoftRulesEngineService workflow        so rules://&lt;name&gt; works
    /// The other kinds (choice / jsonata / ai-decision) are pure artifacts — they document and
    /// version the decision logic that flows carry inline, without engine backing.
    /// </summary>
    public sealed class NamedRuleManager
    {
        private readonly INamedRuleStore _store;
        private readonly RuleEngineService _sqlRules;
        private readonly MicrosoftRulesEngineService _msRules;

        public NamedRuleManager(INamedRuleStore store, RuleEngineService sqlRules, MicrosoftRulesEngineService msRules)
        {
            _store = store;
            _sqlRules = sqlRules;
            _msRules = msRules;
        }

        public IReadOnlyList<NamedRule> List() => _store.List();

        public NamedRule? Get(string name) => _store.Get(name);

        /// <summary>Upserts the rule and (re)registers it with its engine when applicable.</summary>
        public NamedRule Save(NamedRule rule)
        {
            var saved = _store.Save(rule);
            SyncWithEngines(saved);
            return saved;
        }

        /// <summary>Removes the rule and unregisters it from its engine when applicable.</summary>
        public bool Delete(string name)
        {
            var existing = _store.Get(name);
            if (existing == null || !_store.Delete(name)) return false;
            UnsyncFromEngines(existing);
            return true;
        }

        /// <summary>Boot-time: register every persisted engine-backed rule so rule:// and rules:// survive restarts.</summary>
        public void SyncAllWithEngines()
        {
            foreach (var rule in _store.List())
                SyncWithEngines(rule);
        }

        private static void ValidateEngineDefinition(NamedRule rule)
        {
            switch (rule.Kind)
            {
                case RuleKinds.Sql:
                    if (rule.Definition is not JObject sqlDef || sqlDef["expression"]?.Type != JTokenType.String)
                        throw new ArgumentException($"sql rule '{rule.Name}' needs definition.expression (a SQL boolean expression, e.g. '{{amount}} > 100').");
                    break;
                case RuleKinds.MsRules:
                    if (rule.Definition is not JObject msDef || msDef["rules"]?.Type != JTokenType.Array)
                        throw new ArgumentException($"ms-rules rule '{rule.Name}' needs definition.rules (an array of {{ruleName, expression}}).");
                    break;
            }
        }

        private void SyncWithEngines(NamedRule rule)
        {
            switch (rule.Kind)
            {
                case RuleKinds.Sql:
                    ValidateEngineDefinition(rule);
                    var def = (JObject)rule.Definition!;
                    _sqlRules.RegisterRule(rule.Name, new RuleDefinition
                    {
                        RuleId = rule.Name,
                        Name = rule.Name,
                        Description = rule.Description ?? "",
                        Expression = (string?)def["expression"],
                        RequiredParameters = def["requiredParameters"] is JArray arr ? arr.Values<string>().ToList() : new List<string>()
                    });
                    break;

                case RuleKinds.MsRules:
                    ValidateEngineDefinition(rule);
                    var rules = ((JObject)rule.Definition!)["rules"]!;
                    _msRules.RegisterWorkflowFromJson(rule.Name, rules.ToString(Formatting.None));
                    break;
            }
        }

        private void UnsyncFromEngines(NamedRule rule)
        {
            switch (rule.Kind)
            {
                case RuleKinds.Sql:
                    _sqlRules.RemoveRule(rule.Name);
                    break;
                case RuleKinds.MsRules:
                    _msRules.RemoveWorkflow(rule.Name);
                    break;
            }
        }
    }
}
