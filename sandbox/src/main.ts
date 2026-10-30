// Page wiring: tools, input, the session (solo or a room), and the panels.
// Every change to the world is an Intent submitted to the session, so solo
// and multiplayer run exactly the same code.
import { RULES, type CellRule } from "./rules.ts";
import { PAINT_OTHER, PAINT_TERRAIN, previewCells, spellById } from "./actions.ts";
import { SCENARIOS, type Scenario, goalState } from "./scenarios.ts";
import { draw, elementColor, type View } from "./render.ts";
import { defOf } from "./entities.ts";
import { dayOf, minuteOfDay, minutesAt } from "./sky.ts";
import { type Intent } from "./intents.ts";
import { Lockstep } from "./net.ts";
import { type Db, type Lobby, LocalTransport, RoomTransport, joinRoom, randomCode } from "./room.ts";
import { Lab, type Sample, type UserCap } from "./lab.ts";
import { loadScenario } from "./scenarios.ts";
import { type Dir, type World, DIR_NAMES, cellAt, clockTick, critterAt, entityAt, hashWorld, playerOf, terrainDef, windDir } from "./world.ts";

type Tab = "spells" | "critters" | "build";
type Tool = { tab: Tab; id: string };

const $ = <T extends HTMLElement>(id: string) => document.getElementById(id) as T;
const canvas = $<HTMLCanvasElement>("board");
const ctx = canvas.getContext("2d")!;

const store = {
  get(key: string): string | null { try { return localStorage.getItem(key); } catch { return null; } },
  set(key: string, v: string) { try { localStorage.setItem(key, v); } catch { /* private mode */ } },
};
const freshSeed = () => Math.floor(Math.random() * 2 ** 31);

// ---------- platform capabilities (absent outside the published page) ----------
type ClaudeGlobal = { use(name: string): Promise<unknown> };
const claude = (globalThis as unknown as { claude?: ClaudeGlobal }).claude;
const use = <T>(name: string): Promise<T | null> => (claude ? (claude.use(name) as Promise<T | null>).catch(() => null) : Promise.resolve(null));
let lobby: Lobby | null = null;
let db: Db | null = null;
let user: (UserCap & { profiles(ids: string[]): Promise<Record<string, { name: string }>> }) | null = null;
let roomChecked = false;

// ---------- session ----------
const startScenario = SCENARIOS.find((s) => s.id === store.get("sandbox.scenario")) ?? SCENARIOS[0]!;
let transport: LocalTransport | RoomTransport = new LocalTransport();
let session: Lockstep = makeSession(loadScenario(startScenario, freshSeed()), transport, false);
let names: Record<string, string> = {};

function makeSession(world: World, t: LocalTransport | RoomTransport, joinExisting: boolean): Lockstep {
  const s = new Lockstep(t, world, (ids) => lab.source(ids), { joinExisting });
  s.onNotice = (text) => { notice = text; renderDetail(); };
  s.onWorld = () => { lastLogLen = -1; goalWasDone = false; renderAll(); };
  s.onState = () => renderRoom();
  return s;
}
const world = (): World => session.world;
const me = (): string => transport.me;
const submit = (it: Intent) => session.submit(it);
const scenarioNow = (): Scenario => SCENARIOS.find((s) => s.id === world().scenarioId) ?? SCENARIOS[0]!;

/** A stable colour per player, from their room label. */
function colorOf(owner: string): string {
  if (owner === "you") return "#6a50c8";
  let h = 0;
  for (const ch of owner) h = (h * 31 + ch.charCodeAt(0)) >>> 0;
  return `hsl(${h % 360} 62% 52%)`;
}

let tool: Tool = { tab: "spells", id: "fire-bolt" };
let hover: [number, number] | null = null;
let carryFrom: [number, number] | null = null;
let notice = "";
let lastLogLen = -1, lastLogTick = -1;
let goalWasDone = false;
const found = new Set<string>(JSON.parse(store.get("sandbox.routes") ?? "[]") as string[]);

// ---------- tools ----------
const LEVEL = (n: number) => (n === 0 ? "cantrip" : n === 1 ? "1st" : n === 2 ? "2nd" : `${n}th`);
const BUILD_LABEL: Record<string, string> = { water: "Water", oil: "Oil", erase: "Erase" };

