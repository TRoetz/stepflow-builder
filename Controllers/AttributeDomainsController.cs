using System;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using StepFunctionsApp.StepFunctions;

namespace StepFunctionsApp.Controllers
{
    // ═══════════════════════════════════════════════════════════════════════════════
    // ATTRIBUTE DOMAINS — CRUD over IAttributeDomainStore (the attribute-contract side
    // of the builder). Bodies use the flat AttributeDomainEntry JSON shape shared with
    // the registry file; responses never expose the MetaData POCOs' EF navigation props.
    // ═══════════════════════════════════════════════════════════════════════════════

    [ApiController]
    public class AttributeDomainsController : ControllerBase
    {
        private readonly IAttributeDomainStore _store;
        private readonly ISchemaDefinitionStore _schemaStore;

        public AttributeDomainsController(IAttributeDomainStore store, ISchemaDefinitionStore schemaStore)
        {
            _store = store;
            _schemaStore = schemaStore;
        }

        // All domains as registry entries (with their optional linked schema definition).
        [HttpGet("api/attribute-domains")]
        public IActionResult List() => Ok(_store.GetAll().Select(d =>
            AttributeDomainMapping.FromPoco(d, _store.GetByName(d.AttributeDomainName).schema)));

        // A single domain by name (with its optional linked schema definition).
        [HttpGet("api/attribute-domains/{name}")]
        public IActionResult GetByName(string name)
        {
            var (domain, schema) = _store.GetByName(name);
            if (domain == null) return NotFound(new { error = $"Attribute domain '{name}' not found" });
            return Ok(AttributeDomainMapping.FromPoco(domain, schema)); // same shape as list items and the dispatcher's GET
        }

        // Create or replace a domain by name (body: AttributeDomainEntry JSON).
        [HttpPost("api/attribute-domains")]
        public IActionResult Save([FromBody] AttributeDomainEntry entry)
        {
            if (entry?.AttributeDomain == null || string.IsNullOrWhiteSpace(entry.AttributeDomain.AttributeDomainName))
                return BadRequest(new { error = "Body must include attributeDomain.attributeDomainName" });

            try
            {
                var (domain, schema) = AttributeDomainMapping.ToPoco(entry);
                if (schema != null && _schemaStore.Get(schema.SchemaDefinitionName, schema.Version) == null)
                    return BadRequest(new { error = $"Schema definition '{schema.SchemaDefinitionName}' v'{schema.Version}' not found — create it in the Schemas tab first" });
                _store.Save(domain, schema);
                return Ok(new { status = "saved", domainName = domain.AttributeDomainName });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        // Delete a domain by name.
        [HttpDelete("api/attribute-domains/{name}")]
        public IActionResult Delete(string name) =>
            _store.Delete(name) ? Ok(new { status = "deleted", domainName = name }) : NotFound(new { error = $"Attribute domain '{name}' not found" });
    }
}
