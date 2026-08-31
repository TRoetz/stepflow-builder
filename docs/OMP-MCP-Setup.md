# Connecting the OMP (Oh My Pi) Harness to the StepFlow MCP Server

This manual covers connecting an **OMP** coding-agent session to the StepFlow backend's
Model Context Protocol server, so the agent can list, create, inspect and run flows
directly through its toolset.

```
┌─────────────┐   Streamable HTTP (JSON-RPC over POST /mcp)   ┌──────────────────────────┐
│  OMP agent  │ ────────────────────────────────────────────► │ StepFunctionsApp backend │
│  (client)   │ ◄──────────────────────────────────────────── │ management port :5001    │
└─────────────┘        SSE-framed JSON-RPC responses          └──────────────────────────┘
```

- **Server**: `StepFunctionsApp` exposes `/mcp` on its *management port* (default **5001**)
  via the .NET ModelContextProtocol SDK (`endpoints.MapMcp("/mcp")` in `Startup.Configure`).
- **Client**: OMP discovers MCP servers from config files, connects at session start, and
  exposes each server tool as a first-class agent tool named `mcp__<server>_<tool>`.

---

## 1. Prerequisites

The backend must be running before OMP can connect (OMP retries/reconnects automatically if
it comes up later — see §7).

```powershell
# Option A: full stack (backend :5001 + fake test host :5095 + UI :3001)
.\start.ps1

# Option B: backend only (enough for MCP)
dotnet run --project StepFunctionsApp
```

Verify the MCP endpoint is live **without** OMP, using plain curl:

```bash
curl -s http://localhost:5001/mcp ^
  -H "Content-Type: application/json" ^
  -H "Accept: application/json, text/event-stream" ^
  -d "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/list\"}"
```

Expected: an SSE-framed response (`event: message` / `data: {"result":{"tools":[...]}}`)
listing four tools: `list_flows`, `save_flow`, `get_flow`, `run_flow`.

> **Port note.** The management port defaults to 5001 (`DynamicApi:ManagementPort` in
> `StepFunctionsApp/appsettings.json`). If you run the backend on a different port (e.g.
> because another app holds 5001), point the OMP config at that port instead — see §2.3.

---

## 2. Add the server to OMP's configuration

### 2.1 Project-level config (recommended)

OMP-native project config lives in **`.omp/mcp.json`** at the repo root. This file is
already present in this repository:

```json
{
  "$schema": "https://raw.githubusercontent.com/can1357/oh-my-pi/main/packages/coding-agent/src/config/mcp-schema.json",
  "mcpServers": {
    "stepflow": {
      "type": "http",
      "url": "http://localhost:5001/mcp",
      "timeout": 120000
    }
  }
}
```

Field notes:

