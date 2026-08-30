using Microsoft.AspNetCore.Mvc;
using StepFunctionsApp.StepFunctions;

namespace StepFunctionsApp.Controllers
{
    // ═══════════════════════════════════════════════════════════════════════════════
    // SCHEMA DEFINITIONS — CRUD over ISchemaDefinitionStore (the versioned schema
    // registry). Deleting a version that an attribute domain still references is
    // rejected with 409; validation lives in the store implementations.
    // ═══════════════════════════════════════════════════════════════════════════════

    [ApiController]
    public class SchemaDefinitionsController : ControllerBase
    {
        private readonly ISchemaDefinitionStore _store;
        private readonly IAttributeDomainStore _domainStore;

        public SchemaDefinitionsController(ISchemaDefinitionStore store, IAttributeDomainStore domainStore)
        {
            _store = store;
            _domainStore = domainStore;
        }

        // All saved versions of every schema (the UI groups by name).
        [HttpGet("api/schema-definitions")]
        public IActionResult List() => Ok(_store.GetAll());

        // Create or replace one (name, version) pair (body: SchemaDefinition JSON incl. Definition body).
        [HttpPost("api/schema-definitions")]
        public IActionResult Save([FromBody] SchemaDefinition def)
        {
            try
            {
                _store.Save(def);
                return Ok(new { status = "saved", name = def.SchemaDefinitionName, version = def.Version });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        // Delete one saved version; 409 when an attribute domain still references it.
        [HttpDelete("api/schema-definitions/{name}/{version}")]
        public IActionResult Delete(string name, string version)
        {
            var referencedBy = _domainStore.GetAll()
                .Where(d => d.SchemaDefinition != null &&
                            string.Equals(d.SchemaDefinition.SchemaDefinitionName, name, StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(d.SchemaDefinition.Version ?? "1", version, StringComparison.Ordinal))
                .Select(d => d.AttributeDomainName)
                .ToList();
            if (referencedBy.Count > 0)
                return Conflict(new
                {
                    error = $"'{name}' v'{version}' is referenced by attribute domain(s): {string.Join(", ", referencedBy)}",
                    domains = referencedBy
                });

            return _store.Delete(name, version)
                ? Ok(new { status = "deleted", name, version })
                : NotFound(new { error = $"Schema definition '{name}' v'{version}' not found" });
        }
    }
}
