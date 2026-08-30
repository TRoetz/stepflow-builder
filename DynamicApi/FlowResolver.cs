using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using StepFunctionsApp.StepFunctions;
using StepFunctionsApp.Workspace;

namespace StepFunctionsApp.DynamicApi;

// +++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
// FLOW RESOLVER - shared flow id/name resolution for the dynamic API dispatcher and
// the /api/flows/execute-sync endpoint: in-memory registry first, then workspace
// sub-project flow files (registered on the fly so a fresh backend can serve flows
// that were never explicitly registered).
// +++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++

public sealed class FlowResolver
{
    private readonly StepFunctionService _stepService;
    private readonly WorkspaceStore _workspace;
    private readonly ILogger<FlowResolver> _logger;

    public FlowResolver(StepFunctionService stepService, WorkspaceStore workspace, ILogger<FlowResolver>? logger = null)
    {
        _stepService = stepService;
        _workspace = workspace;
        _logger = logger ?? NullLogger<FlowResolver>.Instance;
    }

    /// <summary>Resolves a flow id/name: in-memory registry first, then workspace sub-project flow files (registered on the fly).</summary>
    public StateMachineDefinition? ResolveFlow(string flowIdOrName)
    {
        var sm = _stepService.GetStateMachine(flowIdOrName);
        if (sm != null) return sm.Definition;

        foreach (var sub in _workspace.ListAllSubProjects())
        {
            if (_workspace.LoadFlowDefinition(sub, flowIdOrName) is not { Length: > 0 } json) continue;
            try
            {
                var doc = JObject.Parse(json);
                var statesObj = doc["states"] as JObject;
                if (statesObj == null || !statesObj.HasValues) continue;

                var startAt = doc["startAt"]?.ToString() ?? statesObj.Properties().FirstOrDefault()?.Name ?? "";
                var meta = _workspace.ListFlows(sub).FirstOrDefault(f => f.Id == flowIdOrName).Meta; // may be default when missing — guard null
                var def = new StateMachineDefinition { StartAt = startAt, States = statesObj.ToObject<Dictionary<string, StateDefinition>>()! };
                _stepService.RegisterStateMachine(meta?.Name ?? flowIdOrName, def, meta?.Description, id: flowIdOrName);
                return def;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Skipping workspace flow '{Flow}' in sub-project '{Sub}' during dynamic API resolution", flowIdOrName, sub);
            }
        }
        return null;
    }
}
