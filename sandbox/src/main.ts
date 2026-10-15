// Page wiring: tools, pointer input, the play loop, and the side panels.
import { RULES, type CellRule } from "./rules.ts";
import { step } from "./sim.ts";
import { PAINT_OTHER, PAINT_TERRAIN, carry, castAt, paint, placeCritter, previewCells, spellById } from "./actions.ts";
import { SCENARIOS, type Scenario, goalState, loadScenario } from "./scenarios.ts";
import { draw, elementColor, type View } from "./render.ts";
import { defOf, walk } from "./entities.ts";
import { type Dir, type World, DIR_NAMES, cellAt, critterAt, dirFrom, entityAt, playerOf, terrainDef } from "./world.ts";

type Tab = "spells" | "critters" | "build";
type Tool = { tab: Tab; id: string };

const $ = <T extends HTMLElement>(id: string) => document.getElementById(id) as T;
const canvas = $<HTMLCanvasElement>("board");
const ctx = canvas.getContext("2d")!;

const store = {
  get(key: string): string | null { try { return localStorage.getItem(key); } catch { return null; } },
  set(key: string, v: string) { try { localStorage.setItem(key, v); } catch { /* private mode */ } },
};

let scenario: Scenario = SCENARIOS.find((s) => s.id === store.get("sandbox.scenario")) ?? SCENARIOS[0]!;
let world: World = loadScenario(scenario, freshSeed());
let tool: Tool = { tab: "spells", id: "fire-bolt" };
let playing = true;
let speed = 1;
let hover: [number, number] | null = null;
let carryFrom: [number, number] | null = null;
let notice = "";
let lastLogLen = -1, lastLogTick = -1;
const found = new Set<string>(JSON.parse(store.get("sandbox.routes") ?? "[]") as string[]);

