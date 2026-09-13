// Drives the real form-capture page in headless Edge via CDP:
// renders the live suspended task, fills feeType/year/amount (dispatching change events),
// clicks Submit — the page's own JS then POSTs to /api/form-captures/{taskId}/submit.
// Usage: node smoke/ui_form_submit.mjs <taskId> <feeType> <year> <amount>
import { spawn } from "node:child_process";

const [tid, feeType = "liquor-license", year = "2028", amount = "300"] = process.argv.slice(2);
if (!tid) { console.error("usage: ui_form_submit.mjs <taskId> [feeType] [year] [amount]"); process.exit(2); }

const EDGE = "C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe";
const PORT = 9333;
const URL = `http://localhost:5001/form-capture.html?taskId=${tid}`;

const edge = spawn(EDGE, [
  "--headless=new", "--disable-gpu", "--no-first-run",
  `--remote-debugging-port=${PORT}`,
  "--user-data-dir=C:\\temp\\edge-cdp-profile",
  "about:blank",
], { stdio: "ignore" });

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

async function getJson(path, method = "GET") {
  const res = await fetch(`http://127.0.0.1:${PORT}${path}`, { method });
  return res.json();
}

// wait for devtools endpoint
let target = null;
for (let i = 0; i < 40 && !target; i++) {
  await sleep(500);
  try {
    // newer Chrome requires PUT for /json/new
    let res = await fetch(`http://127.0.0.1:${PORT}/json/new?${encodeURIComponent(URL)}`, { method: "PUT" });
    if (!res.ok) res = await fetch(`http://127.0.0.1:${PORT}/json/new?${encodeURIComponent(URL)}`);
    target = await res.json();
  } catch {}
}
if (!target?.webSocketDebuggerUrl) { console.error("FAIL: no CDP target"); edge.kill(); process.exit(1); }

const ws = new WebSocket(target.webSocketDebuggerUrl);
let seq = 0; const pending = new Map();
ws.onmessage = (ev) => {
  const m = JSON.parse(ev.data.toString());
  if (m.id && pending.has(m.id)) { pending.get(m.id)(m.result ?? { error: m.error }); pending.delete(m.id); }
};
await new Promise((r, j) => { ws.onopen = r; ws.onerror = j; });

function send(method, params = {}) {
  return new Promise((resolve) => { const id = ++seq; pending.set(id, resolve); ws.send(JSON.stringify({ id, method, params })); });
}
async function evalJs(expression) {
  const r = await send("Runtime.evaluate", { expression, returnByValue: true, awaitPromise: true });
  if (r?.exceptionDetails) throw new Error("page exception: " + JSON.stringify(r.exceptionDetails).slice(0, 300));
  return r?.result?.value;
}

await send("Runtime.enable");
await send("Page.enable");

// wait for the form to render from /api/form-captures/{taskId}
let rendered = false;
for (let i = 0; i < 60 && !rendered; i++) {
  rendered = await evalJs(`!!document.querySelector('[data-key="feeType"]')`);
  if (!rendered) await sleep(500);
}
if (!rendered) { console.error("FAIL: form fields never rendered"); ws.close(); edge.kill(); process.exit(1); }

const options = await evalJs(`Array.from(document.querySelectorAll('select[data-key="feeType"] option')).map(o => o.value).join(",")`);
console.log("dropdown options:", options);
if (!options.includes(feeType)) { console.error(`FAIL: feeType '${feeType}' not in dropdown (${options})`); ws.close(); edge.kill(); process.exit(1); }

// fill fields — the page only records values on 'change' events
await evalJs(`(() => { const s = document.querySelector('select[data-key="feeType"]'); s.value = ${JSON.stringify(feeType)}; s.dispatchEvent(new Event("change", { bubbles: true })); return s.value; })()`);
await evalJs(`(() => { const y = document.querySelector('[data-key="year"]'); y.value = ${JSON.stringify(year)}; y.dispatchEvent(new Event("change", { bubbles: true })); return y.value; })()`);
await evalJs(`(() => { const a = document.querySelector('[data-key="amount"]'); a.value = ${JSON.stringify(amount)}; a.dispatchEvent(new Event("change", { bubbles: true })); return a.value; })()`);

// click Submit — the page's own handler POSTs to /api/form-captures/{taskId}/submit
await evalJs(`document.querySelector('button.submit').click(); true`);

let banner = "", ok = false;
for (let i = 0; i < 90 && !ok; i++) {
  await sleep(500);
  banner = await evalJs(`document.getElementById("banner")?.textContent || ""`) || "";
  if (banner.includes("Form submitted")) ok = true;
  else if (banner.toLowerCase().includes("error") || banner.includes("failed")) break;
}
console.log(ok ? "UI SUBMIT OK — page banner: " + banner : "UI SUBMIT FAILED — banner: " + banner);
ws.close(); edge.kill();
process.exit(ok ? 0 : 1);
