using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;
using StepFunctionsApp.Forms;

namespace StepFunctionsApp.Controllers
{
    // ═══════════════════════════════════════════════════════════════════
    // FORM CAPTURE — runtime endpoints for suspended FormCapture states.
    // GET  api/form-captures/{taskId}          → form definition + bound attribute contract
    // POST api/form-captures/{taskId}/submit   → validate/coerce values, persist EAV row, resume
    // The standalone page wwwroot/form-capture.html is the only intended browser client; possession of
    // the taskId is the authorization (no auth exists anywhere in this app). Headless clients use the
    // same logic via MCP tools get_form_capture / submit_form_capture.
    // ═══════════════════════════════════════════════════════════════════

    [ApiController]
    public class FormCaptureController : ControllerBase
    {
        private readonly FormCaptureService _service;

        public FormCaptureController(FormCaptureService service)
        {
            _service = service;
        }

        // Form definition + bound attribute contract for the standalone page.
        [HttpGet("api/form-captures/{taskId}")]
        public async Task<IActionResult> Get(string taskId)
        {
            var (status, view, errorBody) = await _service.GetAsync(taskId);
            if (view != null) return Ok(view);
            return status switch
            {
                404 => NotFound(errorBody),
                _ => StatusCode(status, errorBody) // 500 etc.
            };
        }

        // Validate + coerce the submission against the bound domain's attribute contract,
        // persist an EAV row (bound forms only), then resume the suspended execution.
        [HttpPost("api/form-captures/{taskId}/submit")]
        public async Task<IActionResult> Submit(string taskId, [FromBody] JObject? body)
        {
            var (status, resultBody) = await _service.SubmitAsync(taskId, body);
            return status switch
            {
                200 => Ok(resultBody),
                400 => BadRequest(resultBody),
                404 => NotFound(resultBody),
                409 => Conflict(resultBody),
                _ => StatusCode(status, resultBody) // 500 etc.
            };
        }
    }
}