function freshSeed() { return (Date.now() ^ (performance.now() * 1000)) >>> 0; }

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
      chip(c.name, tool.id === c.id, () => setTool({ tab: "critters", id: c.id }), glyph(c.glyph), world.critters.some((k) => k.kind === c.id) ? "out" : "")));
  } else {
    const ids = [...PAINT_TERRAIN, ...Object.keys(RULES.entities), ...PAINT_OTHER];
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
    const s = spellById(tool.id)!;
    h.textContent = s.name;
    meta.textContent = `${LEVEL(s.level)} · ${s.element ?? s.effect} · ${s.shape}`;
    p.textContent = s.blurb;
    hint.textContent = s.effect === "carry"
      ? carryFrom ? "Now tap where to set it down." : "Tap something light to pick it up."
      : s.effect === "teleport" ? "Click an empty square you can see, up to 6 squares away."
      : s.effect === "levitate" ? "Click a thing to float it. Click the wizard to float yourself over pits."
      : s.origin === "self" ? (s.shape === "burst" ? "Click anywhere: it goes off all around the wizard." : "Click toward where you want it: it shoots out of the wizard in that direction.")
      : "Click a square to cast it there.";
  } else if (tool.tab === "critters") {
    const c = RULES.critters.find((k) => k.id === tool.id)!;
    h.textContent = `${c.glyph} ${c.name}`;
    meta.textContent = c.mode === "act" ? `${c.element} · acts once` : `${c.element} · every ${c.every} ticks`;
    p.textContent = c.blurb;
    hint.textContent = c.mode === "act"
      ? "Tap a rock to dig through it, a pit to fill it, or open ground to raise a pillar."
      : `You have one. Click to set it down, click it again to pick it up.${c.directional ? " It faces away from the wizard." : ""}`;
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
    n.className = "hint";
    n.style.color = "var(--hurt)";
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
let goalWasDone = false;

function renderScenarios() {
  $("scenarios").replaceChildren(...SCENARIOS.map((s) => {
    const b = document.createElement("button");
    b.type = "button";
    b.textContent = s.name;
    b.setAttribute("aria-pressed", String(s.id === scenario.id));
    b.onclick = () => load(s);
    return b;
  }));
}

function renderGoal() {
  const g = goalState(world, scenario);
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

function load(s: Scenario) {
  scenario = s;
  store.set("sandbox.scenario", s.id);
  world = loadScenario(s, freshSeed());
  goalWasDone = false;
  carryFrom = null;
  lastLogLen = -1;
  renderScenarios();
  renderGoal();
  renderChips();
  resize();
}

// ---------- panels ----------
function renderLog() {
  const last = world.log[world.log.length - 1];
  if (world.log.length === lastLogLen && (last?.tick ?? -1) === lastLogTick) return;
  lastLogLen = world.log.length;
  lastLogTick = last?.tick ?? -1;
  const ol = $("log");
  if (!world.log.length) {
    const li = document.createElement("li");
    li.className = "empty";
    li.textContent = "Nothing yet. Cast something.";
    ol.replaceChildren(li);
    return;
  }
  ol.replaceChildren(...world.log.slice(-60).reverse().map((e) => {
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
  const [x, y] = hover;
  const c = cellAt(world, x, y);
  if (!c) return;
  const bits = [terrainDef(c).name];
  if (c.water > 0) bits.push(c.frozen ? `ice (${c.water})` : `water ${c.water}/${RULES.sim.water.maxLevel}`);
  if (c.cover) bits.push(RULES.covers[c.cover]!.name.toLowerCase());
  if (c.fire) bits.push(`fire ${c.fire} (fuel ${c.fuel})`);
  if (c.gas) bits.push(`${c.gas} ${c.gasAmt}`);
  if (c.charge) bits.push(`charge ${c.charge}`);
  const m = world.machines[y * world.w + x];
  if (m && RULES.machines[c.t]) bits.push(m.powered ? "POWERED" : `level ${m.level}`);
  const e = entityAt(world, x, y);
  if (e) {
    const st = Object.keys(e.status).filter((k) => k !== "fallen");
    bits.push(`${defOf(e).name} ${e.hp >= 999 ? "" : `${e.hp}hp`}${st.length ? ` [${st.join(", ")}]` : ""}${e.status.fallen ? " in the pit" : ""}`.trim());
  }
  const cr = critterAt(world, x, y);
  if (cr) bits.push(RULES.critters.find((k) => k.id === cr.kind)!.name.toLowerCase());
  el.textContent = `(${x}, ${y})  ${bits.join(" · ")}`;
}

function renderReadout() {
  const p = playerOf(world);
  const me = p ? `Wizard ${p.hp}/${defOf(p).hp} HP` : "Wizard down";
  const wind = world.wind ? `wind ${DIR_NAMES[world.wind.dir]}` : "still air";
  $("readout").textContent = `${me} · ${wind} · ${(world.tick / RULES.sim.ticksPerSecond).toFixed(1)}s`;
}

function renderTransport() {
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
  const wrap = $("board-wrap");
  const avail = wrap.clientWidth - 16;
  cs = Math.max(8, Math.min(30, Math.floor(avail / world.w)));
  const dpr = window.devicePixelRatio || 1;
  canvas.width = world.w * cs * dpr;
  canvas.height = world.h * cs * dpr;
  canvas.style.width = `${world.w * cs}px`;
  canvas.style.height = `${world.h * cs}px`;
  ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
}
new ResizeObserver(resize).observe($("board-wrap"));

function cellFromEvent(ev: PointerEvent): [number, number] | null {
  const r = canvas.getBoundingClientRect();
  const x = Math.floor(((ev.clientX - r.left) / r.width) * world.w);
  const y = Math.floor(((ev.clientY - r.top) / r.height) * world.h);
  return x >= 0 && y >= 0 && x < world.w && y < world.h ? [x, y] : null;
}
let down: [number, number] | null = null;
let painted = new Set<string>();

canvas.addEventListener("pointerdown", (ev) => {
  const c = cellFromEvent(ev);
  canvas.focus({ preventScroll: true });
  if (!c) return;
  canvas.setPointerCapture(ev.pointerId);
  down = c;
  hover = c;
  if (tool.tab === "build") {
    painted = new Set([c.join()]);
    paint(world, tool.id, c[0], c[1]);
  }
});
canvas.addEventListener("pointermove", (ev) => {
  const c = cellFromEvent(ev);
  hover = c;
  if (c && down && tool.tab === "build" && !painted.has(c.join())) {
    painted.add(c.join());
    paint(world, tool.id, c[0], c[1]);
  }
});
canvas.addEventListener("pointerleave", () => { if (!down) hover = null; });
canvas.addEventListener("pointerup", (ev) => {
  const at = cellFromEvent(ev) ?? down;
  down = null;
  if (!at || tool.tab === "build") return;
  act(at);
  if (ev.pointerType !== "mouse") hover = at;
});

function act([x, y]: [number, number]) {
  notice = "";
  if (tool.tab === "spells") {
    if (tool.id === "mage-hand") {
      if (!carryFrom) {
        if (entityAt(world, x, y)) carryFrom = [x, y];
        else notice = "Nothing there to lift. Click a crate, barrel or goblin.";
      } else {
        notice = carry(world, carryFrom[0], carryFrom[1], x, y) ?? "";
        carryFrom = null;
      }
    } else {
      notice = castAt(world, tool.id, x, y) ?? "";
    }
  } else if (tool.tab === "critters") {
    const p = playerOf(world);
    const facing: Dir = (p && dirFrom(p.x, p.y, x, y)) ?? 1;
    placeCritter(world, tool.id, x, y, facing);
    renderChips();
  }
  renderDetail();
}

function move(d: Dir) {
  const p = playerOf(world);
  if (!p) {
    notice = "You're down. Press Reset to try again.";
  } else {
    notice = walk(world, p, d) ?? "";
  }
  renderDetail();
}

// ---------- controls ----------
$("play").onclick = () => { playing = !playing; renderTransport(); };
$("step").onclick = () => { playing = false; step(world); renderTransport(); };
$("reset").onclick = () => load(scenario);
document.querySelectorAll<HTMLButtonElement>("[data-speed]").forEach((b) => b.onclick = () => { speed = Number(b.dataset.speed); renderTransport(); });
document.querySelectorAll<HTMLButtonElement>("[data-dir]").forEach((b) => b.onclick = () => move(Number(b.dataset.dir) as Dir));
const KEY_DIR: Record<string, Dir> = { ArrowUp: 0, w: 0, ArrowRight: 1, d: 1, ArrowDown: 2, s: 2, ArrowLeft: 3, a: 3 };
window.addEventListener("keydown", (ev) => {
  if (ev.target instanceof HTMLInputElement || ev.metaKey || ev.ctrlKey || ev.altKey) return;
  if (ev.key in KEY_DIR) { move(KEY_DIR[ev.key]!); ev.preventDefault(); }
  else if (/^[1-9]$/.test(ev.key) && tool.tab === "spells") {
    const s = RULES.spells[Number(ev.key) - 1];
    if (s) setTool({ tab: "spells", id: s.id });
  }
  else if (ev.key === " ") { playing = !playing; renderTransport(); ev.preventDefault(); }
  else if (ev.key === ".") { playing = false; step(world); renderTransport(); }
  else if (ev.key === "r") load(scenario);
});

// ---------- loop ----------
let acc = 0, last = performance.now(), lastGoalTick = -1;
function frame(now: number) {
  const dt = Math.min(250, now - last);
  last = now;
  if (playing) {
    acc += dt;
    const interval = 1000 / (RULES.sim.ticksPerSecond * speed);
    let n = 0;
    while (acc >= interval && n++ < 6) {
      step(world);
      acc -= interval;
    }
    if (n >= 6) acc = 0;
  }
  const preview = hover && tool.tab === "spells" && tool.id !== "mage-hand"
    ? previewCells(world, tool.id, hover[0], hover[1])
    : hover ? [hover] : [];
  const spell = tool.tab === "spells" ? spellById(tool.id) : undefined;
  const critter = tool.tab === "critters" ? RULES.critters.find((c) => c.id === tool.id) : undefined;
  const view: View = {
    cs, time: now, hover, preview,
    previewColor: spell ? (spell.element ? elementColor(spell.element) : "#c9b8ff") : critter ? critter.color : "#ffffff",
    selected: carryFrom ? entityAt(world, carryFrom[0], carryFrom[1])?.id ?? null : null,
  };
  draw(ctx, world, view);
  renderLog();
  renderInspect();
  renderReadout();
  if (world.tick !== lastGoalTick) {
    lastGoalTick = world.tick;
    renderGoal();
  }
  requestAnimationFrame(frame);
}

const howto = $<HTMLDetailsElement>("howto");
if (store.get("sandbox.howto") === "closed") howto.open = false;
howto.addEventListener("toggle", () => store.set("sandbox.howto", howto.open ? "open" : "closed"));

renderScenarios();
renderRules();
setTool(tool);
renderGoal();
renderTransport();
resize();
requestAnimationFrame(frame);