function renderTabs() {
  const tabs: [Tab, string][] = [["spells", "Spells"], ["critters", "Critters"], ["build", "Build"]];
  $("tabs").replaceChildren(...tabs.map(([id, label]) => {
    const b = document.createElement("button");
    b.type = "button";
    b.role = "tab";
    b.textContent = label;
    b.setAttribute("aria-selected", String(tool.tab === id));
    b.onclick = () => {
      const first = id === "spells" ? RULES.spells[0]!.id : id === "critters" ? RULES.critters[0]!.id : "grass";
      setTool({ tab: id, id: first });
    };
    return b;
  }));
}

function chip(label: string, pressed: boolean, onClick: () => void, lead?: HTMLElement, small?: string) {
  const b = document.createElement("button");
  b.type = "button";
  b.className = "chip";
  b.setAttribute("aria-pressed", String(pressed));
  if (lead) b.append(lead);
  b.append(label);
  if (small) {
    const s = document.createElement("small");
    s.textContent = small;
    b.append(s);
  }
  b.onclick = onClick;
  return b;
}
const dot = (color: string) => {
  const d = document.createElement("span");
  d.className = "dot";
  d.style.background = color;
  return d;
};
const glyph = (g: string) => {
  const d = document.createElement("span");
  d.className = "glyph";
  d.textContent = g;
  return d;
};

function renderChips() {
  const host = $("chips");
  if (tool.tab === "spells") {
    host.replaceChildren(...RULES.spells.map((s) =>
      chip(s.name, tool.id === s.id, () => setTool({ tab: "spells", id: s.id }), dot(elementColor(s.element ?? (s.effect === "levitate" ? "air" : "thunder"))), LEVEL(s.level))));
  } else if (tool.tab === "critters") {
    host.replaceChildren(...RULES.critters.map((c) =>
      chip(c.name, tool.id === c.id, () => setTool({ tab: "critters", id: c.id }), glyph(c.glyph), world().critters.some((k) => k.kind === c.id) ? "out" : "")));
  } else {
    const ids = [...PAINT_TERRAIN.filter((t) => RULES.terrain[t]), ...Object.keys(RULES.terrain).filter((t) => !PAINT_TERRAIN.includes(t) && !["bedrock", "gate_open", "stump", "deadfall", "rubble", "pillar", "rock", "flesh", "bone", "chitin"].includes(t)),
      ...Object.keys(RULES.entities).filter((e) => !RULES.entities[e]!.player), ...PAINT_OTHER];
    host.replaceChildren(...ids.map((id) => {
      const name = BUILD_LABEL[id] ?? RULES.terrain[id]?.name ?? RULES.entities[id]?.name ?? id;
      const color = id === "water" ? "#3480ce" : id === "oil" ? "#24122f" : id === "erase" ? "transparent" : id in RULES.entities ? "#c08d50" : TERRAIN_SWATCH[id] ?? "#999";
      return chip(name.replace(" (shut)", ""), tool.id === id, () => setTool({ tab: "build", id }), dot(color));
    }));
  }
}
const TERRAIN_SWATCH: Record<string, string> = {
  dirt: "#a7895f", grass: "#7aa956", brush: "#3e6229", tree: "#355f2c", stone: "#8b8d94", wood: "#b47a43", pit: "#2b2522",
  metal: "#9aa6b2", gate: "#3d3a36", boiler: "#b8682e", plate: "#7d7f86", wheel: "#c99a5b", coil: "#c27a3a", windmill: "#efe6d2",
};

