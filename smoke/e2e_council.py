#!/usr/bin/env python3
"""StepFlow end-to-end verification: MCP -> REST API -> dynamic APIs -> council flow.

Run with all three hosts up:
  main app :5001, fake test host :5095, DynamicApiHost :5002
Exit code 0 = all checks passed.
"""
import json
import os
import sqlite3
import sys
import time
import urllib.error
import urllib.request

BASE = "http://localhost:5001"
DYN = "http://localhost:5002"
MCP_URL = BASE + "/mcp"
REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
FEES_DB = os.path.join(REPO, "StepFunctionsApp", "fees.db")

PASS, FAIL = [], []


def check(name, cond, detail=""):
    (PASS if cond else FAIL).append((name, str(detail)[:400]))
    print(f"[{'PASS' if cond else 'FAIL'}] {name}" + ("" if cond else f"  -> {str(detail)[:400]}"))


def http(method, url, body=None, headers=None, timeout=120):
    data = json.dumps(body).encode() if body is not None else None
    req = urllib.request.Request(url, data=data, method=method)
    req.add_header("Content-Type", "application/json")
    for k, v in (headers or {}).items():
        req.add_header(k, v)
    try:
        with urllib.request.urlopen(req, timeout=timeout) as r:
            raw = r.read().decode()
            return r.status, (json.loads(raw) if raw else None)
    except urllib.error.HTTPError as e:
        raw = e.read().decode()
        try:
            return e.code, json.loads(raw)
        except Exception:
            return e.code, raw


_mcp_id = 0


def mcp_call(method, params=None):
    global _mcp_id
    _mcp_id += 1
    payload = {"jsonrpc": "2.0", "id": _mcp_id, "method": method}
    if params is not None:
        payload["params"] = params
    req = urllib.request.Request(MCP_URL, data=json.dumps(payload).encode(), method="POST")
    req.add_header("Content-Type", "application/json")
    req.add_header("Accept", "application/json, text/event-stream")
    with urllib.request.urlopen(req, timeout=180) as r:
        raw = r.read().decode()
    for line in raw.splitlines():
        if not line.startswith("data:"):
            continue
        msg = json.loads(line[5:].strip())
        if isinstance(msg, dict) and msg.get("id") == _mcp_id:
            if "error" in msg:
                return {"__rpc_error": msg["error"]}
            res = msg.get("result", {})
            # tools/list returns the tool catalog directly; tools/call wraps JSON text in content[]
            if isinstance(res, dict) and "tools" in res:
                return res
            text = "".join(c.get("text", "") for c in res.get("content", []) if c.get("type") == "text")
            try:
                return json.loads(text)
            except Exception:
                return {"raw": text}
    raise RuntimeError(f"no MCP response for id {_mcp_id}: {raw[:500]}")


def mcp(tool, args=None):
    r = mcp_call("tools/call", {"name": tool, "arguments": args or {}})
    if "__rpc_error" in r:
        return {"error": str(r["__rpc_error"])}
    return r


def wait_execution(exec_id, want_statuses, timeout=120):
    """Poll until the execution reaches one of want_statuses (or a terminal state)."""
    deadline = time.time() + timeout
    last = None
    while time.time() < deadline:
        st, ex = http("GET", f"{BASE}/api/flows/executions/{exec_id}")
        if st == 200:
            last = ex
            status = ex.get("status")
            if status in want_statuses or status in ("Succeeded", "Failed", "Aborted", "TimedOut"):
                return ex
        time.sleep(1.5)
    return last


def pending_tasks(exec_id, completion_type=None):
    st, tasks = http("GET", f"{BASE}/api/human-tasks?executionId={exec_id}")
    out = [t for t in (tasks or []) if t.get("status") == "Pending"]
    if completion_type:
        out = [t for t in out if (t.get("completionType") or "").lower() == completion_type]
    return out


def fees_rows():
    con = sqlite3.connect(FEES_DB)
    rows = list(con.execute(
        "SELECT id, fee_type, year, amount, status, approved_by FROM fees ORDER BY id"))
    con.close()
    return rows


# ════════════════════════════════════════════════════════════════════════════
print("=" * 72)
print("PART 1 — MCP server surface")
print("=" * 72)

