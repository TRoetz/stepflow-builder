"""E2E parity check: backend :5099 (in-process) vs DynamicApiHost :5002 (HTTP engine).

Creates a published + an unpublished dynamic API on the backend, then compares
responses for identical requests against both surfaces. Exits 0 on full parity.
Requires the workspace flow 'parity-echo' under Acme UI/Website/Web.
"""
import json
import sys
import time
import urllib.error
import urllib.request

BACKEND = "http://localhost:5099"
HOST = "http://localhost:5002"


def call(base, method, path, body=None):
    req = urllib.request.Request(
        base + path,
        data=json.dumps(body).encode() if body is not None else None,
        headers={"Content-Type": "application/json"} if body is not None else {},
        method=method,
    )
    try:
        with urllib.request.urlopen(req, timeout=60) as resp:
            return resp.status, resp.read().decode()
    except urllib.error.HTTPError as e:
        return e.code, e.read().decode()


def main():
    failures = []

    def check(label, cond, detail=""):
        print(f"{'PASS' if cond else 'FAIL'}  {label}" + (f"  [{detail}]" if detail and not cond else ""))
        if not cond:
            failures.append(label)

    # ── 0. both surfaces alive ────────────────────────────────────────────────
    s, b = call(BACKEND, "GET", "/api/dynamic/apis")
    check("backend /api/dynamic/apis reachable", s == 200, f"status={s} body={b[:200]}")
    s, b = call(HOST, "GET", "/health")
    check("host /health alive", s == 200, f"status={s} body={b[:200]}")

    # ── 1. create published + unpublished APIs on the backend ────────────────
    payload_pub = {
        "name": "parity-check",
        "nodePath": "Acme UI/Website/Web",
        "basePath": "/parity",
        "isPublished": True,
        "operations": [{"method": "GET", "path": "", "handlerType": "flow", "flowId": "parity-echo"}],
    }
    payload_hidden = dict(payload_pub, name="parity-hidden", basePath="/hidden", isPublished=False)

    s, b = call(BACKEND, "POST", "/api/dynamic/apis", payload_pub)
    check("create published API -> 201", s == 201, f"status={s} body={b[:300]}")
    pub_id = json.loads(b).get("id") if s in (200, 201) else None

    s, b = call(BACKEND, "POST", "/api/dynamic/apis", payload_hidden)
    check("create unpublished API -> 201", s == 201, f"status={s} body={b[:300]}")

    # ── 2. wait for the host catalog to pick up the published API ────────────
    deadline = time.time() + 45
    seen = False
    while time.time() < deadline:
        s, b = call(HOST, "GET", "/api/dynamic/parity")
        if s != 404:
            seen = True
            break
        time.sleep(2)
    check("host serves published API after sync", seen, f"last status={s} body={b[:300]}")

    # ── 3. parity: GET /api/dynamic/parity on both surfaces (success path) ───
    sb, bb = call(BACKEND, "GET", "/api/dynamic/parity")
    sh, bh = call(HOST, "GET", "/api/dynamic/parity")
    print(f"      backend :5099 -> {sb} {bb[:200]}")
    print(f"      host    :5002 -> {sh} {bh[:200]}")
    check("flow GET status parity", sb == sh, f"backend={sb} host={sh}")
    try:
        jb, jh = json.loads(bb), json.loads(bh)
        check("flow GET body parity", jb == jh, f"backend={bb[:300]} host={bh[:300]}")
    except json.JSONDecodeError as e:
        check("flow GET body parity (raw)", bb == bh, str(e))

    # ── 4. unpublished API: backend serves it in-process, host must not ──────
    sb, bb = call(BACKEND, "GET", "/api/dynamic/hidden")
    sh, bh = call(HOST, "GET", "/api/dynamic/hidden")
    print(f"      backend :5099 -> {sb} {bb[:120]}   host :5002 -> {sh} {bh[:120]}")
    check("backend serves unpublished API in-process (not 404)", sb != 404, f"status={sb} body={bb[:200]}")
    check("host does NOT expose unpublished API (404)", sh == 404, f"status={sh} body={bh[:200]}")

    # ── 5. unknown route parity (both must 404 with same error shape) ────────
    sb, bb = call(BACKEND, "GET", "/api/dynamic/definitely-not-here")
    sh, bh = call(HOST, "GET", "/api/dynamic/definitely-not-here")
    check("unknown route 404 parity", sb == sh == 404, f"backend={sb} host={sh}")
    try:
        check("unknown route error shape parity", json.loads(bb) == json.loads(bh), f"{bb[:150]} vs {bh[:150]}")
    except json.JSONDecodeError:
        check("unknown route error shape parity (raw)", bb == bh, f"{bb[:150]} vs {bh[:150]}")

    # ── 6. cleanup: delete both test APIs via backend management API ─────────
    if pub_id:
        s, b = call(BACKEND, "DELETE", f"/api/dynamic/apis/{pub_id}")
        check("cleanup published API deleted", s in (200, 204), f"status={s} body={b[:150]}")
    s, b = call(BACKEND, "GET", "/api/dynamic/apis")
    if s == 200:
        for api in json.loads(b):
            if api.get("name") in ("parity-check", "parity-hidden"):
                s2, b2 = call(BACKEND, "DELETE", f"/api/dynamic/apis/{api['id']}")
                check(f"cleanup {api['name']} deleted", s2 in (200, 204), f"status={s2}")

    print()
    if failures:
        print(f"E2E PARITY: {len(failures)} FAILURE(S): {failures}")
        return 1
    print("E2E PARITY: ALL CHECKS PASSED")
    return 0


if __name__ == "__main__":
    sys.exit(main())