function renderDetail() {
  const el = $("detail");
  const h = document.createElement("h3");
  const meta = document.createElement("div");
  meta.className = "meta";
  const p = document.createElement("p");
  const hint = document.createElement("p");
  hint.className = "hint";
  if (tool.tab === "spells") {
    const s = spellById(tool.id);
    if (!s) return setTool({ tab: "spells", id: RULES.spells[0]!.id });
    h.textContent = s.name;
    meta.textContent = `${LEVEL(s.level)} · ${s.element ?? s.effect} · ${s.shape}`;
    p.textContent = s.blurb;
    hint.textContent = s.effect === "carry"
      ? carryFrom ? "Now click where to set it down." : "Click something light to pick it up."
      : s.effect === "teleport" ? `Click an empty square you can see, up to ${s.range ?? 6} squares away.`
      : s.effect === "levitate" ? "Click a thing to float it. Click your wizard to float yourself over pits."
      : s.effect === "shapeshift" ? "Taking an animal's shape needs the 3D game's animals: here the cast is only logged."
      : s.origin === "self" ? (s.shape === "burst" ? "Click anywhere: it goes off all around your wizard." : "Click toward where you want it: it shoots out of your wizard in that direction.")
      : "Click a square to cast it there.";
  } else if (tool.tab === "critters") {
    const c = RULES.critters.find((k) => k.id === tool.id);
    if (!c) return setTool({ tab: "critters", id: RULES.critters[0]!.id });
    h.textContent = `${c.glyph} ${c.name}`;
    meta.textContent = c.mode === "act" ? `${c.element} · acts once` : `${c.element} · every ${c.every} ticks`;
    p.textContent = c.blurb;
    hint.textContent = c.mode === "act"
      ? "Click a rock to dig through it, a pit to fill it, or open ground to raise a pillar."
      : `There's one of each. Click to set it down, click it again to pick it up.${c.directional ? " It faces away from your wizard." : ""}`;
  } else {
    const t = RULES.terrain[tool.id], m = RULES.machines[tool.id], e = RULES.entities[tool.id];
    h.textContent = BUILD_LABEL[tool.id] ?? t?.name ?? e?.name ?? tool.id;
    meta.textContent = m ? "machine" : e ? `thing · mass ${e.mass} · ${e.hp >= 999 ? "unbreakable" : `${e.hp} HP`}` : t ? t.tags.join(" · ") : "paint";
    p.textContent = m ? `${m.how} Any powered machine opens every gate.` : tool.id === "erase" ? "Resets a square to bare dirt and removes whatever stood there." : tool.id === "water" ? "Adds a step of water. It runs downhill into pits." : "";
    hint.textContent = "Click or drag on the board to paint.";
  }
  const parts: HTMLElement[] = [h, meta];
  if (p.textContent) parts.push(p);
  if (notice) {
    const n = document.createElement("p");
    n.className = "hint notice";
    n.textContent = notice;
    parts.push(n);
  }
  parts.push(hint);
  el.replaceChildren(...parts);
}

function setTool(t: Tool) {
  tool = t;
  carryFrom = null;
  notice = "";
  renderTabs();
  renderChips();
  renderDetail();
}

// ---------- scenario + goal ----------
const ROUTES: [string, string][] = [
  ["Steam boiler", "Boiler"], ["Pressure plate", "Plate"], ["Water wheel", "Wheel"],
  ["Lightning coil", "Coil"], ["Windmill", "Windmill"], ["tunnel", "Tunnel"], ["blink", "Misty Step"],
];

function renderScenarios() {
  const cur = world().scenarioId;
  $("scenarios").replaceChildren(...SCENARIOS.map((s) => {
    const b = document.createElement("button");
    b.type = "button";
    b.textContent = s.name;
    b.setAttribute("aria-pressed", String(s.id === cur));
    b.onclick = () => { store.set("sandbox.scenario", s.id); submit({ k: "load", id: s.id, seed: freshSeed() }); };
    return b;
  }));
}

function renderGoal() {
  const scenario = scenarioNow();
  const g = goalState(world(), scenario);
  if (g?.done && !goalWasDone && scenario.id === "steam-gate") {
    found.add(g.via ?? "");
    store.set("sandbox.routes", JSON.stringify([...found]));
  }
  goalWasDone = g?.done ?? false;
  const h = document.createElement("h2");
  h.textContent = scenario.name;
  const p = document.createElement("p");
  p.textContent = scenario.intro;
  const kids: HTMLElement[] = [h, p];
  if (g) {
    const prog = document.createElement("div");
    prog.className = `progress${g.done ? " done" : ""}`;
    prog.textContent = g.done ? `${g.text} Reset to try another way.` : g.text;
    kids.push(prog);
  }
  if (scenario.id === "steam-gate") {
    const r = document.createElement("div");
    r.className = "routes";
    r.setAttribute("aria-label", "Ways found");
    for (const [key, label] of ROUTES) {
      const s = document.createElement("span");
      s.className = `route${found.has(key) ? " found" : ""}`;
      s.textContent = `${found.has(key) ? "✓ " : ""}${label}`;
      r.append(s);
    }
    kids.push(r);
  }
  $("goal").replaceChildren(...kids);
}

