// bridge-watch.sh's worker pass, one pass at a time on a scratch mailbox: a Linux worker's reports reach the PM's
// mailbox and leave the worker only once mailed, a guest that hangs is cut off and logged instead of stopping the
// loop, and a copy that has stopped finishing passes is replaced. Hyper-V (powershell) and linux.sh are stand-ins
// that serve a fake guest, so this needs neither.
import assert from "node:assert/strict";
import { spawn, spawnSync } from "node:child_process";
import { chmodSync, copyFileSync, existsSync, mkdirSync, mkdtempSync, readdirSync, readFileSync, rmSync, utimesSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { dirname, join } from "node:path";
import { test } from "node:test";
import { fileURLToPath } from "node:url";

const tools = dirname(fileURLToPath(import.meta.url));
// The watcher needs bash 4 (declare -A); a Mac's /bin/bash is 3.2.
const bash4 = spawnSync("bash", ["-c", "declare -A x"]).status === 0;
const hasTimeout = spawnSync("bash", ["-c", "command -v timeout"]).status === 0;

function scratch() {
  const dir = mkdtempSync(join(tmpdir(), "watch-"));
  const [bin, work, bridge, guest] = ["bin", "w", "bridge", "guest"].map(d => join(dir, d));
  for (const d of [bin, work, bridge, guest, join(dir, "pro"), join(dir, "max")]) mkdirSync(d, { recursive: true });
  for (const f of ["bridge-watch.sh", "bridge.sh"]) copyFileSync(join(tools, f), join(work, f));
  const stub = (f, text) => { writeFileSync(f, text); chmodSync(f, 0o755); };
  stub(join(bin, "powershell"), `#!/usr/bin/env bash
case "$*" in
  *Get-VMNetworkAdapter*) echo "$STUB_IP" ;;
  *FreePhysicalMemory*) echo 20 ;;
  *Get-VM*) echo "$STUB_VMS" | tr ' ' '\\n' ;;
esac
`);
  // Nothing here may reach a live host: ssh is a stand-in that fails loudly (done() throws if it was ever run).
  stub(join(bin, "ssh"), `#!/usr/bin/env bash
echo "$*" >> "${dir}/ssh-called"
exit 255
`);
  stub(join(work, "linux.sh"), `#!/usr/bin/env bash
echo "$*" >> "${dir}/said"
[[ "$1" == "$STUB_HANG" ]] && sleep 30
case "$1" in
  attention) ;;
  outbox) for f in "${guest}"/*.md; do [ -f "$f" ] || continue; echo "=== $(basename "$f")"; cat "$f"; done ;;
  outbox-done) shift 3; for f in "$@"; do rm -f "${guest}/$f"; done ;;
esac
`);
  const env = extra => ({
    ...process.env, PATH: `${bin}:${process.env.PATH}`, BRIDGE: bridge, BRIDGE_WATCH_DRY: "1", BRIDGE_WATCH_ONCE: "1",
    BRIDGE_WATCH_HOMES: `pro=${join(dir, "pro")} max=${join(dir, "max")}`, STUB_VMS: "u2", STUB_IP: "172.26.223.16", STUB_HANG: "", ...extra,
  });
  const pass = (extra = {}) => {
    const r = spawnSync("bash", [join(work, "bridge-watch.sh")], { encoding: "utf8", env: env(extra) });
    return { code: r.status, out: r.stdout + r.stderr };
  };
  // The watcher as the host runs it (no BRIDGE_WATCH_ONCE, which changes what log returns): its first pass, then stopped.
  const live = async () => {
    const alive = join(bridge, "watch", "alive");
    rmSync(alive, { force: true });
    const e = env({}); delete e.BRIDGE_WATCH_ONCE;
    const p = spawn("bash", [join(work, "bridge-watch.sh")], { env: e, detached: true, stdio: "ignore" });
    const exited = new Promise(r => p.on("exit", r));
    for (let i = 0; i < 300 && !existsSync(alive); i++) await new Promise(r => setTimeout(r, 50));
    process.kill(-p.pid);
    await exited;
  };
  const report = (id, text) => writeFileSync(join(guest, `${id}.md`), `# u2 reports: ${id}\n\n${text}\n`);
  const pm = () => { const d = join(bridge, "pm"); return existsSync(d) ? readdirSync(d).filter(f => f.endsWith(".md")).map(f => readFileSync(join(d, f), "utf8")) : []; };
  const left = () => readdirSync(guest);
  const log = () => readFileSync(join(bridge, "bridge.log"), "utf8");
  return { dir, bridge, pass, live, report, pm, left, log, done: () => {
    const leaked = existsSync(join(dir, "ssh-called"));
    rmSync(dir, { recursive: true, force: true });
    assert.ok(!leaked, "a test reached for ssh: it must stay on its stand-ins");
  } };
}

test("a Linux worker's reports reach the PM's mailbox, and leave the worker once mailed", { skip: !bash4 }, () => {
  const s = scratch();
  try {
    s.report("20261106-003157-hold-points-lockstep", "Two peers stay in lockstep.");
    s.report("20261106-090022-self-landing", "# u2 reports: self-landing pushed\n\nMore.");
    const { code, out } = s.pass();
    assert.equal(code, 0, out);
    const mail = s.pm();
    assert.equal(mail.length, 2, out);
    assert.ok(mail.some(m => m.startsWith("# u2 reports: 20261106-003157-hold-points-lockstep\nFrom: watch") && m.includes("Two peers stay in lockstep.")));
    assert.ok(mail.some(m => m.startsWith("# u2 reports: 20261106-090022-self-landing\n")), "the subject is the report's first heading");
    assert.deepEqual(s.left(), []);
    assert.match(s.log(), /workers running: u2/);
  } finally { s.done(); }
});

test("as the host runs it, a report mailed once is gone from the worker, and never mailed twice", { skip: !bash4 || process.platform === "win32" }, async () => {
  const s = scratch();
  try {
    s.report("20261106-134400-u2-outbox", "Landed.");
    await s.live();
    assert.equal(s.pm().length, 1, s.log());
    assert.deepEqual(s.left(), [], s.log());
    await s.live();
    assert.equal(s.pm().length, 1, s.log());
    assert.equal(s.log().match(/mailed pm: u2 reports: 20261106-134400-u2-outbox/g)?.length, 1);
  } finally { s.done(); }
});

test("a removal that fails is logged once, not every pass", { skip: !bash4 }, () => {
  const s = scratch();
  try {
    s.report("20261106-120000-a", "text");
    s.pass({ STUB_HANG: "outbox-done", BRIDGE_WATCH_GUEST_SECS: "1" });
    s.pass({ STUB_HANG: "outbox-done", BRIDGE_WATCH_GUEST_SECS: "1" });
    assert.equal(s.log().match(/can't remove u2's mailed reports: linux.sh outbox-done/g)?.length, 1, s.log());
    assert.doesNotMatch(s.log(), /reading u2 again/);
    s.pass();
    assert.match(s.log(), /removing u2's mailed reports again/);
    assert.deepEqual(s.left(), []);
  } finally { s.done(); }
});

test("a report that couldn't be mailed stays on the worker for the next pass", { skip: !bash4 || process.platform === "win32" }, () => {
  const s = scratch();
  try {
    s.report("20261106-120000-a", "text");
    mkdirSync(join(s.bridge, "pm", "taken"), { recursive: true });
    mkdirSync(join(s.bridge, "pm", "done"), { recursive: true });
    chmodSync(join(s.bridge, "pm"), 0o555);
    s.pass();
    assert.deepEqual(s.left(), ["20261106-120000-a.md"]);
    chmodSync(join(s.bridge, "pm"), 0o755);
    s.pass();
    assert.equal(s.pm().length, 1);
    assert.deepEqual(s.left(), []);
  } finally { chmodSync(join(s.bridge, "pm"), 0o755); s.done(); }
});

test("a guest that hangs is cut off and logged, and the pass goes on to the reports", { skip: !bash4 || !hasTimeout }, () => {
  const s = scratch();
  try {
    s.report("20261106-120000-a", "text");
    const started = Date.now();
    const { code, out } = s.pass({ STUB_HANG: "attention", BRIDGE_WATCH_GUEST_SECS: "1" });
    assert.equal(code, 0, out);
    assert.ok(Date.now() - started < 15000, "cut off, not waited out");
    assert.match(s.log(), /can't read u2's attention: linux.sh attention at 172\.26\.223\.16 exited 124 \(cut off\)/);
    assert.equal(s.pm().length, 1);
    s.pass();
    assert.match(s.log(), /reading u2 again/);
  } finally { s.done(); }
});

test("a running worker with no IP is logged once, not every pass", { skip: !bash4 }, () => {
  const s = scratch();
  try {
    s.pass({ STUB_IP: "" });
    s.pass({ STUB_IP: "" });
    assert.equal(s.log().match(/can't read u2's attention: Hyper-V reports no IPv4/g)?.length, 1);
  } finally { s.done(); }
});

test("a copy that hasn't finished a pass in 10 min is replaced; one that has is left alone", { skip: !bash4 || process.platform === "win32" }, () => {
  const s = scratch();
  const old = spawnSync("bash", ["-c", "sleep 60 >/dev/null 2>&1 & echo $!"], { encoding: "utf8" }).stdout.trim();
  try {
    const watch = join(s.bridge, "watch");
    mkdirSync(watch, { recursive: true });
    writeFileSync(join(watch, "pid"), old);
    writeFileSync(join(watch, "alive"), "");
    assert.match(s.pass({ BRIDGE_WATCH_LOCK: "1" }).out, /already running/);
    const stale = (Date.now() - 11 * 60e3) / 1000;
    utimesSync(join(watch, "alive"), stale, stale);
    s.pass({ BRIDGE_WATCH_LOCK: "1" });
    assert.match(s.log(), new RegExp(`replaced pid ${old}: no pass finished in 10 min`));
    assert.notEqual(readFileSync(join(watch, "pid"), "utf8").trim(), old);
  } finally { try { process.kill(Number(old)); } catch { } s.done(); }
});
