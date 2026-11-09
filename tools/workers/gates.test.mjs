// gates.sh's promise: any gate that fails fails the whole, whether it ran in line or alongside, and only the sandbox's are
// optional. Each tool is a stand-in on PATH that fails when told to, so this runs in a moment and needs none of them.
import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { chmodSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { dirname, join } from "node:path";
import { test } from "node:test";
import { fileURLToPath } from "node:url";

const script = join(dirname(fileURLToPath(import.meta.url)), "gates.sh");

function run(fail, sandbox = "0") {
  const bin = mkdtempSync(join(tmpdir(), "gates-"));
  const said = join(bin, "said");
  for (const tool of ["dotnet", "npm", "node"]) {
    const stub = join(bin, tool);
    writeFileSync(stub, `#!/usr/bin/env bash\necho "${tool} $* (NODE_OPTIONS=$NODE_OPTIONS)" >> "${said}"\n[[ "${tool} $*" == *"${fail}"* ]] && exit 3\nexit 0\n`);
    chmodSync(stub, 0o755);
  }
  const done = spawnSync("bash", [script, join(bin, "log")], {
    encoding: "utf8",
    env: { ...process.env, PATH: `${bin}:${process.env.PATH}`, SANDBOX_GATES: sandbox },
  });
  const ran = readFileSync(said, "utf8");
  rmSync(bin, { recursive: true, force: true });
  return { code: done.status, out: done.stdout, ran };
}

const NOTHING = "no such gate";

test("all passing passes", () => {
  const { code, ran } = run(NOTHING);
  assert.equal(code, 0);
  for (const gate of ["dotnet build", "dotnet format", "dotnet test", "jscpd", "chat-report", "chat-strict"]) assert.ok(ran.includes(gate), gate);
  assert.ok(!ran.includes("sandbox run"), "the sandbox's gates are off");
});

for (const gate of ["dotnet build", "dotnet format", "dotnet test", "jscpd", "chat-report", "chat-strict"]) {
  test(`${gate} failing fails the landing`, () => {
    const { code, out } = run(gate);
    assert.equal(code, 1);
    assert.match(out, /FAILED/);
  });
}

for (const gate of ["typecheck", "sandbox test", "run build"]) {
  test(`with the sandbox on, ${gate} failing fails the landing`, () => assert.equal(run(gate, "1").code, 1));
}

test("nothing runs on a build that failed", () => {
  const { ran } = run("dotnet build");
  assert.ok(!ran.includes("dotnet test") && !ran.includes("dotnet format"));
});

test("jscpd gets the heap a 5 GB VM needs, with no NODE_OPTIONS in the shell", () => {
  const prior = process.env.NODE_OPTIONS;
  delete process.env.NODE_OPTIONS;
  try {
    assert.match(run(NOTHING).ran, /exec --offline -- jscpd.*\(NODE_OPTIONS=.*--max-old-space-size=3072\)/);
  } finally {
    if (prior !== undefined) process.env.NODE_OPTIONS = prior;
  }
});

test("jscpd is the one pinned in tools/jscpd's lock, not whatever npx fetches", () => {
  const { ran } = run(NOTHING);
  assert.match(ran, /npm ci --prefix \S*tools\/jscpd /);
  assert.ok(!ran.includes("npx"));
});

test("a test run reads the build it follows", () => assert.match(run(NOTHING).ran, /dotnet test core -c Release --no-build/));

test("a landing leaves the Sweep category to the sweep", () => assert.match(run(NOTHING).ran, /dotnet test core .*--filter TestCategory!=Sweep/));
