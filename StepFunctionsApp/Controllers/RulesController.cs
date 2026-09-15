using Microsoft.AspNetCore.Mvc;
using StepFunctionsApp.Rules;

namespace StepFunctionsApp.Controllers
{
    // ═══════════════════════════════════════════════════════════════════════════════
    // RULES — named rule artifacts (the project's rule catalog, rules.json).
    //   GET    /api/rules            → list all rules (name, kind, description)
    //   GET    /api/rules/{*name}    → full rule definition ({*name}: names may contain '/')
    //   POST   /api/rules            → upsert a rule { name, kind, description?, definition }
    //   DELETE /api/rules/{*name}    → delete a rule
    // sql and ms-rules kinds are (re)registered with their engines on save/delete, so
    // rule://<name> and rules://<name> flow resources work immediately.
    // ═══════════════════════════════════════════════════════════════════════════════

    [ApiController]
    [Route("api/rules")]
    public class RulesController : ControllerBase
    {
        private readonly NamedRuleManager _rules;

        public RulesController(NamedRuleManager rules) => _rules = rules;

        /// <summary>Lists every named rule artifact.</summary>
        [HttpGet]
        public IActionResult List() => Ok(_rules.List());

        /// <summary>Returns one full rule definition by name (404 when unknown).</summary>
        [HttpGet("{*name}")]
        public IActionResult Get(string name)
        {
            var rule = _rules.Get(name);
            return rule == null ? NotFound(new { error = $"Rule '{name}' not found." }) : Ok(rule);
        }

        /// <summary>Creates or replaces a rule by name and syncs its engine registration.</summary>
        [HttpPost]
        public IActionResult Save([FromBody] NamedRule rule)
        {
            try
            {
                return Ok(_rules.Save(rule));
            }
            catch (Exception ex) when (ex is ArgumentException or ArgumentNullException)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        /// <summary>Deletes a rule by name and unregisters it from its engine.</summary>
        [HttpDelete("{*name}")]
        public IActionResult Delete(string name)
        {
            var deleted = _rules.Delete(name);
            return deleted ? Ok(new { deleted = name }) : NotFound(new { error = $"Rule '{name}' not found." });
        }
    }
}