tools = mcp_call("tools/list")
tool_names = [t["name"] for t in tools.get("tools", [])]
check("MCP tools/list returns tool catalog", len(tool_names) >= 30, f"got {len(tool_names)}: {tool_names}")
for expected in ["list_flows", "get_flow", "save_flow", "run_flow",
                 "list_dynamic_apis", "get_dynamic_api", "save_dynamic_api", "delete_dynamic_api",
                 "write_eav_row", "read_eav_rows", "update_eav_row", "patch_eav_row", "delete_eav_row",
                 "run_data_exchange_profile", "list_attribute_domains"]:
    check(f"MCP tool present: {expected}", expected in tool_names)

flows = mcp("list_flows")
flow_ids = [f.get("id") for f in flows] if isinstance(flows, list) else []
check("MCP list_flows includes council-fee-capture", "council-fee-capture" in flow_ids or any(
    (f.get('name') == 'Council Fee Capture') for f in flows), json.dumps(flows)[:300])

gf = mcp("get_flow", {"idOrName": "council-fees-read"})
check("MCP get_flow council-fees-read returns definition",
      gf.get("definition", {}).get("startAt") == "BuildQuery" and len(gf["definition"].get("states", {})) == 2,
      json.dumps(gf)[:300])

rr = mcp("run_flow", {"idOrName": "council-fees-read", "inputJson": "{}"})
check("MCP run_flow council-fees-read succeeds with rows",
      rr.get("status") == "Succeeded" and (rr.get("output") or {}).get("count", 0) >= 2, json.dumps(rr)[:300])

rf = mcp("run_flow", {"idOrName": "council-fees-read", "inputJson": '{"feeType":"dog-license"}'})
rows_f = (rf.get("output") or {}).get("rows", [])
check("MCP run_flow council-fees-read feeType filter works",
      rf.get("status") == "Succeeded" and rows_f and all(r["feeType"] == "dog-license" for r in rows_f),
      json.dumps(rf)[:300])

# ════════════════════════════════════════════════════════════════════════════
print("=" * 72)
print("PART 2 — MCP dynamic API CRUD + live serving on :5001 and :5002")
print("=" * 72)

# 2a. save a throwaway echo flow via MCP (also proves MCP -> workspace persistence)
sf = mcp("save_flow", {
    "name": "e2e-echo-flow",
    "statesJson": json.dumps({
        "Start": {"type": "Pass", "next": "Echo"},
        # jsonata is a Task resource (transform://jsonata), same mechanism as council-fees-read.
        # input_data.$ = whole flow input -> $.ping / $.hello inside the expression.
        "Echo": {"type": "Task", "resource": "transform://jsonata",
                 # NOTE: JSONata object keys are expressions — bare identifiers evaluate
                 # against the context, so literal keys must be string-quoted.
                 "parameters": {"expression": "{\"note\":'echoed', \"ping\":$.ping, \"hello\":$.hello}",
                                "input_data.$": "$"},
                 "next": "Done"},
        "Done": {"type": "Succeed"}
    }),
})
check("MCP save_flow persists e2e-echo-flow", bool(sf.get("id")), json.dumps(sf)[:300])

persisted = False
ws_root = os.path.join(REPO, "StepFunctionsApp", "workspace-data", "Default", "Default", "Default", "flows")
if os.path.isdir(ws_root):
    for d in os.listdir(ws_root):
        p = os.path.join(ws_root, d, "flow.json")
        if os.path.isfile(p) and json.load(open(p)).get("states", {}).get("Echo", {}).get("resource") == "transform://jsonata":
            persisted = True
check("MCP-saved flow written to workspace disk (visible in UI catalog)", persisted)

er = mcp("run_flow", {"idOrName": "e2e-echo-flow", "inputJson": '{"ping":"via-mcp","hello":"world"}'})
out = er.get("output") or {}
check("MCP run_flow e2e-echo-flow echoes input",
      er.get("status") == "Succeeded" and out.get("note") == "echoed"
      and out.get("ping") == "via-mcp" and out.get("hello") == "world", json.dumps(er)[:300])

# 2b. dynamic API CRUD via MCP
apis = mcp("list_dynamic_apis")
api_ids = [a.get("id") for a in apis] if isinstance(apis, list) else []
check("MCP list_dynamic_apis lists Council-Fees-API", "Council-Fees-API" in api_ids, json.dumps(apis)[:300])

apis_acme = mcp("list_dynamic_apis", {"nodePathPrefix": "Acme UI/Website"})
acme_ids = [a.get("id") for a in apis_acme] if isinstance(apis_acme, list) else []
check("MCP list_dynamic_apis nodePathPrefix filter works", "Site-Orders" in acme_ids and "Council-Fees-API" not in acme_ids,
      json.dumps(apis_acme)[:300])

