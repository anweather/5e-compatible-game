// The brief queue (queue.mjs): which brief a machine takes (its needs, areas a taken brief holds, nothing queued), and
// the claim on a scratch origin, including two workers racing for one brief.
import assert from "node:assert/strict";
import { execFileSync } from "node:child_process";
import { chmodSync, mkdtempSync, mkdirSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { test } from "node:test";
import { caps, claim, meets, overlaps, parseBrief, pick, queueOn, QUEUE } from "./queue.mjs";

const brief = (needs, area, branch = "b") => `Needs: ${needs}\nArea: ${area}\nBranch: ${branch}\n\n# Brief\n`;
const core = new Set(["core"]), unity = new Set(["core", "unity"]), mac = new Set(["core", "unity", "mac"]);

test("a brief's head is read, comments and code marks aside", () => {
  assert.deepEqual(parseBrief("Needs: unity      (what the worker must have)\nArea: `tools/workers/**`, docs/a.md (plus notes)\nBranch: `x-y`\n"),
    { needs: "unity", area: ["tools/workers/**", "docs/a.md"], branch: "x-y" });
  assert.deepEqual(parseBrief("# no head\n"), { needs: null, area: [], branch: null });
});

test("needs: core and any run anywhere, unity where an Editor is, mac on the Mac; unsaid runs nowhere", () => {
  for (const n of ["core", "any"]) assert.ok(meets(n, core));
  assert.ok(!meets("unity", core) && meets("unity", unity));
  assert.ok(!meets("mac", unity) && meets("mac", mac));
  assert.ok(!meets(null, mac) && !meets("gpu", mac));
});

test("areas overlap when one holds the other, globs by their folder", () => {
  assert.ok(overlaps(["tools/workers/**"], ["tools/workers/land.sh"]));
  assert.ok(overlaps(["tools/workers/*.cmd"], ["tools/workers/"]));
  assert.ok(overlaps(["unity/Assets"], ["unity/Assets/Prototype/Scripts/ChemGrid.cs"]));
  assert.ok(!overlaps(["tools/workers/**"], ["tools/coordinator/**"]));
  assert.ok(!overlaps(["docs/a.md"], ["docs/ab.md"]));
  assert.ok(overlaps([], ["docs/a.md"]), "a brief naming no area could touch anything");
});

test("pick: the first by name this machine meets, passing over overlapping areas", () => {
  const briefs = [
    { name: "c-core.md", text: brief("core", "rules/") },
    { name: "a-unity.md", text: brief("unity", "unity/Assets/Prototype/Scripts/") },
    { name: "b-busy.md", text: brief("core", "tools/workers/queue.mjs") },
  ];
  const taken = [{ name: "t.md", text: brief("core", "tools/workers/**") }];
  const onCore = pick(briefs, taken, core);
  assert.equal(onCore.pick.name, "c-core.md");
  assert.deepEqual(onCore.passed, ["a-unity.md: needs unity", "b-busy.md: its area overlaps t.md, taken"]);
  assert.equal(pick(briefs, taken, unity).pick.name, "a-unity.md");
});

test("pick: nothing queued, or nothing this machine can do, is nothing", () => {
  assert.equal(pick([], [], mac).pick, null);
  assert.equal(pick([{ name: "u.md", text: brief("unity", "unity/") }], [], core).pick, null);
});

test("caps: given (env, then ~/.worker/caps) or found", () => {
  const none = () => { throw new Error("no file"); };
  assert.deepEqual([...caps({ env: { WORKER_CAPS: "unity, mac" }, read: none })], ["core", "unity", "mac"]);
  assert.deepEqual([...caps({ env: {}, read: () => "unity\n" })], ["core", "unity"]);
  assert.deepEqual([...caps({ env: {}, read: none, platform: "linux", exists: () => true })], ["core"]);
  assert.deepEqual([...caps({ env: {}, read: none, platform: "win32", exists: () => true })], ["core", "unity"]);
  assert.deepEqual([...caps({ env: {}, read: none, platform: "darwin", exists: () => false })], ["core", "mac"]);
});

test("the queue is opt-in: on for u2 unless told, off elsewhere unless told", () => {
  const none = () => { throw new Error("no file"); };
  assert.ok(queueOn("u2", { env: {}, read: none }));
  assert.ok(!queueOn("w1", { env: {}, read: none }));
  assert.ok(queueOn("w1", { env: {}, read: () => "on\n" }));
  assert.ok(!queueOn("u2", { env: { WORKER_QUEUE: "0" }, read: () => "on" }));
});

// A scratch origin with a queue on dev, and a clone per worker.
function origin(files) {
  const dir = mkdtempSync(join(tmpdir(), "queue-"));
  const g = (cwd, ...a) => execFileSync("git", a, { cwd, encoding: "utf8", stdio: ["ignore", "pipe", "pipe"] }).trim();
  g(dir, "init", "-q", "--bare", "-b", "dev", "origin.git");
  g(dir, "clone", "-q", "origin.git", "seed");
  const seed = join(dir, "seed");
  for (const [f, text] of Object.entries(files)) { mkdirSync(join(seed, f, ".."), { recursive: true }); writeFileSync(join(seed, f), text); }
  g(seed, "add", "-A"); g(seed, "commit", "-qm", "queue"); g(seed, "push", "-q", "origin", "HEAD:dev");
  const clone = name => { g(dir, "clone", "-q", "origin.git", name); return join(dir, name); };
  return { dir, g, clone };
}
const ident = { GIT_AUTHOR_NAME: "t", GIT_AUTHOR_EMAIL: "t@t", GIT_COMMITTER_NAME: "t", GIT_COMMITTER_EMAIL: "t@t" };
Object.assign(process.env, ident);

test("claim moves the brief to taken/ on dev, and the next claim passes over it", () => {
  const { dir, g, clone } = origin({ [`${QUEUE}/README.md`]: "readme", [`${QUEUE}/a.md`]: brief("core", "rules/"), [`${QUEUE}/b.md`]: brief("core", "rules/x.json") });
  try {
    const u2 = clone("u2");
    const got = claim(u2, core, "u2");
    assert.equal(got.taken, `${QUEUE}/taken/a.md`);
    assert.equal(g(u2, "status", "--porcelain"), "", "the worker's checkout isn't touched");
    const tree = g(u2, "ls-tree", "-r", "--name-only", "origin/dev", QUEUE).split("\n");
    assert.deepEqual(tree, [`${QUEUE}/README.md`, `${QUEUE}/b.md`, `${QUEUE}/taken/a.md`]);
    assert.match(g(u2, "log", "-1", "--format=%s", "origin/dev"), /^queue: u2 takes a\.md$/);
    assert.equal(claim(clone("w1"), core, "w1"), null, "b.md's area is inside a.md's");
  } finally { rmSync(dir, { recursive: true, force: true }); }
});

test("a claim that loses the push race fetches and picks again", () => {
  const { dir, g, clone } = origin({ [`${QUEUE}/a.md`]: brief("core", "rules/"), [`${QUEUE}/b.md`]: brief("core", "world/") });
  try {
    const [u2, w1] = [clone("u2"), clone("w1")];
    // Between w1's pick and its push, u2 takes a.md: origin's hook lands u2's claim, then refuses w1's push.
    const hook = join(dir, "origin.git", "hooks", "pre-receive");
    writeFileSync(hook, `#!/usr/bin/env bash\n[ -e "${dir}/raced" ] && exit 0\ntouch "${dir}/raced"\nenv -u GIT_QUARANTINE_PATH git update-ref refs/heads/dev "$(cat "${dir}/u2-claim")"\nexit 1\n`);
    chmodSync(hook, 0o755);
    g(u2, "fetch", "-q");
    writeFileSync(join(dir, "raced"), ""); // u2's own claim goes through
    assert.equal(claim(u2, core, "u2").name, "a.md");
    writeFileSync(join(dir, "u2-claim"), g(u2, "rev-parse", "origin/dev"));
    g(dir + "/origin.git", "update-ref", "refs/heads/dev", g(u2, "rev-parse", "origin/dev~1")); // back to before it
    rmSync(join(dir, "raced"));
    const lines = [];
    assert.equal(claim(w1, core, "w1", { log: m => lines.push(m) }).name, "b.md");
    assert.ok(lines.includes("queue: lost the race for a.md; picking again"), lines.join("\n"));
    assert.deepEqual(g(w1, "ls-tree", "--name-only", `origin/dev:${QUEUE}/taken`).split("\n"), ["a.md", "b.md"]);
  } finally { rmSync(dir, { recursive: true, force: true }); }
});

test("globs keep their file-name prefix, so two files in one folder don't clash", () => {
  assert.equal(overlaps(['packages/com.anweather.chemistry-core/Runtime/MapCheck*.cs'], ['packages/com.anweather.chemistry-core/Runtime/WorldPlan*.cs']), false);
  assert.equal(overlaps(['packages/com.anweather.chemistry-core/Runtime/'], ['packages/com.anweather.chemistry-core/Runtime/WorldPlan*.cs']), true);
  assert.equal(overlaps(['tools/workers/**'], ['tools/workers/*.cmd']), true);
  assert.equal(overlaps(['docs/x.md'], ['docs/xy.md']), false);
  assert.equal(overlaps(['world/maps/'], ['world/maps/valley/land.json']), true);
});
