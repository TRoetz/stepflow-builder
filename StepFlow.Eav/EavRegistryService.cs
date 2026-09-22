using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace StepFunctionsApp.StepFunctions
{
    // ═══════════════════════════════════════════════════════════════════════════════
    // EAV REGISTRY SERVICE
    // Maintains data contracts, maps dynamic JSON to strict dictionaries, 
    // and persists schema definitions to disk.
    // ═══════════════════════════════════════════════════════════════════════════════

    public class EavRegistryService
    {
        private Dictionary<string, EavEntityDefinition> _registry = new(StringComparer.OrdinalIgnoreCase);
        private string? _persistencePath;

        public void Initialize(string path)
        {
            _persistencePath = path;
            LoadFromDisk();
        }

        public IEnumerable<EavEntityDefinition> GetAllEntities() => _registry.Values.OrderBy(e => e.EntityName);

        public void RegisterEntity(EavEntityDefinition entity)
        {
            _registry[entity.EntityName] = entity;
            SaveToDisk();
        }

        public void DeleteEntity(string entityName)
        {
            if (_registry.Remove(entityName))
            {
                SaveToDisk();
            }
        }

        public EavEntityDefinition? GetEntity(string entityName) => 
            _registry.TryGetValue(entityName, out var e) ? e : null;

        // Transforms raw StepFlow JToken into a strict EAV Dictionary (delegates to the shared EavMapper).
        public Dictionary<string, object> MapPayloadToEav(string entityName, JToken payload)
        {
            var entity = GetEntity(entityName) ?? throw new ArgumentException($"EAV Entity '{entityName}' not found.");
            return EavMapper.Map(entity, payload);
        }

        private void SaveToDisk()
        {
            if (string.IsNullOrEmpty(_persistencePath)) return;
            var json = JsonConvert.SerializeObject(_registry.Values, Formatting.Indented);
            File.WriteAllText(_persistencePath, json);
        }

        private void LoadFromDisk()
        {
            if (string.IsNullOrEmpty(_persistencePath) || !File.Exists(_persistencePath)) return;
            try
            {
                var json = File.ReadAllText(_persistencePath);
                var entities = JsonConvert.DeserializeObject<List<EavEntityDefinition>>(json) ?? new();
                _registry = entities.ToDictionary(e => e.EntityName, StringComparer.OrdinalIgnoreCase);
            }
            catch { /* Fallback to empty if corrupted */ }
        }

    }
}