gapi = mcp("get_dynamic_api", {"id": "Council-Fees-API"})
ops = gapi.get("operations") or []
check("MCP get_dynamic_api Council-Fees-API exposes flow-backed ops",
      any(o.get("method") == "GET" and o.get("handlerType") == "flow" and o.get("flowId") == "council-fees-read" for o in ops),
      json.dumps(gapi)[:300])

sapi = mcp("save_dynamic_api", {"definitionJson": json.dumps({
    "name": "E2E-Echo",
    "description": "temporary e2e dynamic api",
    "nodePath": "Default/Default/Default",
    "basePath": "/e2e-echo",
    "isActive": True,
    "isPublished": True,  # DynamicApiHost only syncs published APIs
    "operations": [
        {"method": "POST", "path": "", "handlerType": "flow", "flowId": "e2e-echo-flow",
         "description": "echo via dynamic api"}
    ],
})})
check("MCP save_dynamic_api creates E2E-Echo", sapi.get("id") and sapi.get("created") is True, json.dumps(sapi)[:300])

# 2c. in-process dispatcher on :5001 serves it immediately
st, body = http("POST", f"{BASE}/api/dynamic/e2e-echo", {"ping": "via-5001"})
check(":5001 dynamic API dispatch (in-process) echoes flow output",
      st == 200 and isinstance(body, dict) and body.get("ping") == "via-5001" and body.get("note") == "echoed",
      f"{st} {json.dumps(body)[:300]}")

# 2d. DynamicApiHost :5002 picks it up on its sync cycle (<= ~35s)
deadline = time.time() + 45
loaded = False
while time.time() < deadline:
    st, h = http("GET", f"{DYN}/health")
    if st == 200 and h.get("apisLoaded", 0) >= 4:
        loaded = True
        break
    time.sleep(3)
check(":5002 DynamicApiHost catalog syncs new API", loaded, json.dumps(h)[:200])

st, body = http("POST", f"{DYN}/api/dynamic/e2e-echo", {"ping": "via-5002"})
check(":5002 dynamic API dispatch (external host) echoes flow output",
      st == 200 and isinstance(body, dict) and body.get("ping") == "via-5002" and body.get("note") == "echoed",
      f"{st} {json.dumps(body)[:300]}")

# unknown route -> clean 404 JSON error
st, body = http("GET", f"{DYN}/api/dynamic/nope-nothing")
check(":5002 unknown dynamic API returns 404 JSON error", st == 404 and isinstance(body, dict) and "error" in body,
      f"{st} {json.dumps(body)[:200]}")

# 2e. delete via MCP; :5001 stops serving immediately
dapi = mcp("delete_dynamic_api", {"id": sapi.get("id") or "E2E-Echo"})
check("MCP delete_dynamic_api removes E2E-Echo", dapi.get("deleted") is True, json.dumps(dapi)[:200])
st, body = http("POST", f"{BASE}/api/dynamic/e2e-echo", {"ping": "gone"})
check(":5001 no longer dispatches deleted API (404)", st == 404, f"{st} {json.dumps(body)[:200]}")

# ════════════════════════════════════════════════════════════════════════════
print("=" * 72)
print("PART 3 — MCP EAV row round-trip (write/read/patch/update/delete)")
print("=" * 72)

w = mcp("write_eav_row", {"domain": "e2e-test-domain", "valuesJson": '{"a":1,"b":"x"}', "entityId": "ent-1"})
row_key = w.get("rowKeyId")
check("MCP write_eav_row returns rowKeyId", bool(row_key), json.dumps(w)[:200])

r = mcp("read_eav_rows", {"domain": "e2e-test-domain", "limit": 5})
mine = [x for x in (r if isinstance(r, list) else []) if x.get("rowKeyId") == row_key]
check("MCP read_eav_rows sees the new row", len(mine) == 1 and mine[0]["values"].get("b") == "x", json.dumps(r)[:300])

p = mcp("patch_eav_row", {"domain": "e2e-test-domain", "rowKeyId": row_key, "patchJson": '{"b":"y","c":true}'})
r2 = mcp("read_eav_rows", {"domain": "e2e-test-domain", "limit": 5})
mine2 = [x for x in r2 if x.get("rowKeyId") == row_key][0]
check("MCP patch_eav_row merges partial values", p.get("patched") is True and mine2["values"].get("b") == "y"
      and mine2["values"].get("c") is True and mine2["values"].get("a") == 1, json.dumps(mine2)[:300])