// ---------- multiplayer ----------
let joining = false;
let roomError = "";

async function goOnline(code: string) {
  if (!lobby || joining) return;
  code = code.trim().toLowerCase().replace(/[^a-z0-9_.-]/g, "-").slice(0, 40);
  if (!/^[a-z0-9]/.test(code)) {
    roomError = "Room codes start with a letter or number.";
    return renderRoom();
  }
  joining = true;
  roomError = "";
  renderRoom();
  try {
    const room = await joinRoom(lobby, code);
    const rt = new RoomTransport(room, db, room.me);
    rt.decorate({ uid: (await user?.id().catch(() => null)) ?? null });
    // Hand our wizard to our room identity; a host will replace the world anyway.
    const w = world();
    const mine = playerOf(w, transport.me);
    if (mine) mine.owner = rt.me;
    transport = rt;
    session = makeSession(w, rt, true);
    room.onPeers(() => { void resolveNames(); renderRoom(); });
    void lobby.presence({ inRoom: code }).catch(() => {});
  } catch (e) {
    roomError = (e as { code?: string }).code === "not_permitted"
      ? "This page isn't shared with you at Contributor level, so you can't join rooms."
      : (e as Error).message || "Couldn't join that room.";
  } finally {
    joining = false;
    renderAll();
  }
}

async function goOffline() {
  if (!(transport instanceof RoomTransport)) return;
  const old = transport;
  const w = world();
  w.entities = w.entities.filter((e) => !e.owner || e.owner === old.me);
  const mine = playerOf(w, old.me);
  if (mine) mine.owner = "you";
  transport = new LocalTransport();
  session = makeSession(w, transport, false);
  await old.leave();
  void lobby?.presence({ inRoom: null }).catch(() => {});
  renderAll();
}

async function resolveNames() {
  if (!(transport instanceof RoomTransport) || !user) return;
  const peers = transport.room.peers();
  const ids = [...new Set(peers.map((p) => p.by).filter((b): b is string => !!b))];
  if (!ids.length) return;
  const ps = await user.profiles(ids).catch(() => ({} as Record<string, { name: string }>));
  for (const p of peers) if (p.by) names[p.peer] = ps[p.by]?.name || "";
  renderRoom();
}

function renderRoom() {
  const host = $("room");
  const kids: Node[] = [];
  const h = document.createElement("h2");
  h.textContent = "Play together";
  kids.push(h);
  const line = document.createElement("p");
  line.className = "hint";
  if (!roomChecked) {
    line.textContent = "Checking whether this page can host rooms...";
    kids.push(line);
    return host.replaceChildren(...kids);
  }
  if (!lobby) {
    line.textContent = "Rooms work on the published page, for people it's shared with at Contributor level or above. Right now you're playing alone.";
    kids.push(line);
    return host.replaceChildren(...kids);
  }
  if (transport instanceof RoomTransport) {
    const peers = transport.room.peers().filter((p) => p.kind === "viewer");
    const st = session.state();
    const status = st === "syncing" ? "catching up with the host..." : st === "hosting" || st === "solo" ? "you're hosting" : "in sync";
    line.innerHTML = "";
    line.append("Room ", Object.assign(document.createElement("strong"), { textContent: transport.code }), ` · ${peers.length} here · ${status}`);
    kids.push(line);
    const ul = document.createElement("ul");
    ul.className = "players";
    const hostId = session.host();
    for (const p of peers) {
      const li = document.createElement("li");
      li.append(dot(colorOf(p.peer)), names[p.peer] || "A player");
      const tags = [p.sameTab ? "you" : "", p.peer === hostId ? "host" : ""].filter(Boolean).join(", ");
      if (tags) li.append(Object.assign(document.createElement("small"), { textContent: tags }));
      ul.append(li);
    }
    kids.push(ul);
    const leave = Object.assign(document.createElement("button"), { type: "button", className: "btn", textContent: "Leave room" });
    leave.onclick = () => void goOffline();
    kids.push(leave);
  } else {
    line.textContent = "Start a room and give your friends the code, or join theirs. Everyone in a room plays the same board.";
    kids.push(line);
    const row = document.createElement("div");
    row.className = "row";
    const input = Object.assign(document.createElement("input"), { type: "text", id: "room-code", placeholder: "Room code, e.g. frost-4821", autocomplete: "off" });
    const join = Object.assign(document.createElement("button"), { type: "button", className: "btn", textContent: joining ? "Joining..." : "Join", disabled: joining });
    join.onclick = () => void goOnline(input.value);
    input.onkeydown = (ev) => { if (ev.key === "Enter") void goOnline(input.value); };
    const start = Object.assign(document.createElement("button"), { type: "button", className: "btn primary", textContent: "Start a room", disabled: joining });
    start.onclick = () => void goOnline(randomCode());
    row.append(input, join, start);
    kids.push(row);
    const open = new Map<string, number>();
    for (const p of lobby.peers()) {
      const r = p.presence.inRoom;
      if (typeof r === "string" && !p.sameTab) open.set(r, (open.get(r) ?? 0) + 1);
    }
    if (open.size) {
      const list = document.createElement("div");
      list.className = "row";
      list.append("Open now: ");
      for (const [code, n] of open) {
        const b = Object.assign(document.createElement("button"), { type: "button", className: "chip-btn", textContent: `${code} (${n})` });
        b.onclick = () => void goOnline(code);
        list.append(b);
      }
      kids.push(list);
    }
  }
  if (roomError) kids.push(Object.assign(document.createElement("p"), { className: "hint notice", textContent: roomError }));
  host.replaceChildren(...kids);
}

