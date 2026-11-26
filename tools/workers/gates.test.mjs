// gates.sh's promise: any gate that fails fails the whole, whether it ran in line or alongside, and only the sandbox's are
// optional; and the core tests it runs are the touched modules' (modules.json), never fewer. Each tool is a stand-in on
// PATH that fails when told to (node passes modules.mjs to the real one), so this runs in a moment and needs none of them.
import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { chmodSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { dirname, join } from "node:path";
import { test } from "node:test";
import { fileURLToPath } from "node:url";

const script = join(dirname(fileURLToPath(import.meta.url)), "gates.sh");

function run(fail, sandbox = "0", files = null) {
  const bin = mkdtempSync(join(tmpdir(), "gates-"));
  const said = join(bin, "said");
  for (const tool of ["dotnet", "npm", "node"]) {
    const stub = join(bin, tool);
    const real = tool === "node" ? `[[ "$1" == *modules.mjs ]] && exec "${process.execPath}" "$@"\n` : "";
    // A solution filter is read out into what was said, so a test sees which projects a run had.
    const slnf = `for a in "$@"; do [[ "$a" == *.slnf ]] && cat "$a" >> "${said}"; done\n`;
    writeFileSync(stub, `#!/usr/bin/env bash\n${real}echo "${tool} $* (NODE_OPTIONS=$NODE_OPTIONS)" >> "${said}"\n${slnf}[[ "${tool} $*" == *"${fail}"* ]] && exit 3\nexit 0\n`);
    chmodSync(stub, 0o755);
  }
  const env = { ...process.env, PATH: `${bin}:${process.env.PATH}`, SANDBOX_GATES: sandbox, GATES_ALL: files ? "0" : "1", GATES_FILES: files ? files.join("\n") : "" };
  const done = spawnSync("bash", [script, join(bin, "log")], { encoding: "utf8", env });
  const ran = readFileSync(said, "utf8");
  rmSync(bin, { recursive: true, force: true });
  return { code: done.status, out: done.stdout, ran };
}

const NOTHING = "no such gate";

test("all passing passes", () => {
  const { code, ran } = run(NOTHING);
  assert.equal(code, 0);
  for (const gate of ["dotnet build", "dotnet format", "dotnet test", "chat-report", "chat-strict"]) assert.ok(ran.includes(gate), gate);
  assert.ok(!ran.includes("sandbox run"), "the sandbox's gates are off");
});

for (const gate of ["dotnet build", "dotnet format", "dotnet test", "chat-report", "chat-strict"]) {
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

test("duplicate code isn't a landing gate: CI's duplicates workflow and the sweep run it", () => {
  assert.ok(!run(NOTHING).ran.includes("jscpd"));
  const here = dirname(script);
  assert.match(readFileSync(join(here, "../../.github/workflows/duplicates.yml"), "utf8"), /tools\/jscpd\/run\.sh/);
  assert.match(readFileSync(join(here, "../editmode.sh"), "utf8"), /sweep\)[^]*jscpd\/run\.sh/);
});

test("a test run reads the build it follows", () => assert.match(run(NOTHING).ran, /dotnet test core -c Release --no-build/));

test("a landing leaves the Sweep category to the sweep", () => assert.match(run(NOTHING).ran, /dotnet test core .*--filter TestCategory!=Sweep/));

// By module: what each kind of change runs. The projects come from the real map and core/'s csproj files.
const projectsIn = (ran, filter) => {
  const runs = [...ran.matchAll(/dotnet test (\S+\.slnf) [^\n]*--filter (\S+)[^\n]*\n(\{[^]*?\n\})/g)];
  const hit = runs.find(r => r[2] === filter);
  return hit ? JSON.parse(hit[3]).solution.projects.map(p => p.split("/")[0]).sort() : null;
};
const ALL = ["Chat.Bench.Tests", "Chem.Land.Tests", "Chem.LandWorld.Tests", "Chem.Lockstep.Tests", "Chem.Play.Tests", "Chem.Playtest.Tests", "Chem.Rules.Tests", "Chem.Shared.Tests", "Chem.Sim.Tests", "Hold.Runner.Tests", "Playtest.Runner.Tests", "Rules.Mcp.Tests"];

test("the map agrees with core/'s csproj files and Chem.Tests.props", () => {
  const r = spawnSync(process.execPath, [join(dirname(script), "modules.mjs"), "check"], { encoding: "utf8" });
  assert.equal(r.status, 0, r.stdout + r.stderr);
});

test("a Land-only change runs Chem.Land.Tests and what references it, and the rest's Unit fixtures", () => {
  const { code, out, ran } = run(NOTHING, "0", ["unity/Assets/Prototype/Chemistry/Tests/ChunkGenTests.cs", "core/Chem.Land.Tests/LandFixture.cs"]);
  assert.equal(code, 0, out);
  const tested = projectsIn(ran, "TestCategory!=Sweep");
  assert.deepEqual(tested, ["Chem.Land.Tests", "Chem.LandWorld.Tests"], "LandWorld references Land (its helpers); Sim no longer does (step 5b)");
  assert.deepEqual(projectsIn(ran, "TestCategory=Unit"), ALL.filter(p => !tested.includes(p)), "the smoke is every other project's Unit fixtures");
  assert.ok(!/dotnet test core /.test(ran));
  assert.match(out, /core tests: some \(Chem\.Land\.Tests .*\)/);
  assert.match(out, /tests-land \[.*ChunkGenTests\.cs\]/, "it says why");
});

test("a docs change runs no dotnet tests, but still builds and formats", () => {
  const { code, out, ran } = run(NOTHING, "0", ["docs/BACKLOG.md", "unity/CLAUDE.md", "README.md"]);
  assert.equal(code, 0, out);
  assert.ok(!ran.includes("dotnet test"));
  assert.ok(ran.includes("dotnet build") && ran.includes("dotnet format"));
  assert.match(out, /core tests: none/);
});

test("a tools-only change runs no dotnet tests; the workers' node tests still run", () => {
  const { ran } = run(NOTHING, "0", ["tools/workers/land.sh", "tools/probe.sh"]);
  assert.ok(!ran.includes("dotnet test"));
  assert.match(ran, /node --test tools\/workers\/gates\.test\.mjs/);
});

test("a doc a test reads runs that test's project", () => {
  assert.deepEqual(projectsIn(run(NOTHING, "0", ["docs/MODDING.md"]).ran, "TestCategory!=Sweep"), ["Chem.Rules.Tests", "Chem.Sim.Tests"]);
});

test("an unknown path runs everything", () => {
  const { out, ran } = run(NOTHING, "0", ["docs/BACKLOG.md", "somewhere/new.txt"]);
  assert.match(ran, /dotnet test core -c Release --no-build .*--filter TestCategory!=Sweep/);
  assert.match(out, /somewhere\/new\.txt is in no module/);
});

test("a Land runtime change runs everything: Game.Core references Chem.Land, and every test project Game.Core", () => {
  assert.match(run(NOTHING, "0", ["packages/com.anweather.chemistry-core/Runtime/Land/HillsRoads.cs"]).ran, /dotnet test core -c Release/);
});

test("a Runtime change above the land runs every project but the land's own tests, which build on Chem.Land alone", () => {
  const tested = projectsIn(run(NOTHING, "0", ["packages/com.anweather.chemistry-core/Runtime/World.Sim.cs"]).ran, "TestCategory!=Sweep");
  assert.deepEqual(tested, ALL.filter(p => p !== "Chem.Land.Tests"));
});

test("a rules/ change runs every project that reads the rules", () => {
  const tested = projectsIn(run(NOTHING, "0", ["rules/spells.json"]).ran, "TestCategory!=Sweep");
  assert.deepEqual(tested, ALL.filter(p => p !== "Playtest.Runner.Tests"));
});

test("a shared core file (the props, Directory.Build.props) runs everything", () => {
  for (const f of ["core/Chem.Tests.props", "core/Directory.Build.props"]) assert.match(run(NOTHING, "0", [f]).ran, /dotnet test core -c Release/, f);
});

test("a failing smoke fails the landing", () => {
  const { code, out } = run("TestCategory=Unit", "0", ["unity/Assets/Prototype/Chemistry/Tests/ChunkGenTests.cs"]);
  assert.equal(code, 1);
  assert.match(out, /FAILED {2}core smoke/);
});
