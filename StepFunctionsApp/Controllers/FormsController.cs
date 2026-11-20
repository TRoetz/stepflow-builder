using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using StepFunctionsApp.StepFunctions;

namespace StepFunctionsApp.Controllers
{
    // ═══════════════════════════════════════════════════════════════════════════════
    // FORMS — CRUD over IFormDefinitionStore (the form side of the builder). Thin
    // wrappers: validation lives in the store implementations.
    // ═══════════════════════════════════════════════════════════════════════════════

    [ApiController]
    public class FormsController : ControllerBase
    {
        private readonly IFormDefinitionStore _store;

        public FormsController(IFormDefinitionStore store)
        {
            _store = store;
        }

        // All form definitions (every saved version; the UI groups by FormId).
        [HttpGet("api/forms")]
        public IActionResult List() => Ok(_store.GetAll());

        // The active storage provider name ("json" | "sqlite").
        [HttpGet("api/forms/provider")]
        public IActionResult Provider() => Ok(new { provider = _store.ProviderName });

        // One saved form definition — the current version, or an exact (formId, version) pair when ?version= is given.
        [HttpGet("api/forms/{formId}")]
        public IActionResult Get(string formId, [FromQuery] string? version)
        {
            try
            {
                var def = string.IsNullOrWhiteSpace(version) ? _store.Get(formId) : _store.Get(formId, version);
                return def is null
                    ? NotFound(new { error = $"Form '{formId}' not found" })
                    : Ok(def);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { error = ex.Message }); // invalid form id / version label
            }
        }

        // Create or replace a form by FormId (body: full FormDefinition JSON incl. Page).
        [HttpPost("api/forms")]
        public IActionResult Save([FromBody] FormDefinition def)
        {
            try
            {
                _store.Save(def);
                return Ok(new { status = "saved", formId = def.FormId, version = def.Version });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        // Delete a form by id.
        [HttpDelete("api/forms/{formId}")]
        public IActionResult Delete(string formId) =>
            _store.Delete(formId) ? Ok(new { status = "deleted", formId }) : NotFound(new { error = $"Form '{formId}' not found" });
    }
}
