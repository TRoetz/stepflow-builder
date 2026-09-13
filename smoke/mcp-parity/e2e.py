#!/usr/bin/env python3
"""MCP-created flow -> API + UI compatibility end-to-end check.

Requires the live backend on :5001 (start.ps1 or `dotnet run --project StepFunctionsApp`).

Phases:
  MCP   - initialize session, list tools, save_flow(mcp-ui-parity), list_flows, get_flow, run_flow
  REST  - GET /api/flows lists it, GET definition round-trips, execute-sync reproduces the output
  UI    - fetches the same definition the React canvas imports (flowService.ts importFlow path)

Exit code 0 = all assertions passed.
"""
import json
import sys
import urllib.request

BASE = "http://localhost:5001"
MCP_URL = BASE + "/mcp"
FLOW_NAME = "mcp-ui-parity"

STATES_JSON = json.dumps({
    "Greet": {
        "type": "Task",
        "resource": "internal://echo",
        "parameters": {"greeting": "hello from MCP", "source": FLOW_NAME},
        "next": "Summarize",
    },
    "Summarize": {
        "type": "Task",
        "resource": "transform://jsonata",
        "parameters": {
            "expression": "{ \"source\": $.source, \"message\": $.greeting & ' - transformed' }",
            "input_data.$": "$",
        },
        "end": True,
    },
})

EXPECTED_OUTPUT = {"source": FLOW_NAME, "message": "hello from MCP - transformed"}

failures = []


def check(label, cond, detail=""):
    status = "PASS" if cond else "FAIL"
    print(f"[{status}] {label}" + (f"  -- {detail}" if detail and not cond else ""))
    if not cond:
        failures.append(label)


# ── minimal MCP streamable-HTTP client ────────────────────────────────────────

class McpClient:
    def __init__(self, url):
        self.url = url
        self.session_id = None
        self._id = 0

    def _post(self, payload, expect_response=True):
        body = json.dumps(payload).encode()
        req = urllib.request.Request(
            self.url, data=body, method="POST",
            headers={
                "Content-Type": "application/json",
                "Accept": "application/json, text/event-stream",
                **({"mcp-session-id": self.session_id} if self.session_id else {}),
            },
        )
        with urllib.request.urlopen(req, timeout=60) as resp:
            sid = resp.headers.get("mcp-session-id")
            if sid:
                self.session_id = sid
            ctype = resp.headers.get("Content-Type", "")
            raw = resp.read().decode()
        if not expect_response:
            return None
        if "text/event-stream" in ctype:
            for line in raw.splitlines():
                if line.startswith("data:"):
                    return json.loads(line[5:].strip())
            raise RuntimeError(f"SSE response without data: {raw[:300]}")
        return json.loads(raw)

    def call(self, method, params=None):
        self._id += 1
        msg = {"jsonrpc": "2.0", "id": self._id, "method": method}
        if params is not None:
            msg["params"] = params
        resp = self._post(msg)
        if resp.get("error"):
            raise RuntimeError(f"{method}: {resp['error']}")
        return resp.get("result", {})

    def notify(self, method):
        self._post({"jsonrpc": "2.0", "method": method}, expect_response=False)


def tool_result_text(result):
    """MCP tools/call result -> text content string."""
    for c in result.get("content", []):
        if c.get("type") == "text":
            return c["text"]
    raise RuntimeError(f"no text content: {json.dumps(result)[:300]}")


def http_json(method, path, body=None):
    data = json.dumps(body).encode() if body is not None else None
    req = urllib.request.Request(
        BASE + path, data=data, method=method,
        headers={"Content-Type": "application/json", **({"Accept": "application/json"} if method == "GET" else {})},
    )
    with urllib.request.urlopen(req, timeout=60) as resp:
        return json.loads(resp.read().decode())


# ── Phase 1: MCP ──────────────────────────────────────────────────────────────

print("== Phase 1: MCP ==")
mcp = McpClient(MCP_URL)
init = mcp.call("initialize", {
    "protocolVersion": "2025-03-26",
    "capabilities": {},
    "clientInfo": {"name": "stepflow-e2e", "version": "1.0"},
})
check("MCP initialize", init.get("serverInfo", {}).get("name") == "StepFunctionsApp", json.dumps(init)[:200])
mcp.notify("notifications/initialized")

