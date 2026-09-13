#!/usr/bin/env python3
"""Prod-instance acceptance: the imported council-fees solution runs end-to-end on :5011.

Form capture -> submit -> department lookup -> human approval -> SQL update, all against
the freshly created C:\\temp\\stepflow-prod\\data\\fees.db (bound via SqlDataSources__fees).
"""
import json
import sqlite3
import time
import urllib.request

BASE = "http://localhost:5011"
PROD_FEES_DB = r"C:\temp\stepflow-prod\data\fees.db"


def http(method, url, body=None):
    data = json.dumps(body).encode() if body is not None else None
    req = urllib.request.Request(url, data=data, method=method,
                                 headers={"Content-Type": "application/json"})
    try:
        with urllib.request.urlopen(req) as r:
            raw = r.read().decode()
            return r.status, (json.loads(raw) if raw else None)
    except urllib.error.HTTPError as e:
        raw = e.read().decode()
        try:
            return e.code, json.loads(raw)
        except Exception:
            return e.code, raw


def wait_status(exec_id, want, timeout=60):
    deadline = time.time() + timeout
    last = None
    while time.time() < deadline:
        st, exr = http("GET", f"{BASE}/api/flows/executions/{exec_id}")
        if st == 200:
            last = exr
            if exr.get("status") in want:
                return exr
        time.sleep(0.5)
    raise AssertionError(f"execution {exec_id} did not reach {want}; last={json.dumps(last)[:400]}")


def find_event(execution, event_type):
    """Last history entry of the given type (e.g. 'FormCaptureCreated', 'HumanTaskCreated')."""
    found = None
    for h in execution.get("history", []):
        if h.get("type") == event_type:
            found = h
    return found


passed, failed = 0, 0


def check(name, ok, detail=""):
    global passed, failed
    print(f"[{'PASS' if ok else 'FAIL'}] {name}" + (f" — {detail}" if detail and not ok else ""))
    if ok:
        passed += 1
    else:
        failed += 1


# 0. prod fees db exists with schema + seed from the import migrations
conn = sqlite3.connect(PROD_FEES_DB)
# idempotent re-run: remove this check's own row if a previous run left it
conn.execute("DELETE FROM fees WHERE fee_type='dog-license' AND year=2030")
conn.commit()
tables = [r[0] for r in conn.execute("SELECT name FROM sqlite_master WHERE type='table'")]
check("prod fees.db created by import (fees + fee_types tables)", {"fees", "fee_types"} <= set(tables), str(tables))
seeded = dict(conn.execute("SELECT fee_type, department FROM fee_types").fetchall())
check("fee_types seed rows present from package migrations", seeded.get("dog-license") == "Animal Control", str(seeded))

# 1. start the council flow on prod
st, exr = http("POST", f"{BASE}/api/flows/execute/council-fee-capture", {})
check("prod: council-fee-capture starts", st in (200, 202), f"status={st} body={str(exr)[:200]}")
exec_id = exr.get("executionId") or exr.get("id")
check("prod: got execution id", bool(exec_id), str(exr)[:200])

# 2. suspends at form capture; contract served from imported form + domain
exr = wait_status(exec_id, {"Suspended"})
ev = find_event(exr, "FormCaptureCreated") or {}
task_id = (ev.get("data") or {}).get("taskId")
check("prod: suspended at form-capture task", bool(task_id), json.dumps(exr)[:300])

st, contract = http("GET", f"{BASE}/api/form-captures/{task_id}")
options = []
if st == 200 and isinstance(contract, dict):
    page = (contract.get("form") or {}).get("page") or {}
    def walk(el):
        if not isinstance(el, dict):
            return
        # Dropdown options are child elements of type DropdownOption with a Value.
        if el.get("Type") == "DropdownOption":
            options.append(el.get("Value", ""))
        for ch in (el.get("Children") or []):
            walk(ch)
    for sec in page.get("RootElements", []) or []:
        walk(sec)
check("prod: form contract served with imported dropdown options",
      st == 200 and "dog-license" in options and "liquor-license" in options, f"status={st} options={options}")

# 3. submit the form (dog license 2030)
st, sub = http("POST", f"{BASE}/api/form-captures/{task_id}/submit",
               {"values": {"feeType": "dog-license", "year": 2030, "amount": 75}})
check("prod: form submit accepted", st in (200, 202), f"status={st} body={str(sub)[:200]}")

# 4. suspends at human approval task assigned to Animal Control
exr = wait_status(exec_id, {"Suspended"})
ev = find_event(exr, "HumanTaskCreated") or {}
ht_id = (ev.get("data") or {}).get("taskId")
st, ht = http("GET", f"{BASE}/api/human-tasks/{ht_id}") if ht_id else (404, None)
assignee = (ht or {}).get("assignee", "") if isinstance(ht, dict) else ""
check("prod: suspended at human approval task", bool(ht_id), json.dumps(exr)[:300])
check("prod: approver is Animal Control (department lookup worked)", assignee == "Animal Control", f"assignee={assignee!r} body={str(ht)[:200]}")

# 5. approve it
st, comp = http("POST", f"{BASE}/api/human-tasks/{ht_id}/complete",
                {"approved": True, "completedBy": "Animal Control"})
check("prod: approval accepted", st in (200, 202), f"status={st} body={str(comp)[:200]}")

# 6. execution succeeds and the row landed in the PROD database
exr = wait_status(exec_id, {"Succeeded"}, timeout=90)
check("prod: execution Succeeded", exr.get("status") == "Succeeded", json.dumps(exr)[:300])
row = conn.execute(
    "SELECT fee_type, year, amount, status FROM fees WHERE fee_type='dog-license' AND year=2030").fetchone()
check("prod: fees.db row (dog-license, 2030, 75.0, approved)",
      row is not None and row[0] == "dog-license" and row[1] == 2030 and abs(row[2] - 75.0) < 0.01 and row[3] == "approved",
      str(row))

# 7. the imported dynamic API serves prod data on :5011
st, api = http("GET", f"{BASE}/api/dynamic/council-fees/type/dog-license")
rows = (api or {}).get("rows") if isinstance(api, dict) else None
check("prod: Council-Fees-API serves the new row via /api/dynamic",
      st == 200 and rows is not None and any(r.get("year") == 2030 for r in rows), f"status={st} body={str(api)[:200]}")

# cleanup: stop the probe execution left over from manual verification
http("POST", f"{BASE}/api/flows/executions/f4626963b579/stop", {})

print("=" * 56)
print(f"RESULT: {passed}/{passed + failed} passed, {failed} failed")
raise SystemExit(1 if failed else 0)
