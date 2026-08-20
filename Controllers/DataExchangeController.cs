using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;
using StepFlow.DataModel.Entities;
using StepFlow.DataModel.Entities.DataSource;
using StepFunctionsApp.DataExchange;

namespace StepFunctionsApp.Controllers
{
    // +++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
    // DATA EXCHANGE CONTROLLER
    // REST surface for Data Exchange profiles (customer file -> internal schema
    // pipelines): profile CRUD, synchronous execution, and the execution monitor.
    // +++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++

    [ApiController]
    public class DataExchangeController : ControllerBase
    {
        private readonly DataExchangeProfileStore _profiles;
        private readonly DataExchangeExecutor _executor;
        private readonly DataExchangeExecutionLog _executions;

        public DataExchangeController(
            DataExchangeProfileStore profiles,
            DataExchangeExecutor executor,
            DataExchangeExecutionLog executions)
        {
            _profiles = profiles;
            _executor = executor;
            _executions = executions;
        }

        // List all profiles
        [HttpGet("api/data-exchange/profiles")]
        public IActionResult ListProfiles() => Ok(_profiles.LoadAll());

        // Get a single profile by id or name
        [HttpGet("api/data-exchange/profiles/{id}")]
        public IActionResult GetProfile(string id)
        {
            var profile = _profiles.Get(id);
            return profile == null ? NotFound() : Ok(profile);
        }

        // Save (create or update) a profile from its JSON document
        [HttpPost("api/data-exchange/profiles")]
        public IActionResult SaveProfile([FromBody] JObject payload)
        {
            DataExchangeProfile? profile;
            try
            {
                profile = payload.ToObject<DataExchangeProfile>();
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = $"Invalid profile document: {ex.Message}" });
            }

            if (profile == null || string.IsNullOrWhiteSpace(profile.DataExchangeProfileName))
                return BadRequest(new { error = "DataExchangeProfileName is required" });

            // Profiles created from the UI may omit the data source; default to a file medium so
            // ingestion falls back to input.filePath and the inbox monitor can pick it up.
            profile.DataSource ??= new DataSource { MediumType = DataSourceMediumType.File };

            var id = _profiles.Save(profile);
            return Ok(new { id });
        }

        // Delete a profile by id or name
        [HttpDelete("api/data-exchange/profiles/{id}")]
        public IActionResult DeleteProfile(string id) =>
            _profiles.Delete(id) ? Ok(new { deleted = true }) : NotFound();

        // Execute a profile synchronously. Body: { "profileId": "...", "input": { ... } }
        [HttpPost("api/data-exchange/execute")]
        public async Task<IActionResult> Execute([FromBody] JObject payload, CancellationToken ct)
        {
            var profileId = (string?)payload["profileId"] ?? (string?)payload["id"];
            if (string.IsNullOrWhiteSpace(profileId))
                return BadRequest(new { error = "profileId is required" });

            var input = payload["input"] as JObject ?? new JObject();
            try
            {
                var result = await _executor.ExecuteAsync(profileId, input, ct);
                _executions.Record(result);
                return Ok(result);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { error = ex.Message });
            }
        }

        // Recent execution artifacts (newest first)
        [HttpGet("api/data-exchange/executions")]
        public IActionResult ListExecutions([FromQuery] int limit = 50) => Ok(_executions.List(limit));

        // Single execution artifact by id
        [HttpGet("api/data-exchange/executions/{id}")]
        public IActionResult GetExecution(string id)
        {
            var result = _executions.Get(id);
            return result == null ? NotFound() : Ok(result);
        }
    }
}