u = mcp("update_eav_row", {"domain": "e2e-test-domain", "rowKeyId": row_key, "valuesJson": '{"z":9}'})
r3 = mcp("read_eav_rows", {"domain": "e2e-test-domain", "limit": 5})
mine3 = [x for x in r3 if x.get("rowKeyId") == row_key][0]
check("MCP update_eav_row replaces values wholesale", u.get("updated") is True and mine3["values"] == {"z": 9},
      json.dumps(mine3)[:200])

d = mcp("delete_eav_row", {"domain": "e2e-test-domain", "rowKeyId": row_key})
r4 = mcp("read_eav_rows", {"domain": "e2e-test-domain", "limit": 5})
check("MCP delete_eav_row removes the row", d.get("deleted") is True and all(x.get("rowKeyId") != row_key for x in r4),
      json.dumps(d)[:200])

# ════════════════════════════════════════════════════════════════════════════
print("=" * 72)
print("PART 4 — MCP data-exchange profile execution")
print("=" * 72)

profiles = mcp("list_data_exchange_profiles")
pnames = [p.get("name") or p.get("id") for p in profiles] if isinstance(profiles, list) else []
check("MCP list_data_exchange_profiles lists workspace profiles", len(pnames) >= 3, json.dumps(profiles)[:300])

# Run the customer-orders-import profile with inline rows matching its source schema.
# Pipeline: MapToInternalSchema -> EnrichWithNzdRate (lookup on fake host :5095)
#           -> dispatch CSV + North-only JSON to file://C:/temp/dx-e2e/out/
os.makedirs(r"C:\temp\dx-e2e\out", exist_ok=True)
# Source attribute names per the profile's MapToInternalSchema AttributeMappings.
dx_rows = [
    {"OrderID": "ORD-100", "CustomerCode": "CUST-A", "Region": "North", "Amount": 100, "CurrencyCode": "USD"},
    {"OrderID": "ORD-101", "CustomerCode": "CUST-B", "Region": "South", "Amount": 50, "CurrencyCode": "USD"},
]
rex = mcp("run_data_exchange_profile",
          {"idOrName": "customer-orders-import", "inputJson": json.dumps({"rows": dx_rows})})
ok = isinstance(rex, dict) and (rex.get("success") is True or rex.get("status") in ("Succeeded", "Completed"))
csv_out = r"C:\temp\dx-e2e\out\enriched-orders.csv"
check(f"MCP run_data_exchange_profile customer-orders-import executes inline rows",
      ok, json.dumps(rex)[:400])
if os.path.isfile(csv_out):
    csv_text = open(csv_out).read()
    check("data-exchange dispatch wrote enriched CSV (incl. NZD lookup from :5095)",
          "ORD-100" in csv_text and "NZD" in csv_text.upper() or "AmountNZD" in csv_text, csv_text[:300])
else:
    check("data-exchange dispatch wrote enriched CSV (incl. NZD lookup from :5095)", False,
          f"{csv_out} missing; result={json.dumps(rex)[:300]}")

# ════════════════════════════════════════════════════════════════════════════
print("=" * 72)
print("PART 5 — REST API surface (UI-facing routes)")
print("=" * 72)

st, h = http("GET", f"{BASE}/api/health")
check("REST /api/health healthy with flows registered", st == 200 and h.get("status") == "healthy" and h.get("registeredFlows", 0) >= 10,
      json.dumps(h)[:200])

st, fl = http("GET", f"{BASE}/api/flows")
names = [f.get("name") for f in (fl or [])] if isinstance(fl, list) else []
check("REST GET /api/flows lists council flows + e2e-echo-flow",
      "Council Fee Capture" in names and "Council Fees Read" in names and "e2e-echo-flow" in names, str(names)[:300])

st, reg = http("POST", f"{BASE}/api/flows", {
    "name": "e2e-rest-temp",
    "states": {"Only": {"type": "Succeed", "result": {"ok": True}}},
})
temp_id = (reg or {}).get("id")
check("REST POST /api/flows registers temp flow", st in (200, 201) and bool(temp_id), f"{st} {json.dumps(reg)[:200]}")

st, exr = http("POST", f"{BASE}/api/flows/execute-sync/{temp_id}", {})
check("REST execute-sync runs temp flow to Succeeded", st == 200 and (exr or {}).get("status") == "Succeeded",
      f"{st} {json.dumps(exr)[:200]}")