// ---------- panels ----------
function renderLog() {
  const w = world();
  const last = w.log[w.log.length - 1];
  if (w.log.length === lastLogLen && (last?.tick ?? -1) === lastLogTick) return;
  lastLogLen = w.log.length;
  lastLogTick = last?.tick ?? -1;
  const ol = $("log");
  if (!w.log.length) {
    const li = document.createElement("li");
    li.className = "empty";
    li.textContent = "Nothing yet. Cast something.";
    ol.replaceChildren(li);
    return;
  }
  ol.replaceChildren(...w.log.slice(-60).reverse().map((e) => {
    const li = document.createElement("li");
    li.className = e.kind;
    const text = document.createElement("span");
    text.textContent = e.count > 1 ? `${e.text} ×${e.count}` : e.text;
    const t = document.createElement("span");
    t.className = "t";
    t.textContent = `${(e.tick / RULES.sim.ticksPerSecond).toFixed(1)}s`;
    li.append(text, t);
    return li;
  }));
}

function renderInspect() {
  const el = $("inspect");
  if (!hover) return;
  const w = world();
  const [x, y] = hover;
  const c = cellAt(w, x, y);
  if (!c) return;
  const bits = [terrainDef(c).name];
  if (c.water > 0) bits.push(c.frozen ? `ice (${c.water})` : `water ${c.water}/${RULES.sim.water.maxLevel}`);
  if (c.cover) bits.push(RULES.covers[c.cover]!.name.toLowerCase());
  if (c.fire) bits.push(`fire ${c.fire} (fuel ${c.fuel})`);
  if (c.gas) bits.push(`${c.gas} ${c.gasAmt}`);
  if (c.charge) bits.push(`charge ${c.charge}`);
  const m = w.machines[y * w.w + x];
  if (m && RULES.machines[c.t]) bits.push(m.powered ? "POWERED" : `level ${m.level}`);
  const e = entityAt(w, x, y);
  if (e) {
    const st = Object.keys(e.status).filter((k) => k !== "fallen");
    const who = e.owner ? (e.owner === me() ? "Your wizard" : `${names[e.owner] || "A friend"}'s wizard`) : defOf(e).name;
    bits.push(`${who} ${e.hp >= 999 ? "" : `${e.hp}hp`}${st.length ? ` [${st.join(", ")}]` : ""}${e.status.fallen ? " in the pit" : ""}`.trim());
  }
  const cr = critterAt(w, x, y);
  if (cr) bits.push(RULES.critters.find((k) => k.id === cr.kind)?.name.toLowerCase() ?? cr.kind);
  el.textContent = `(${x}, ${y})  ${bits.join(" · ")}`;
}

