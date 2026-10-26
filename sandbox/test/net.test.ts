// Lockstep over an in-memory room: several "pages" in one process.
import { test } from "node:test";
import assert from "node:assert/strict";
import { Lockstep, type PeerInfo, type Snapshot, type Transport } from "../src/net.ts";
import { RULES, type ModPatch } from "../src/rules.ts";
import { applyMods } from "../src/intents.ts";
import { SCENARIOS, loadScenario } from "../src/scenarios.ts";
import { hashWorld, playerOf } from "../src/world.ts";

type Handler = (data: unknown, from: string) => void;

class Hub {
  pages = new Map<string, FakeTransport>();
  snapshot: Snapshot | null = null;
  mods: Record<string, ModPatch> = {};
  /** Probability that a "tick" to a given page is lost. */
  drop = new Map<string, number>();
  seed = 99;
  rand() {
    this.seed = (this.seed * 1103515245 + 12345) >>> 0;
    return this.seed / 2 ** 32;
  }
}

class FakeTransport implements Transport {
  handlers = new Map<string, Handler[]>();
  presence = { since: 0, synced: false };
  me: string;
  private hub: Hub;
  constructor(me: string, hub: Hub) {
    this.me = me;
    this.hub = hub;
    hub.pages.set(me, this);
  }
  peers(): PeerInfo[] {
    return [...this.hub.pages.values()].map((p) => ({ id: p.me, ...p.presence }));
  }
  emit(topic: string, data: unknown) {
    const json = JSON.stringify(data);
    assert.ok(json.length <= 4096, `${topic} message is ${json.length} bytes`);
    for (const page of this.hub.pages.values()) {
      if (topic === "tick" && page.me !== this.me && this.hub.rand() < (this.hub.drop.get(page.me) ?? 0)) continue;
      setTimeout(() => page.deliver(topic, JSON.parse(json), this.me), 0);
    }
  }
  deliver(topic: string, data: unknown, from: string) {
    if (!this.hub.pages.has(this.me)) return;
    for (const h of this.handlers.get(topic) ?? []) h(data, from);
  }
  on(topic: string, fn: Handler) {
    this.handlers.set(topic, [...(this.handlers.get(topic) ?? []), fn]);
  }
  setPresence(p: { since: number; synced: boolean }) {
    this.presence = p;
  }
  async saveSnapshot(s: Snapshot) {
    this.hub.snapshot = JSON.parse(JSON.stringify(s));
  }
  async loadSnapshot() {
    return this.hub.snapshot ? (JSON.parse(JSON.stringify(this.hub.snapshot)) as Snapshot) : null;
  }
}

const settle = async (rounds = 6) => { for (let i = 0; i < rounds; i++) await new Promise((r) => setTimeout(r, 1)); };

function page(hub: Hub, id: string, since: number, joinExisting: boolean) {
  const world = loadScenario(SCENARIOS.find((s) => s.id === "meadow")!, 5);
  const t = new FakeTransport(id, hub);
  const ls = new Lockstep(t, world, async (ids) => (ids.every((i) => hub.mods[i]) ? ids.map((i) => hub.mods[i]!) : null), {
    joinExisting, now: since, retryMs: 20,
  });
  return { t, ls };
}

async function run(pages: { ls: Lockstep }[], frames: number) {
  for (let f = 0; f < frames; f++) {
    for (const p of pages) p.ls.frame(1000 / 6 + 1);
    await settle(3);
  }
  await settle(30);
}

const agree = (pages: { ls: Lockstep }[]) => {
  const hashes = pages.map((p) => hashWorld(p.ls.world));
  assert.ok(hashes.every((h) => h === hashes[0]), `worlds differ: ${hashes.join(" ")}`);
};

test("a friend joins late, gets the snapshot, and both worlds stay identical", async () => {
  const hub = new Hub();
  const a = page(hub, "aaa", 1, false);
  await run([a], 10);
  a.ls.submit({ k: "cast", id: "fire-bolt", x: 9, y: 15 });
  await run([a], 5);
  const b = page(hub, "bbb", 2, true);
  await run([a, b], 10);
  assert.equal(b.ls.state(), "synced");
  assert.equal(a.ls.state(), "hosting");
  b.ls.submit({ k: "walk", d: 1 });
  b.ls.submit({ k: "cast", id: "create-water", x: 20, y: 5 });
  a.ls.submit({ k: "cast", id: "gust-of-wind", x: 25, y: 13 });
  await run([a, b], 30);
  agree([a, b]);
  assert.ok(playerOf(a.ls.world, "aaa") && playerOf(a.ls.world, "bbb"), "both wizards exist");
});