tools = mcp.call("tools/list").get("tools", [])
tool_names = {t["name"] for t in tools}
for needed in ("list_flows", "get_flow", "save_flow", "run_flow"):
    check(f"MCP tool present: {needed}", needed in tool_names, str(sorted(tool_names)))

saved = json.loads(tool_result_text(mcp.call("tools/call", {"name": "save_flow", "arguments": {
    "name": FLOW_NAME,
    "statesJson": STATES_JSON,
    "startAt": "Greet",
    "description": "MCP-created parity flow: echo + jsonata transform (E2E UI compatibility)",
}})))
check("save_flow returns id", bool(saved.get("id")), json.dumps(saved)[:300])
flow_id = saved.get("id")

listed = json.loads(tool_result_text(mcp.call("tools/call", {"name": "list_flows", "arguments": {}})))
by_name = {f.get("name"): f for f in listed} if isinstance(listed, list) else {}
check("list_flows contains mcp-ui-parity", FLOW_NAME in by_name, json.dumps(listed)[:300])

got = json.loads(tool_result_text(mcp.call("tools/call", {"name": "get_flow", "arguments": {"idOrName": FLOW_NAME}})))
defn = got.get("definition") or {}
states = defn.get("states") or {}
check("get_flow round-trips 2 states", set(states) == {"Greet", "Summarize"}, json.dumps(got)[:300])
check("get_flow startAt=Greet", defn.get("startAt") == "Greet", str(defn.get("startAt")))

ran = json.loads(tool_result_text(mcp.call("tools/call", {"name": "run_flow", "arguments": {
    "idOrName": FLOW_NAME, "inputJson": "{}"}})))
check("run_flow Succeeded", ran.get("status") == "Succeeded", json.dumps(ran)[:300])
mcp_output = ran.get("output")
check("run_flow output matches expected", mcp_output == EXPECTED_OUTPUT, json.dumps(mcp_output)[:300])

# ── Phase 2: REST API visibility ──────────────────────────────────────────────

print("\n== Phase 2: REST API ==")
flows = http_json("GET", "/api/flows")
rest_by_name = {f.get("name"): f for f in flows} if isinstance(flows, list) else {}
check("GET /api/flows lists mcp-ui-parity", FLOW_NAME in rest_by_name, json.dumps(flows)[:400])
rest_id = (rest_by_name.get(FLOW_NAME) or {}).get("id")
check("REST id matches MCP save_flow id", rest_id == flow_id, f"mcp={flow_id} rest={rest_id}")

definition = http_json("GET", f"/api/flows/{rest_id}/definition")
d_states = (definition.get("states") or {}) if isinstance(definition, dict) else {}
check("REST definition has both states", set(d_states) == {"Greet", "Summarize"}, json.dumps(definition)[:300])
check("REST Greet resource is internal://echo", d_states.get("Greet", {}).get("resource") == "internal://echo")

sync = http_json("POST", f"/api/flows/execute-sync/{rest_id}", {})
check("execute-sync Succeeded", sync.get("status") == "Succeeded", json.dumps(sync)[:300])
check("execute-sync output matches MCP run_flow output", sync.get("output") == EXPECTED_OUTPUT, json.dumps(sync.get("output"))[:300])

# ── Phase 3: UI compatibility (same payload the canvas import consumes) ───────

print("\n== Phase 3: UI compatibility ==")
# flowService.ts importFlow() accepts { startAt, states } camelCase ASL — exactly what
# GET /api/flows/{id}/definition returns. Validate the shape the canvas compiler needs:
check("UI payload has startAt", isinstance(definition.get("startAt"), str))
ok_states = all(
    isinstance(s, dict) and s.get("type") in ("Task", "Pass", "Choice", "Wait", "HumanTask", "FormCapture", "Succeed", "Fail", "Parallel", "Map")
    for s in d_states.values()
)
check("UI payload states are canvas-compilable types", ok_states, json.dumps(d_states)[:300])
edges = [n for n, s in d_states.items() if isinstance(s.get("next"), str)]
check("UI payload has a renderable edge (Greet->Summarize)", "Greet" in edges and d_states["Greet"]["next"] == "Summarize")

# ── Summary ───────────────────────────────────────────────────────────────────
print()
if failures:
    print(f"E2E FAILED ({len(failures)}): {failures}")
    sys.exit(1)
print("E2E PASSED: MCP-created flow is visible via API and UI-compatible; execution output verified.")