function renderReadout() {
  const w = world();
  const p = playerOf(w, me());
  const mine = p ? `Wizard ${p.hp}/${defOf(p).hp} HP` : "Wizard down";
  const dir = windDir(w);
  const wind = dir >= 0 ? `wind ${DIR_NAMES[dir]}` : "still air";
  const minutes = minutesAt(RULES.sim, clockTick(w)), m = minuteOfDay(minutes);
  const clock = `day ${dayOf(minutes)} ${String(Math.floor(m / 60)).padStart(2, "0")}:${String(m % 60).padStart(2, "0")} · ${w.weather.kind}`;
  const mods = w.mods.length ? ` · ${w.mods.length} mod${w.mods.length > 1 ? "s" : ""} on` : "";
  $("readout").textContent = `${mine} · ${clock} · ${wind} · ${(w.tick / RULES.sim.ticksPerSecond).toFixed(1)}s${mods}`;
}

let shownCtl = "";
function renderTransport() {
  const { playing, speed } = world().ctl;
  const key = `${playing}|${speed}`;
  if (key === shownCtl) return;
  shownCtl = key;
  $("play").textContent = playing ? "Pause" : "Play";
  document.querySelectorAll<HTMLButtonElement>("[data-speed]").forEach((b) => b.setAttribute("aria-pressed", String(Number(b.dataset.speed) === speed)));
}

// ---------- rules table ----------
function describeCond(r: CellRule): string {
  const c = r.if;
  if (!c) return "always";
  const out: string[] = [];
  if (c.cover) out.push(`on ${c.cover.join(" or ")}`);
  if (c.fire === true) out.push("burning");
  if (typeof c.fire === "number") out.push(`fire ≥ ${c.fire}`);
  if (c.fireBelow !== undefined) out.push(`fire < ${c.fireBelow}`);
  if (c.water === true) out.push("wet (unfrozen water)");
  if (c.frozen) out.push("ice");
  if (c.gas) out.push(c.gas === "any" ? "any vapour" : `${c.gas} present`);
  if (c.terrainTag) out.push(`ground is ${c.terrainTag}`);
  if (c.notTerrainTag) out.push(`ground isn't ${c.notTerrainTag}`);
  if (c.conductive !== undefined) out.push(c.conductive ? "conductive (water or metal)" : "not conductive");
  return out.join(", ");
}
function describeDo(r: CellRule): string {
  const words = r.do.map((e) => {
    if (e.ignite !== undefined) return e.ignite === "cover" ? "ignite the spill" : `ignite (${e.ignite})`;
    if (e.boil) return "boil a unit to steam";
    if (e.melt) return "melt";
    if (e.freeze) return "freeze";
    if (e.extinguish) return "put out fire";
    if (e.condense) return "condense steam to water";
    if (e.addWater !== undefined) return "add water";
    if (e.setCover !== undefined) return `spill ${e.setCover}`;
    if (e.spreadCover) return `splash to ${e.spreadCover} neighbours`;
    if (e.break) return "break to rubble";
    if (e.setTerrain) return `fill with ${e.setTerrain}`;
    if (e.raise) return `raise a ${e.raise}`;
    if (e.conduct) return "electrify everything conductive that touches";
    if (e.fan) return "fan the fire and push it downwind";
    if (e.pushGas) return "push the vapour along";
    if (e.clearGas) return "scatter the vapour";
    if (e.mark) return `count as ${e.mark} for machines`;
    if (e.gas) return `release ${e.gas}`;
    return "?";
  });
  return (words.join(", ") || "nothing") + (r.stop ? " · stops" : "");
}
function renderRules() {
  const t = $("rules-table");
  const head = document.createElement("tr");
  for (const h of ["Element", "When the square is", "Then", "Log line"]) {
    const th = document.createElement("th");
    th.textContent = h;
    head.append(th);
  }
  const rows = RULES.cellRules.map((r) => {
    const tr = document.createElement("tr");
    const el = document.createElement("td");
    const span = document.createElement("span");
    span.className = "el";
    span.append(dot(elementColor(r.element)), r.element);
    el.append(span);
    const cells = [describeCond(r), describeDo(r), r.log ?? "(silent)"].map((txt) => {
      const td = document.createElement("td");
      td.textContent = txt;
      return td;
    });
    tr.append(el, ...cells);
    return tr;
  });
  const thead = document.createElement("thead");
  thead.append(head);
  const tbody = document.createElement("tbody");
  tbody.append(...rows);
  t.replaceChildren(thead, tbody);
}