test("lost ticks are detected and repaired", async () => {
  const hub = new Hub();
  const a = page(hub, "aaa", 1, false);
  const b = page(hub, "bbb", 2, true);
  await run([a, b], 8);
  let resyncs = 0;
  b.ls.onState = (st) => { if (st === "syncing") resyncs++; };
  hub.drop.set("bbb", 0.3);
  for (let i = 0; i < 5; i++) a.ls.submit({ k: "cast", id: "fire-bolt", x: 3 + i, y: 3 });
  await run([a, b], 40);
  hub.drop.delete("bbb");
  await run([a, b], 20);
  agree([a, b]);
  assert.equal(b.ls.state(), "synced");
  assert.ok(resyncs > 0, "dropping ticks should have forced at least one resync");
});

test("when the host leaves, the next-longest page takes over and a third page follows it", async () => {
  const hub = new Hub();
  const a = page(hub, "aaa", 1, false);
  const b = page(hub, "bbb", 2, true);
  const c = page(hub, "ccc", 3, true);
  await run([a, b, c], 12);
  c.ls.submit({ k: "walk", d: 2 });
  await run([a, b, c], 6);
  let cResynced = false;
  c.ls.onState = (st) => { if (st === "syncing") cResynced = true; };
  hub.pages.delete("aaa");
  await run([b, c], 25);
  assert.equal(b.ls.state(), "hosting");
  b.ls.submit({ k: "cast", id: "thunderwave", x: 15, y: 13 });
  await run([b, c], 15);
  agree([b, c]);
  assert.ok(cResynced, "the third page followed the new host via a snapshot");
  assert.equal(playerOf(b.ls.world, "aaa"), undefined, "the departed player's wizard is gone");
});

test("a mod switched on mid-game changes the rules for everyone at the same tick", async () => {
  const hub = new Hub();
  hub.mods["long-slides"] = {
    sim: { push: { slideCells: 7 } },
    reactions: { add: [{ id: "frost-on-grass", element: "cold", if: { terrainTag: "flammable" }, do: [{ extinguish: true }], log: "Frost settles on the grass" }] },
  };
  const a = page(hub, "aaa", 1, false);
  const b = page(hub, "bbb", 2, true);
  await run([a, b], 8);
  b.ls.submit({ k: "mods", ids: ["long-slides"] });
  await run([a, b], 10);
  assert.deepEqual(a.ls.world.mods, ["long-slides"]);
  assert.deepEqual(b.ls.world.mods, ["long-slides"]);
  assert.equal(RULES.sim.push.slideCells, 7);
  assert.ok(RULES.cellRules.some((r) => r.id === "frost-on-grass"));
  agree([a, b]);
  a.ls.submit({ k: "mods", ids: [] });
  await run([a, b], 10);
  assert.equal(RULES.sim.push.slideCells, 3);
  agree([a, b]);
});

test("a broken mod is refused and the rules stay as they were", () => {
  const w = loadScenario(SCENARIOS[0]!, 1);
  const err = applyMods(w, ["bad"], [{ reactions: { add: [{ id: "x", element: "fire", do: [{ teleportEverything: true } as never] }] } }]);
  assert.match(err!, /unknown effect/);
  assert.deepEqual(w.mods, []);
});

test("every example patch in docs/MODDING.md passes the engine's checks", async () => {
  const { readFile } = await import("node:fs/promises");
  const { composeRules, validateRules } = await import("../src/rules.ts");
  const md = await readFile(new URL("../../docs/MODDING.md", import.meta.url), "utf8");
  const blocks = [...md.matchAll(/```json\r?\n([\s\S]*?)```/g)].map((m) => m[1]!);
  assert.ok(blocks.length >= 4, "found the examples");
  for (const b of blocks) assert.deepEqual(validateRules(composeRules([JSON.parse(b)])), [], b.slice(0, 60));
});

test("every example mod in rules/mods passes the engine's checks", async () => {
  const { readFile, readdir } = await import("node:fs/promises");
  const { composeRules, validateRules } = await import("../src/rules.ts");
  const dir = new URL("../../rules/mods/", import.meta.url);
  const files = (await readdir(dir)).filter((f) => f.endsWith(".json"));
  assert.ok(files.length >= 3, "found the examples");
  for (const f of files) {
    // Saved as {"title", "idea", "patch"}, which the Experiments panel also accepts.
    const mod = JSON.parse(await readFile(new URL(f, dir), "utf8")) as { patch: ModPatch };
    assert.deepEqual(validateRules(composeRules([mod.patch])), [], f);
  }
});