st, dele = http("DELETE", f"{BASE}/api/flows/{temp_id}")
check("REST DELETE /api/flows/{id} unregisters flow (new endpoint)", st == 200 and (dele or {}).get("deleted") is True,
      f"{st} {json.dumps(dele)[:200]}")

st, stop = http("POST", f"{BASE}/api/flows/executions/nonexistent123/stop", {})
check("REST stop endpoint reports stopped:false for unknown id (new endpoint)",
      st == 200 and not (stop or {}).get("stopped"), f"{st} {json.dumps(stop)[:200]}")

st, ws = http("GET", f"{BASE}/api/workspace")
default_flows = []
if isinstance(ws, dict):
    for org in ws.get("orgs", []) or []:
        if org.get("name") != "Default":
            continue
        for proj in org.get("projects", []) or []:
            if proj.get("name") != "Default":
                continue
            for sub in proj.get("subProjects", []) or []:
                if sub.get("name") == "Default":
                    default_flows = [f.get("id") for f in sub.get("flows", []) or []]
check("REST GET /api/workspace tree exposes Default/Default/Default with council + e2e flows",
      st == 200 and {"council-fee-capture", "council-fees-read", "e2e-echo-flow"} <= set(default_flows),
      f"{st} {str(default_flows)[:300]}")

# ════════════════════════════════════════════════════════════════════════════
print("=" * 72)
print("PART 6 — COUNCIL FLOW end-to-end (form capture -> SQL -> human approval)")
print("=" * 72)


def run_council_case(label, fee_values, approval_payload, expect_status):
    r = mcp("run_flow", {"idOrName": "council-fee-capture", "inputJson": "{}"})
    check(f"{label}: run_flow suspends at form capture", r.get("status") == "Suspended", json.dumps(r)[:300])
    exec_id = r.get("executionId")
    if not exec_id:
        return None, None

    forms = pending_tasks(exec_id, "form")
    check(f"{label}: pending form-capture task discovered via REST", len(forms) == 1, json.dumps(forms)[:300])
    tid = forms[0]["taskId"]

    st, fdef = http("GET", f"{BASE}/api/form-captures/{tid}")
    attrs = [a.get("attributeName") for a in (fdef or {}).get("attributes", [])] if isinstance(fdef, dict) else []
    check(f"{label}: form definition + CouncilFee contract served to UI page",
          st == 200 and (fdef or {}).get("form", {}).get("formId") == "council-fees"
          and set(attrs) >= {"feeType", "year", "amount"}, f"{st} {json.dumps(fdef)[:300]}")

    st, sub = http("POST", f"{BASE}/api/form-captures/{tid}/submit", {"values": fee_values})
    check(f"{label}: form submission accepted and execution resumed", st == 200 and (sub or {}).get("status") == "completed",
          f"{st} {json.dumps(sub)[:300]}")

    ex = wait_execution(exec_id, want_statuses={"Suspended"}, timeout=90)
    check(f"{label}: execution resumed past SQL insert + department lookup, suspended at approval HumanTask",
          ex is not None and ex.get("status") == "Suspended",
          json.dumps({k: ex.get(k) for k in ("status", "currentNode")} if ex else None)[:300])

    approvals = pending_tasks(exec_id, "api")
    check(f"{label}: approval human task discovered via REST", len(approvals) == 1, json.dumps(approvals)[:300])
    atid = approvals[0]["taskId"]

    st, comp = http("POST", f"{BASE}/api/human-tasks/{atid}/complete", approval_payload)
    check(f"{label}: human task completion accepted via REST", st == 200, f"{st} {json.dumps(comp)[:300]}")

    final = wait_execution(exec_id, want_statuses=set(), timeout=120)
    check(f"{label}: execution reaches {expect_status}", final is not None and final.get("status") == expect_status,
          json.dumps({k: final.get(k) for k in ("status", "errorCode", "errorMessage")} if final else None)[:400])
    return exec_id, final


# Run A — dog-license, correct approver -> approved row
run_council_case("A(dog/approve)", {"feeType": "dog-license", "year": 2027, "amount": 150},
                 {"approved": True, "completedBy": "Animal Control"}, "Succeeded")

rows = fees_rows()
a_row = [r for r in rows if r[1] == "dog-license" and r[2] == 2027]
check("A: fees.db has dog-license/2027 approved by Animal Control",
      len(a_row) == 1 and a_row[0][4] == "approved" and a_row[0][5] == "Animal Control", str(rows))

