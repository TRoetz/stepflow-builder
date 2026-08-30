using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;
using StepFunctionsApp.StepFunctions;

namespace StepFunctionsApp.Controllers
{
    // ═══════════════════════════════════════════════════════════════════════════════
    // EAV ROWS — REST surface for captured attribute rows (eav-data/{domain}.json).
    // Wire shape is identical to the dynamic API dispatcher's eav handler so external
    // Dynamic API hosts can forward requests here unchanged. Domain names are validated
    // by the store (SafeDomainRegex); unsafe ones → 400 { error }.
    // ═══════════════════════════════════════════════════════════════════════════════

    [ApiController]
    public class EavRowsController : ControllerBase
    {
        private readonly EavRowStore _eavRows;

        public EavRowsController(EavRowStore eavRows)
        {
            _eavRows = eavRows;
        }

        // All rows for a domain with the full query language (filter/sort/page/limit/offset/fields - see EavQuery).
        [HttpGet("api/eav/{domain}/rows")]
        public IActionResult List(string domain)
        {
            try
            {
                var options = EavQuery.Parse(Request.Query);
                var (page, total) = EavQuery.Apply(_eavRows.ListRows(domain), options);
                return Ok(new JObject { ["rows"] = new JArray(page), ["count"] = total });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        // Single row by EntityId (falls back to RowKeyId): 404 when nothing matches, an object for a unique match,
        // an array when the entityId matched several rows. Only 'fields' is honored from the query string.
        [HttpGet("api/eav/{domain}/rows/{id}")]
        public IActionResult Get(string domain, string id)
        {
            try
            {
                var options = EavQuery.Parse(Request.Query);
                var matches = EavQuery.Lookup(_eavRows.ListRows(domain), id).Select(r => EavQuery.Shape(r, options.Fields)).ToArray();
                if (matches.Length == 0) return NotFound(new { error = $"Row '{id}' not found" });
                return Ok(matches.Length == 1 ? matches[0] : new JArray(matches));
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        // Append a row: top-level entityId/entityType become row fields; everything else is captured values.
        [HttpPost("api/eav/{domain}/rows")]
        public IActionResult Create(string domain, [FromBody] JObject body)
        {
            if (body == null || body.Type != JTokenType.Object) return BadRequest(new { error = "Body must be a JSON object" });
            try
            {
                var row = new EavRow();
                foreach (var prop in body.Properties())
                {
                    switch (prop.Name.ToLowerInvariant())
                    {
                        case "entityid": row.EntityId = prop.Value.Type == JTokenType.Null ? null : prop.Value; break;
                        case "entitytype": row.EntityType = prop.Value.ToString(); break;
                        default: row.Values[prop.Name] = prop.Value; break;
                    }
                }
                _eavRows.AppendRow(domain, row); // assigns RowKeyId when empty
                return StatusCode(StatusCodes.Status201Created, row);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        // Replace a row's values wholesale by row key.
        [HttpPut("api/eav/{domain}/rows/{rowKeyId}")]
        public IActionResult Update(string domain, string rowKeyId, [FromBody] JObject body)
        {
            if (body == null || body.Type != JTokenType.Object) return BadRequest(new { error = "Body must be a JSON object" });
            try
            {
                if (!_eavRows.UpdateRow(domain, rowKeyId, body))
                    return NotFound(new { error = $"Row '{rowKeyId}' not found" });
                var updated = _eavRows.ListRows(domain).FirstOrDefault(r => string.Equals(r.RowKeyId, rowKeyId, StringComparison.OrdinalIgnoreCase));
                return Ok(updated);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        // Merge a patch into an existing row's values (existing keys overwritten).
        [HttpPatch("api/eav/{domain}/rows/{rowKeyId}")]
        public IActionResult Patch(string domain, string rowKeyId, [FromBody] JObject body)
        {
            if (body == null || body.Type != JTokenType.Object) return BadRequest(new { error = "Body must be a JSON object" });
            try
            {
                if (!_eavRows.PatchRow(domain, rowKeyId, body))
                    return NotFound(new { error = $"Row '{rowKeyId}' not found" });
                var updated = _eavRows.ListRows(domain).FirstOrDefault(r => string.Equals(r.RowKeyId, rowKeyId, StringComparison.OrdinalIgnoreCase));
                return Ok(updated);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        // Remove a row by key.
        [HttpDelete("api/eav/{domain}/rows/{rowKeyId}")]
        public IActionResult Delete(string domain, string rowKeyId)
        {
            try
            {
                if (!_eavRows.RemoveRow(domain, rowKeyId))
                    return NotFound(new { error = $"Row '{rowKeyId}' not found" });
                return Ok(new { status = "deleted", rowKeyId });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }
    }
}