| Field | Meaning |
|---|---|
| `type: "http"` | Streamable HTTP transport (the MCP spec's current remote transport). **Required** — omitting it makes OMP treat the entry as a stdio server and fail validation. |
| `url` | The `/mcp` endpoint on the backend management port. |
| `timeout: 120000` | Per-request timeout in ms (default is 30 000). `run_flow` executes synchronously, so long flows need headroom; set `0` to disable client-side timeouts entirely. Can also be overridden process-wide with the env var `OMP_MCP_TIMEOUT_MS`. |
| `$schema` | Optional; gives editors autocomplete/validation. OMP writes it automatically when it manages the file. |

No `headers` are needed: the endpoint has no authentication (it is a local development
surface — see §8).

### 2.2 Alternative config locations

OMP discovers MCP servers from several places, in descending priority (first definition
wins; duplicates are **not** merged):

1. OMP-native project: `.omp/mcp.json` ← *used here*
2. OMP extension packages
3. Imported tool configs (`~/.claude.json`, `~/.codex/config.toml`, `~/.gemini/settings.json`,
   `opencode.json`, `~/.cursor/mcp.json`, `~/.codeium/windsurf/mcp_config.json`, `.vscode/mcp.json`)
4. OMP-native user: `~/.omp/agent/mcp.json` (or `~/.omp/profiles/<name>/agent/mcp.json`
   when a named profile is active)
5. Root fallback files: `mcp.json`, then `.mcp.json` in the project root

Use the **user** file if you want StepFlow available from every checkout; use the **project**
file (as here) to keep it scoped to this repo and shareable with teammates. If a same-named
server exists in another tool's config, either rename one or suppress it via `disabledServers`
in your user file:

```json
{ "mcpServers": { }, "disabledServers": ["stepflow"] }
```

### 2.3 Alternate ports / environment-driven URLs

OMP expands `${VAR}` and `${VAR:-default}` placeholders in config strings at discovery time,
so a portable URL is possible if you sometimes run the backend on a non-default port:

```json
"mcpServers": {
  "stepflow": {
    "type": "http",
    "url": "http://localhost:${STEPFLOW_PORT:-5001}/mcp"
  }
}
```

Unset `STEPFLOW_PORT` → resolves to 5001. (Header values support the same expansion plus a
`!command` indirection for secrets — not needed here since there is no auth.)

### 2.4 Using the `/mcp add` wizard instead of editing JSON

Inside an OMP session you can also run:

```
/mcp add stepflow http http://localhost:5001/mcp
```

The wizard writes the same entry into an OMP-managed file (project `.omp/mcp.json` when you
choose project scope). Direct JSON editing is fine whenever you need fields the wizard does
not prompt for.

---

## 3. Connect and verify in OMP

1. **Start (or restart) OMP from the repo root** so it discovers `.omp/mcp.json`:

   ```powershell
   cd C:\Source\stepflow-builder
   omp
   ```

   MCP discovery runs at session start; for interactive sessions the connect happens in the
   background right after the session is live (a slow server never blocks startup — tools are
   registered late if needed).

2. **If you edited `mcp.json` while a session was already running**, reload:

   ```
   /mcp reload
   ```

3. **Check discovery and connection state:**

   ```
   /mcp list          # shows each server, its status (connected/connecting/disconnected) and source file
   /mcp test stepflow # one-shot connect + tools/list against just this server
   ```

4. **Confirm the tools are in the agent's toolset.** With a healthy connection you get four
   tools (server/tool names lowercased, sanitized to letters/underscores):

   | OMP tool name | StepFlow MCP tool |
   |---|---|
   | `mcp__stepflow_list_flows` | `list_flows` |
   | `mcp__stepflow_get_flow` | `get_flow` |
   | `mcp__stepflow_save_flow` | `save_flow` |
   | `mcp__stepflow_run_flow` | `run_flow` |

5. **Smoke-test through the agent** (plain natural language is enough):

   > "Use the stepflow MCP tools: list the registered flows, then run a quick echo test."

   The agent should call `mcp__stepflow_list_flows`, and you can ask it to save + run the
   canonical smoke flow:

   ```json
   // statesJson for an internal://echo round-trip (no external dependencies)
   {"Start":{"type":"Pass","next":"Echo"},
    "Echo":{"type":"Task","resource":"internal://echo","parameters":{"note":"hello"}},
    "Done":{"type":"Succeed"}}
   ```

   `run_flow` should return `"status": "Succeeded"` with the echo in `output`.

---

## 4. Tool reference (what the agent can do)

All four tools return **JSON strings**. Failures are returned as tool results of the form
`{"error": "<message>"}` — *not* protocol-level errors — so the agent can read and react to
them (e.g. "flow not found, call list_flows").

### `list_flows()`
No parameters. Returns an array:

```json
[ { "id": "db52fed3a53a", "name": "csv-to-api-form-capture",
    "description": "...", "updatedAt": "2026-08-31T08:18:54Z" } ]
```

### `get_flow(idOrName)`
`idOrName`: flow **id or name**. Returns metadata plus the full Amazon States Language
definition as camelCase JSON (the same shape the React UI imports):

```json
{ "id": "...", "name": "...", "description": "...",
  "createdAt": "...", "updatedAt": "...", "definition": { "startAt": "...", "states": { } } }
```

### `save_flow(name, statesJson, startAt?, description?)`
Upsert by name — saving again with the same name **replaces** that flow's definition.

| Param | Type | Notes |
|---|---|---|
| `name` | string (required) | Unique flow name; stable names are upserted, not duplicated. |
| `statesJson` | string (required) | The ASL **states object** as a JSON *string*: `{ "<stateName>": { "type": "...", ... }, ... }`. Every non-terminal state needs `"next"`; terminal states omit it or set `"end": true`. |
| `startAt` | string? | First state name. Defaults to the first key in `statesJson`; validated against the keys. |
| `description` | string? | Human-readable description stored with the flow. |

Returns `{ "id", "name", "updatedAt" }`. The engine's ASL dialect supports the state types
`Task`, `Pass`, `Choice`, `Wait`, `Parallel`, `Map`, `Succeed`, `Fail` and task resource
schemes `http(s)://…`, `transform://query|filter|project|aggregate|sort|lookup|schema|sample|execute|javascript|python|powershell|csharp|shell`,
`rule://<RuleId>`, `rules://<workflowName>`, `ai://<service>`, `flow://<flowIdOrName>`,
`internal://echo`. The full node catalog and worked examples live in
[`StepFlow_Usage_Guide.md`](./StepFlow_Usage_Guide.md).

### `run_flow(idOrName, inputJson?)`
Runs the flow **synchronously** on the engine.

| Param | Type | Notes |
|---|---|---|
| `idOrName` | string (required) | Flow id or name. |
| `inputJson` | string? | Execution input as a JSON object *string*, e.g. `{"orderId": 42}`. Defaults to `{}`. |

Returns:

```json
{ "executionId": "02dc6398", "status": "Succeeded",
  "output": { }, "errorCode": null, "errorMessage": null,
  "history": [ { "type": "StateEntered", "state": "...", "data": "..." } ] }
```

**Human-gated flows.** If the flow reaches a `HumanTask`/FormCapture state, execution
suspends and `run_flow` returns `"status": "Suspended"` (with the task id in the history).
MCP has **no resume tool** — completion happens out-of-band:

- UI form page: open `/form-capture.html?taskId=<id>` on the backend port, fill and submit; or
- REST: `POST /api/form-captures/<taskId>/submit` with the captured values as JSON body; or
- file drop: write a completion JSON into the `human-task-completions/` directory.

The engine auto-resumes the suspended execution (also on backend restart, via
`FlowState.AutoResume`).

---

## 5. Transport details — why this pairing works

Verified against the running server (`curl -si`):

| Behavior | StepFlow server | OMP client handling |
|---|---|---|
| Request path | `POST /mcp`, JSON-RPC body, requires `Accept: application/json, text/event-stream` | Streamable HTTP transport POSTs exactly this. |
| Response framing | Always SSE-framed: `Content-Type: text/event-stream`, `event: message` + `data: {…}` | OMP's per-request SSE path consumes the stream until it finds the response matching its request id — fully supported. |
| Server-push stream (`GET /mcp`) | **405 Method Not Allowed** (POST only; StepFlow emits no server-initiated notifications) | OMP starts an optional background GET listener after `initialize`; on 405 it silently disables itself. No impact. |
| Session tracking | Stateless — responses carry no `Mcp-Session-Id` header | OMP tracks the session id only when present; each POST is independent here. |
| Handshake | Standard MCP `initialize` / `notifications/initialized`, answers `ping` and `roots/list` | OMP performs this handshake automatically (protocol version 2025-11-25, advertises `roots`). |

---

## 6. Timeouts and long-running flows

Per-request timeout precedence: **`OMP_MCP_TIMEOUT_MS` env var → per-server `timeout` →
30 000 ms default**; `0` disables the client-side timeout.

- `list_flows`, `get_flow`, `save_flow` return in milliseconds — the default is plenty.
- `run_flow` blocks until the flow finishes (or suspends). Flows with long `Wait` states,
  slow HTTP tasks, or large DataExchange imports can exceed 30 s. The project config sets
  `timeout: 120000` for this reason; raise it or set `OMP_MCP_TIMEOUT_MS=0` if you run
  longer flows.

Note the timeout is *client-side only*: aborting the request does not cancel engine execution —
the flow keeps running and checkpoints to disk regardless.

---

## 7. Lifecycle, reconnection, failure behavior

- **Startup**: discovery + connect happen when the OMP session starts (background for
  interactive sessions). A server that is down at startup simply shows as disconnected; its
  absence does not fail the session or other servers' tools.
- **Auto-reconnect**: if the transport drops mid-session (e.g. you restart the backend),
  OMP reconnects with backoff (500/1000/2000/4000 ms) and reloads tools; stale tools remain
  visible while reconnecting, and a tool call that hits a retriable connection error attempts
  one reconnect + retry. More than 5 reconnect attempts within 30 s trips a circuit breaker —
  reset it with `/mcp reconnect stepflow`.
- **Manual controls**:

  ```
  /mcp list              # status + source file per server
  /mcp test stepflow     # isolated connect + tools/list probe
  /mcp reload            # disconnect all, rediscover configs, rebind tools (use after editing mcp.json)
  /mcp reconnect stepflow# force one server's reconnect (also resets the circuit breaker)
  ```

- **Teardown**: connections close when the owning OMP session ends. The StepFlow side is
  stateless per request, so there is nothing to clean up on the backend.

---

## 8. Security notes

- `/mcp` has **no authentication** and can create/replace flows (`save_flow`) and start
  executions (`run_flow`). It is intended for local development only (Kestrel binds
  `localhost` by default). Do not expose the management port to untrusted networks without
  putting an authenticating proxy in front.
- Flow definitions saved via MCP must not embed credentials — same rule as the UI: HTTP auth
  tokens are configured per-resource, SSH hosts come from `ssh_hosts.json`.

---

## 9. Troubleshooting

| Symptom | Likely cause / fix |
|---|---|
| `/mcp list` doesn't show `stepflow` | Config not discovered: check `.omp/mcp.json` is valid JSON and in the repo root; run `/mcp reload`. Project-level loading can be disabled by the setting `mcp.enableProjectConfig: false`; a user-file `disabledServers: ["stepflow"]` also suppresses it. |
| `Server "stepflow": stdio server requires "command" field` | Missing `"type": "http"` — OMP defaults to stdio when `type` is omitted. |
| Status stuck at `connecting`, `/mcp test stepflow` fails with connection refused | Backend not running, or wrong port in the URL. Verify with the curl check from §1; confirm which port the backend actually bound (default 5001). |
| `run_flow` times out after ~30 s | Default client timeout on a long flow — raise `timeout` in `.omp/mcp.json` or set `OMP_MCP_TIMEOUT_MS`. The engine execution itself is unaffected. |
| Tools visible but calls fail with `MCP error: …` right after a backend restart | Transient window while OMP auto-reconnects; retry the call or `/mcp reconnect stepflow`. |
| A different tool's config also defines a server named `stepflow` | First definition in discovery order wins (OMP-native project > imported tools > user). Rename one, or use `disabledServers`/`enabledServers` in your user file. |
| Malformed JSON in any MCP config file | That file contributes no entries (discovery warning logged); fix the JSON and `/mcp reload`. |

---

## 10. End-to-end verification checklist

Run these once after connecting; each maps to a concrete observable:

1. `curl` tools/list (§1) → four tool names in the SSE payload.
2. OMP: `/mcp list` → `stepflow` **connected**, source `.omp/mcp.json`.
3. Ask the agent to call `list_flows` → JSON array of registered flows (e.g. includes
   `csv-to-api-form-capture`).
4. Ask the agent to `save_flow` an echo flow (§3) → returns `{id, name, updatedAt}`;
   `get_flow` on that name returns the definition you sent.
5. Ask the agent to `run_flow` it → `"status": "Succeeded"`, `output` contains the echoed
   input, `history` shows `StateEntered`/`StateExited` per state.
6. (Optional) Run a flow with a FormCapture gate → `"status": "Suspended"`; complete the task
   via `/form-capture.html?taskId=…` or `POST /api/form-captures/<id>/submit`; confirm the
   execution resumes to `Succeeded`.