eav_file = os.path.join(REPO, "StepFunctionsApp", "eav-data", "CouncilFee.json")
if os.path.isfile(eav_file):
    eav_doc = json.load(open(eav_file))
    eav_rows = eav_doc if isinstance(eav_doc, list) else (eav_doc.get("rows") or [])
else:
    eav_rows = []
a_eav = [r for r in eav_rows
         if (r.get("Values") or {}).get("feeType") == "dog-license"
         and float((r.get("Values") or {}).get("year", 0)) == 2027]
check("A: form capture persisted an EAV row (CouncilFee domain)", len(a_eav) >= 1, str(eav_rows[-3:])[:400])

# Run B — food-premises, WRONG approver -> rejected row
run_council_case("B(food/reject)", {"feeType": "food-premises-license", "year": 2027, "amount": 40},
                 {"approved": True, "completedBy": "Food Safety"}, "Succeeded")

rows = fees_rows()
b_row = [r for r in rows if r[1] == "food-premises-license" and r[2] == 2027]
check("B: fees.db has food-premises/2027 rejected (approver mismatch)",
      len(b_row) == 1 and b_row[0][4] == "rejected", str(rows))

# Run C — liquor-license, correct approver -> approved row (validates fee_types fix)
run_council_case("C(liquor/approve)", {"feeType": "liquor-license", "year": 2026, "amount": 300},
                 {"approved": True, "completedBy": "Liquor Licensing Board"}, "Succeeded")

rows = fees_rows()
c_row = [r for r in rows if r[1] == "liquor-license" and r[2] == 2026]
check("C: fees.db has liquor-license/2026 approved by Liquor Licensing Board",
      len(c_row) == 1 and c_row[0][4] == "approved" and c_row[0][5] == "Liquor Licensing Board", str(rows))

# Run D — unknown fee type -> Fail(UnknownFeeType)
r = mcp("run_flow", {"idOrName": "council-fee-capture", "inputJson": "{}"})
exec_id = r.get("executionId")
forms = pending_tasks(exec_id, "form") if exec_id else []
if forms:
    st, sub = http("POST", f"{BASE}/api/form-captures/{forms[0]['taskId']}/submit",
                   {"values": {"feeType": "parking-permit", "year": 2027, "amount": 10}})
    final = wait_execution(exec_id, want_statuses=set(), timeout=60)
    check("D: unknown fee type fails with UnknownFeeType",
          st == 200 and final is not None and final.get("status") == "Failed" and final.get("errorCode") == "UnknownFeeType",
          json.dumps({k: final.get(k) for k in ("status", "errorCode")} if final else None)[:300])
else:
    check("D: unknown fee type fails with UnknownFeeType", False, "no pending form task found")

# ════════════════════════════════════════════════════════════════════════════
print("=" * 72)
print("PART 7 — council data read-back through the published dynamic API")
print("=" * 72)

st, body = http("GET", f"{BASE}/api/dynamic/council-fees")
all_rows = (body or {}).get("rows", []) if isinstance(body, dict) else []
check(":5001 GET /council-fees returns all fees incl. new approved rows",
      st == 200 and any(r.get("feeType") == "dog-license" and r.get("year") == 2027 and r.get("status") == "approved" for r in all_rows)
      and any(r.get("feeType") == "liquor-license" and r.get("status") == "approved" for r in all_rows),
      f"{st} {json.dumps(body)[:400]}")

st, body = http("GET", f"{BASE}/api/dynamic/council-fees/type/food-premises-license")
frows = (body or {}).get("rows", []) if isinstance(body, dict) else []
check(":5001 GET /council-fees/type/{feeType} path-param filter works",
      st == 200 and frows and all(r.get("feeType") == "food-premises-license" for r in frows),
      f"{st} {json.dumps(body)[:300]}")

st, body = http("GET", f"{DYN}/api/dynamic/council-fees/type/liquor-license")
lrows = (body or {}).get("rows", []) if isinstance(body, dict) else []
check(":5002 GET /council-fees/type/{feeType} matches engine result",
      st == 200 and lrows and all(r.get("feeType") == "liquor-license" for r in lrows),
      f"{st} {json.dumps(body)[:300]}")

# ════════════════════════════════════════════════════════════════════════════
print("=" * 72)
total = len(PASS) + len(FAIL)
print(f"RESULT: {len(PASS)}/{total} passed, {len(FAIL)} failed")
if FAIL:
    print("\nFailed checks:")
    for name, detail in FAIL:
        print(f"  - {name}: {detail}")
sys.exit(1 if FAIL else 0)