// ---------- board input ----------
let cs = 20;
function resize() {
  const w = world();
  const wrap = $("board-wrap");
  const avail = wrap.clientWidth - 16;
  cs = Math.max(8, Math.min(30, Math.floor(avail / w.w)));
  const dpr = window.devicePixelRatio || 1;
  canvas.width = w.w * cs * dpr;
  canvas.height = w.h * cs * dpr;
  canvas.style.width = `${w.w * cs}px`;
  canvas.style.height = `${w.h * cs}px`;
  ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
}
new ResizeObserver(resize).observe($("board-wrap"));

function cellFromEvent(ev: PointerEvent): [number, number] | null {
  const w = world();
  const r = canvas.getBoundingClientRect();
  const x = Math.floor(((ev.clientX - r.left) / r.width) * w.w);
  const y = Math.floor(((ev.clientY - r.top) / r.height) * w.h);
  return x >= 0 && y >= 0 && x < w.w && y < w.h ? [x, y] : null;
}

// Painting: a drag is sent in small batches rather than one message per square.
let down: [number, number] | null = null;
let painted = new Set<string>();
let paintBuffer: [number, number][] = [];
let paintTimer: ReturnType<typeof setTimeout> | null = null;
function flushPaint() {
  if (paintTimer) clearTimeout(paintTimer);
  paintTimer = null;
  if (!paintBuffer.length) return;
  submit({ k: "paint", b: tool.id, cells: paintBuffer });
  paintBuffer = [];
}
function queuePaint(c: [number, number]) {
  if (painted.has(c.join())) return;
  painted.add(c.join());
  paintBuffer.push(c);
  if (paintBuffer.length >= 200) flushPaint();
  else if (!paintTimer) paintTimer = setTimeout(flushPaint, 120);
}

canvas.addEventListener("pointerdown", (ev) => {
  const c = cellFromEvent(ev);
  canvas.focus({ preventScroll: true });
  if (!c) return;
  canvas.setPointerCapture(ev.pointerId);
  down = c;
  hover = c;
  if (tool.tab === "build") {
    painted = new Set();
    queuePaint(c);
  }
});
canvas.addEventListener("pointermove", (ev) => {
  const c = cellFromEvent(ev);
  hover = c;
  if (c && down && tool.tab === "build") queuePaint(c);
});
canvas.addEventListener("pointerleave", () => { if (!down) hover = null; });
canvas.addEventListener("pointerup", (ev) => {
  const at = cellFromEvent(ev) ?? down;
  down = null;
  if (tool.tab === "build") return flushPaint();
  if (!at) return;
  act(at);
  if (ev.pointerType !== "mouse") hover = at;
});

function act([x, y]: [number, number]) {
  notice = "";
  if (tool.tab === "spells") {
    if (tool.id === "mage-hand") {
      if (!carryFrom) {
        if (entityAt(world(), x, y)) carryFrom = [x, y];
        else notice = "Nothing there to lift. Click a crate, barrel or goblin.";
      } else {
        submit({ k: "carry", fx: carryFrom[0], fy: carryFrom[1], tx: x, ty: y });
        carryFrom = null;
      }
    } else {
      submit({ k: "cast", id: tool.id, x, y });
    }
  } else if (tool.tab === "critters") {
    submit({ k: "critter", id: tool.id, x, y });
  }
  renderDetail();
}

function move(d: Dir) {
  notice = "";
  submit({ k: "walk", d });
}

