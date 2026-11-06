// land.sh here, end to end on a scratch origin: a worker lands the branch it's on, and every refusal stops with the
// reason, exits non-zero and goes to the PM through the worker's outbox. The gates are a stand-in that passes unless
// told to fail, so this needs no dotnet.
import assert from "node:assert/strict";
import { execFileSync, spawnSync } from "node:child_process";
import { copyFileSync, existsSync, mkdirSync, mkdtempSync, readdirSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { dirname, join } from "node:path";
import { test } from "node:test";
import { fileURLToPath } from "node:url";

const tools = dirname(fileURLToPath(import.meta.url));
const taken = "docs/handoffs/briefs/queue/taken";
const ident = { GIT_AUTHOR_NAME: "t", GIT_AUTHOR_EMAIL: "t@t", GIT_COMMITTER_NAME: "t", GIT_COMMITTER_EMAIL: "t@t" };

function scratch(devFiles = {}) {
  const dir = mkdtempSync(join(tmpdir(), "land-"));
  const home = join(dir, "home");
  mkdirSync(join(home, ".worker", "outbox"), { recursive: true });
  const env = { ...process.env, ...ident, HOME: home, WORKER_CAPS: "core", LAND_NO_MAIL: "1" };
  const g = (cwd, ...a) => execFileSync("git", a, { cwd, env, encoding: "utf8", stdio: ["ignore", "pipe", "pipe"] }).trim();
  g(dir, "init", "-q", "--bare", "-b", "dev", "origin.git");
  g(dir, "clone", "-q", "origin.git", "repo");
  const repo = join(dir, "repo");
  const put = (f, text) => { mkdirSync(join(repo, f, ".."), { recursive: true }); writeFileSync(join(repo, f), text); };
  for (const f of ["land.sh", "queue.mjs", "bridge.sh"]) { mkdirSync(join(repo, "tools/workers"), { recursive: true }); copyFileSync(join(tools, f), join(repo, "tools/workers", f)); }
  put("tools/workers/gates.sh", '#!/usr/bin/env bash\n[[ -z "${LAND_TEST_FAIL:-}" ]] || { echo "  FAILED  core tests"; exit 1; }\necho "  ok  core tests"\n');
  put("tools/workers/commit-cost.mjs", "");
  put("README.md", "x\n");
  for (const [f, text] of Object.entries(devFiles)) put(f, text);
  g(repo, "add", "-A"); g(repo, "commit", "-qm", "dev"); g(repo, "push", "-q", "origin", "HEAD:dev");
  g(repo, "switch", "-q", "-c", "feat");
  const commit = (files, remove = []) => {
    for (const [f, text] of Object.entries(files)) put(f, text);
    for (const f of remove) g(repo, "rm", "-q", f);
    g(repo, "add", "-A"); g(repo, "commit", "-qm", "work");
  };
  const land = (args = [], extra = {}) => {
    const r = spawnSync("bash", ["tools/workers/land.sh", "here", ...args], { cwd: repo, env: { ...env, ...extra }, encoding: "utf8", input: "Merge feat\n" });
    return { code: r.status, out: r.stdout + r.stderr };
  };
  const outbox = () => readdirSync(join(home, ".worker", "outbox")).map(f => readFileSync(join(home, ".worker", "outbox", f), "utf8"));
  const devHas = f => spawnSync("git", ["cat-file", "-e", `origin/dev:${f}`], { cwd: repo }).status === 0;
  return { dir, repo, g, commit, land, outbox, devHas, done: () => rmSync(dir, { recursive: true, force: true }) };
}

test("a clean branch lands: on dev, deleted on origin, the checkout back on dev, nothing sent", () => {
  const s = scratch();
  try {
    s.commit({ "rules/x.json": "{}\n" });
    const { code, out } = s.land();
    assert.equal(code, 0, out);
    assert.match(out, /Landed feat on dev/);
    s.g(s.repo, "fetch", "-q", "--prune");
    assert.ok(s.devHas("rules/x.json"));
    assert.equal(s.g(s.repo, "branch", "--show-current"), "dev");
    assert.equal(s.g(s.repo, "branch", "-r", "--list", "origin/feat"), "");
    assert.deepEqual(s.outbox(), []);
    assert.ok(existsSync(`${s.repo}-land`), "the landing worktree is kept for the next landing's build");
  } finally { s.done(); }
});

const refused = (s, out, code, why) => {
  assert.equal(code, 1, out);
  assert.match(out, new RegExp(`STOP: .*${why}`));
  const [report, ...more] = s.outbox();
  assert.equal(more.length, 0);
  assert.match(report, /^# .* reports: landing feat refused/);
  assert.match(report, new RegExp(why));
};

test("a branch that leaves its queue claim on dev is refused; deleting it, however long, needs no --deletes", () => {
  const s = scratch({ [`${taken}/feat.md`]: "Needs: core\nArea: rules/\nBranch: feat\n" + "A long brief.\n".repeat(150) });
  try {
    s.commit({ "rules/x.json": "{}\n" });
    let { code, out } = s.land();
    refused(s, out, code, "leaves its claim");
    s.commit({}, [`${taken}/feat.md`]);
    ({ code, out } = s.land());
    assert.equal(code, 0, out);
    assert.ok(!s.devHas(`${taken}/feat.md`));
  } finally { s.done(); }
});

test("Unity C# from a machine without Unity stops, with or without --compiled", () => {
  for (const args of [[], ["--compiled"]]) {
    const s = scratch();
    try {
      s.commit({ "unity/Assets/Prototype/Scripts/A.cs": "class A {}\n" });
      const { code, out } = s.land(args);
      refused(s, out, code, "the PC");
      assert.ok(!s.devHas("unity/Assets/Prototype/Scripts/A.cs"));
    } finally { s.done(); }
  }
});

test("Unity C# lands with --compiled where there's Unity", () => {
  const s = scratch();
  try {
    s.commit({ "unity/Assets/Prototype/Scripts/A.cs": "class A {}\n" });
    const { code, out } = s.land(["--compiled"], { WORKER_CAPS: "unity" });
    assert.equal(code, 0, out);
    assert.ok(s.devHas("unity/Assets/Prototype/Scripts/A.cs"));
  } finally { s.done(); }
});

test("a failing gate is refused and dev is untouched", () => {
  const s = scratch();
  try {
    s.commit({ "rules/x.json": "{}\n" });
    const { code, out } = s.land([], { LAND_TEST_FAIL: "1" });
    refused(s, out, code, "a gate failed");
    assert.ok(!s.devHas("rules/x.json"));
    assert.equal(s.g(s.repo, "branch", "--show-current"), "feat", "the worker stays on its branch to fix it");
  } finally { s.done(); }
});

test("deleting files still needs --deletes, as the coordinator's landing does", () => {
  const s = scratch();
  try {
    s.commit({}, ["README.md"]);
    const { code, out } = s.land();
    refused(s, out, code, "deletes files");
    assert.equal(s.land(["--deletes"]).code, 0);
  } finally { s.done(); }
});

test("uncommitted changes, or being on dev, are refused before anything is pushed", () => {
  const s = scratch();
  try {
    s.commit({ "rules/x.json": "{}\n" });
    writeFileSync(join(s.repo, "rules/x.json"), "{ }\n");
    let { code, out } = s.land();
    refused(s, out, code, "has changes");
    assert.equal(s.g(s.repo, "ls-remote", "origin", "feat"), "", "nothing pushed");
    s.g(s.repo, "checkout", "-q", "--", ".");
    s.g(s.repo, "switch", "-q", "dev");
    rmSync(join(s.dir, "home/.worker/outbox"), { recursive: true }); mkdirSync(join(s.dir, "home/.worker/outbox"));
    ({ code, out } = s.land());
    assert.equal(code, 1);
    assert.match(out, /STOP: the checkout is on dev/);
  } finally { s.done(); }
});
