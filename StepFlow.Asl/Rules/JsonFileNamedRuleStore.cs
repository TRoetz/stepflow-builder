using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace StepFunctionsApp.Rules
{
    /// <summary>
    /// File-backed named rule store: one JSON array at the configured path (rules.json by
    /// default). Lock + atomic rewrite, mirroring EavRowStore/EavRegistryService. Missing file
    /// ⇒ empty catalog; corrupt file ⇒ loud error on load (rules are config, not data to skip).
    /// </summary>
    public sealed class JsonFileNamedRuleStore : INamedRuleStore
    {
        private readonly object _lock = new();
        private string? _path;
        private Dictionary<string, NamedRule> _rules = new(StringComparer.OrdinalIgnoreCase);

        public void Initialize(string path)
        {
            lock (_lock)
            {
                _path = Path.GetFullPath(path);
                LoadFromDisk();
            }
        }

        public IReadOnlyList<NamedRule> List()
        {
            lock (_lock)
                return _rules.Values.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public NamedRule? Get(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            lock (_lock)
                return _rules.TryGetValue(name.Trim(), out var rule) ? Clone(rule) : null;
        }

        public NamedRule Save(NamedRule rule)
        {
            if (rule == null) throw new ArgumentNullException(nameof(rule));
            if (string.IsNullOrWhiteSpace(rule.Name)) throw new ArgumentException("rule.name is required.", nameof(rule));
            if (!RuleKinds.IsKnown(rule.Kind))
                throw new ArgumentException($"Unknown rule kind '{rule.Kind}'. Expected one of: {string.Join(", ", RuleKinds.All)}.", nameof(rule));
            if (rule.Definition == null || rule.Definition.Type == JTokenType.Null)
                throw new ArgumentException("rule.definition is required.", nameof(rule));

            lock (_lock)
            {
                var stored = Clone(rule);
                stored.UpdatedAtUtc = DateTime.UtcNow;
                _rules[stored.Name] = stored;
                SaveToDisk();
                return Clone(stored);
            }
        }

        public bool Delete(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            lock (_lock)
            {
                if (!_rules.Remove(name.Trim())) return false;
                SaveToDisk();
                return true;
            }
        }

        private void LoadFromDisk()
        {
            _rules = new Dictionary<string, NamedRule>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(_path) || !File.Exists(_path)) return;
            var json = File.ReadAllText(_path);
            if (string.IsNullOrWhiteSpace(json)) return;
            var list = JsonConvert.DeserializeObject<List<NamedRule>>(json) ?? new();
            foreach (var rule in list.Where(r => r != null && !string.IsNullOrWhiteSpace(r.Name)))
                _rules[rule.Name] = Clone(rule);
        }

        private void SaveToDisk()
        {
            if (string.IsNullOrEmpty(_path)) return;
            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var temp = _path + ".tmp";
            File.WriteAllText(temp, JsonConvert.SerializeObject(
                _rules.Values.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList(), Formatting.Indented));
            File.Move(temp, _path, overwrite: true);
        }

        /// <summary>Detaches callers from the live instance so in-place edits can't bypass persistence.</summary>
        private static NamedRule Clone(NamedRule rule) => new()
        {
            Name = rule.Name,
            Kind = rule.Kind,
            Description = rule.Description,
            Definition = rule.Definition?.DeepClone() ?? new JObject(),
            UpdatedAtUtc = rule.UpdatedAtUtc
        };
    }
}