// ---------- controls ----------
$("play").onclick = () => submit({ k: "play", on: !world().ctl.playing });
$("step").onclick = () => { if (world().ctl.playing) submit({ k: "play", on: false }); submit({ k: "step" }); };
$("reset").onclick = () => submit({ k: "load", id: world().scenarioId, seed: freshSeed() });
document.querySelectorAll<HTMLButtonElement>("[data-speed]").forEach((b) => b.onclick = () => submit({ k: "speed", v: Number(b.dataset.speed) }));
document.querySelectorAll<HTMLButtonElement>("[data-dir]").forEach((b) => b.onclick = () => move(Number(b.dataset.dir) as Dir));
const KEY_DIR: Record<string, Dir> = { ArrowUp: 0, w: 0, ArrowRight: 1, d: 1, ArrowDown: 2, s: 2, ArrowLeft: 3, a: 3 };
window.addEventListener("keydown", (ev) => {
  const t = ev.target;
  if (t instanceof HTMLInputElement || t instanceof HTMLTextAreaElement || t instanceof HTMLSelectElement || ev.metaKey || ev.ctrlKey || ev.altKey) return;
  if (ev.key in KEY_DIR) { move(KEY_DIR[ev.key]!); ev.preventDefault(); }
  else if (/^[1-9]$/.test(ev.key) && tool.tab === "spells") {
    const s = RULES.spells[Number(ev.key) - 1];
    if (s) setTool({ tab: "spells", id: s.id });
  }
  else if (ev.key === " ") { submit({ k: "play", on: !world().ctl.playing }); ev.preventDefault(); }
  else if (ev.key === ".") $("step").click();
  else if (ev.key === "r") $("reset").click();
});

// ---------- experiments ----------
const lab = new Lab({
  root: $("lab"),
  db: null, user: null, sample: null,
  active: () => world().mods,
  setActive: (ids) => submit({ k: "mods", ids }),
});

// ---------- loop ----------
let last = performance.now(), lastGoalTick = -1, lastRulesKey = "", lastScenario = "";
function renderAll() {
  renderScenarios();
  renderGoal();
  renderChips();
  renderRules();
  renderDetail();
  renderRoom();
  shownCtl = "";
  renderTransport();
  resize();
}

function frame(now: number) {
  const dt = Math.min(250, now - last);
  last = now;
  session.frame(dt);
  const w = world();
  const rulesKey = w.mods.join("|");
  if (rulesKey !== lastRulesKey || w.scenarioId !== lastScenario) {
    lastRulesKey = rulesKey;
    lastScenario = w.scenarioId;
    renderAll();
  }
  lab.tick();
  const preview = hover && tool.tab === "spells" && tool.id !== "mage-hand"
    ? previewCells(w, tool.id, hover[0], hover[1], me())
    : hover ? [hover] : [];
  const spell = tool.tab === "spells" ? spellById(tool.id) : undefined;
  const critter = tool.tab === "critters" ? RULES.critters.find((c) => c.id === tool.id) : undefined;
  const view: View = {
    cs, time: now, hover, preview, me: me(), colorOf,
    previewColor: spell ? (spell.element ? elementColor(spell.element) : "#c9b8ff") : critter ? critter.color : "#ffffff",
    selected: carryFrom ? entityAt(w, carryFrom[0], carryFrom[1])?.id ?? null : null,
  };
  draw(ctx, w, view);
  renderLog();
  renderInspect();
  renderReadout();
  renderTransport();
  if (w.tick !== lastGoalTick) {
    lastGoalTick = w.tick;
    renderGoal();
  }
  requestAnimationFrame(frame);
}

const howto = $<HTMLDetailsElement>("howto");
if (store.get("sandbox.howto") === "closed") howto.open = false;
howto.addEventListener("toggle", () => store.set("sandbox.howto", howto.open ? "open" : "closed"));

setTool(tool);
renderAll();
requestAnimationFrame(frame);

// Read-only hook for tests and the console: compare boards across tabs.
Object.assign(globalThis, { __sandbox: { hash: () => hashWorld(world()), state: () => session.state(), me, mods: () => world().mods, tick: () => world().tick } });

// Light up the shared features once the platform answers (null outside it).
void (async () => {
  const [lobbyCap, dbCap, userCap, sampleCap] = await Promise.all([
    use<Lobby>("room"), use<Db>("db"), use<typeof user>("user"), use<Sample>("sample"),
  ]);
  lobby = lobbyCap;
  db = dbCap;
  user = userCap;
  roomChecked = true;
  void lab.connect({ db, user, sample: sampleCap });
  lobby?.onPeers(() => { if (!(transport instanceof RoomTransport)) renderRoom(); });
  renderRoom();
})();
