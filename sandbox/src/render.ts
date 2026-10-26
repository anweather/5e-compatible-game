// Canvas renderer. The board keeps one palette in both themes on purpose:
// it is a place, not page chrome.
import { RULES } from "./rules.ts";
import { defOf, isLevitating } from "./entities.ts";
import { type Cell, type Dir, type World, DX, DY } from "./world.ts";

export interface View {
  cs: number; // CSS pixels per cell
  time: number; // ms, for flicker and spin
  hover: [number, number] | null;
  preview: [number, number][];
  previewColor: string;
  /** This page's player, and each player's colour. */
  me: string;
  colorOf: (owner: string) => string;
  selected: number | null; // entity id held by Mage Hand
}

const TERRAIN_COLOR: Record<string, string> = {
  bedrock: "#3a3d44", dirt: "#a7895f", grass: "#7aa956", brush: "#557d3c", tree: "#7aa956", stump: "#a7895f", deadfall: "#7aa956",
  stone: "#8b8d94", pillar: "#a59a84", rubble: "#a7895f", rock: "#9a8f7d", wood: "#b47a43", pit: "#2b2522", metal: "#9aa6b2",
  gate: "#a7895f", gate_open: "#a7895f", boiler: "#a7895f", plate: "#a7895f", wheel: "#2b2522", coil: "#a7895f", windmill: "#a7895f",
};
const ELEMENT_COLOR: Record<string, string> = {
  fire: "#ff8a2a", cold: "#8fd8f5", water: "#3f8fd8", air: "#e8eedd", earth: "#9c7a55",
  lightning: "#ffe14d", grease: "#e3cf7a", oil: "#3a2346", thunder: "#c9b8ff",
};
export const elementColor = (el: string | undefined) => ELEMENT_COLOR[el ?? ""] ?? "#ffffff";

const hash = (x: number, y: number, k = 0) => (((x * 73856093) ^ (y * 19349663) ^ (k * 83492791)) >>> 0) / 4294967296;

export function draw(ctx: CanvasRenderingContext2D, w: World, v: View) {
  const s = v.cs;
  ctx.clearRect(0, 0, w.w * s, w.h * s);
  for (let y = 0; y < w.h; y++) for (let x = 0; x < w.w; x++) drawCell(ctx, w.cells[y * w.w + x]!, x, y, s, v.time, w);
  for (let y = 0; y < w.h; y++) for (let x = 0; x < w.w; x++) drawOverlay(ctx, w.cells[y * w.w + x]!, x, y, s, v.time);
  for (const e of w.entities) drawEntity(ctx, e, s, v.time, v.selected === e.id, v);
  for (const c of w.critters) drawCritter(ctx, c, s, v.time);
  for (let y = 0; y < w.h; y++) for (let x = 0; x < w.w; x++) drawGas(ctx, w.cells[y * w.w + x]!, x, y, s, v.time);
  drawPreview(ctx, v, s);
}

function rect(ctx: CanvasRenderingContext2D, x: number, y: number, s: number, color: string) {
  ctx.fillStyle = color;
  ctx.fillRect(x * s, y * s, s, s);
}

function drawCell(ctx: CanvasRenderingContext2D, c: Cell, x: number, y: number, s: number, t: number, w: World) {
  const px = x * s, py = y * s;
  // A pit or other low ground heaped back up to ground level draws as bare earth, not a hole.
  const low = RULES.terrain[c.t]?.elevation ?? 0;
  const look = low < 0 && low + c.h >= 0 ? "dirt" : c.t;
  rect(ctx, x, y, s, TERRAIN_COLOR[look] ?? "#f0f");
  // Raised ground is lighter and dug ground darker, a shade per step.
  if (c.h !== 0) rect(ctx, x, y, s, c.h > 0 ? `rgba(255,255,255,${0.12 * c.h})` : `rgba(0,0,0,${-0.15 * c.h})`);
  ctx.save();
  switch (look) {
    case "grass":
    case "tree":
      ctx.strokeStyle = "#5f8f41";
      ctx.lineWidth = Math.max(1, s / 16);
      for (let k = 0; k < 3; k++) {
        const gx = px + hash(x, y, k) * s, gy = py + hash(y, x, k) * s;
        ctx.beginPath(); ctx.moveTo(gx, gy + s * 0.18); ctx.lineTo(gx + s * 0.06, gy); ctx.stroke();
      }
      if (c.t === "tree") {
        ctx.fillStyle = "#6b4a2b";
        ctx.fillRect(px + s * 0.42, py + s * 0.55, s * 0.16, s * 0.35);
        ctx.fillStyle = "#355f2c";
        ctx.beginPath(); ctx.arc(px + s / 2, py + s * 0.42, s * 0.42, 0, Math.PI * 2); ctx.fill();
        ctx.fillStyle = "#467a37";
        ctx.beginPath(); ctx.arc(px + s * 0.4, py + s * 0.34, s * 0.2, 0, Math.PI * 2); ctx.fill();
      }
      break;
    case "brush":
      ctx.fillStyle = "#3e6229";
      for (let k = 0; k < 5; k++) {
        ctx.beginPath();
        ctx.arc(px + hash(x, y, k) * s, py + hash(y, x, k + 9) * s, s * 0.16, 0, Math.PI * 2);
        ctx.fill();
      }
      ctx.fillStyle = "#c2523c";
      ctx.fillRect(px + hash(x, y, 4) * s * 0.8, py + hash(x, y, 5) * s * 0.8, s * 0.1, s * 0.1);
      break;
    case "stump":
      ctx.fillStyle = "#7a5530";
      ctx.beginPath(); ctx.arc(px + s / 2, py + s / 2, s * 0.22, 0, Math.PI * 2); ctx.fill();
      break;
    case "deadfall":
      // A fallen trunk lying across the square, its root ball at one end.
      ctx.strokeStyle = "#6b4a2b";
      ctx.lineWidth = s * 0.2;
      ctx.beginPath(); ctx.moveTo(px + s * 0.15, py + s * 0.8); ctx.lineTo(px + s * 0.85, py + s * 0.25); ctx.stroke();
      ctx.fillStyle = "#4e3a26";
      ctx.beginPath(); ctx.arc(px + s * 0.18, py + s * 0.78, s * 0.17, 0, Math.PI * 2); ctx.fill();
      ctx.fillStyle = "#3e6229";
      ctx.beginPath(); ctx.arc(px + s * 0.82, py + s * 0.27, s * 0.18, 0, Math.PI * 2); ctx.fill();
      break;
    case "stone":
    case "pillar":
    case "bedrock":
      ctx.fillStyle = "rgba(255,255,255,0.14)";
      ctx.fillRect(px, py, s, s * 0.14);
      ctx.fillStyle = "rgba(0,0,0,0.22)";
      ctx.fillRect(px, py + s * 0.86, s, s * 0.14);
      if (c.t !== "bedrock") {
        ctx.strokeStyle = "rgba(0,0,0,0.18)";
        ctx.strokeRect(px + 0.5, py + 0.5, s - 1, s * 0.5);
      }
      break;
    case "rubble":
      ctx.fillStyle = "#8b8d94";
      for (let k = 0; k < 4; k++) ctx.fillRect(px + hash(x, y, k) * s * 0.8, py + hash(y, x, k) * s * 0.8, s * 0.18, s * 0.14);
      break;
    case "wood":
      ctx.strokeStyle = "#7e5129";
      ctx.lineWidth = Math.max(1, s / 14);
      for (let k = 1; k < 3; k++) { ctx.beginPath(); ctx.moveTo(px + (k * s) / 3, py); ctx.lineTo(px + (k * s) / 3, py + s); ctx.stroke(); }
      break;
    case "pit":
    case "wheel": {
      const g = ctx.createLinearGradient(px, py, px, py + s);
      g.addColorStop(0, "#14100e"); g.addColorStop(1, "#3a312b");
      ctx.fillStyle = g; ctx.fillRect(px, py, s, s);
      break;
    }
    case "metal":
      ctx.fillStyle = "#c7d0d8";
      ctx.fillRect(px, py, s, s * 0.12);
      ctx.fillStyle = "#6c7884";
      for (const [a, b] of [[0.2, 0.3], [0.8, 0.3], [0.2, 0.75], [0.8, 0.75]] as const) { ctx.beginPath(); ctx.arc(px + a * s, py + b * s, s * 0.05, 0, 7); ctx.fill(); }
      break;
    case "gate":
      ctx.fillStyle = "#3d3a36";
      for (let k = 0; k < 4; k++) ctx.fillRect(px + s * (0.12 + k * 0.22), py, s * 0.1, s);
      ctx.fillRect(px, py + s * 0.2, s, s * 0.1); ctx.fillRect(px, py + s * 0.7, s, s * 0.1);
      break;
    case "gate_open":
      ctx.fillStyle = "#3d3a36";
      ctx.fillRect(px, py, s, s * 0.12); ctx.fillRect(px, py + s * 0.88, s, s * 0.12);
      break;
  }
  drawMachine(ctx, c, x, y, s, t, w);
  ctx.restore();
}

function drawMachine(ctx: CanvasRenderingContext2D, c: Cell, x: number, y: number, s: number, t: number, w: World) {
  const def = RULES.machines[c.t];
  if (!def) return;
  const m = w.machines[y * w.w + x];
  const px = x * s, py = y * s, cx = px + s / 2, cy = py + s / 2;
  const on = m?.powered ?? false;
  if (on) {
    ctx.fillStyle = "rgba(255,225,90,0.35)";
    ctx.fillRect(px, py, s, s);
  }
  ctx.lineWidth = Math.max(1, s / 12);
  switch (c.t) {
    case "boiler": {
      ctx.fillStyle = "#b8682e";
      ctx.beginPath(); ctx.arc(cx, cy, s * 0.44, 0, 7); ctx.fill();
      ctx.fillStyle = c.water > 0 ? "#3f8fd8" : "#5a3418";
      ctx.beginPath(); ctx.arc(cx, cy, s * 0.26, 0, 7); ctx.fill();
      const frac = Math.min(1, (m?.level ?? 0) / (def.build ?? 4));
      ctx.strokeStyle = "#ffe14d";
      ctx.beginPath(); ctx.arc(cx, cy, s * 0.36, -Math.PI / 2, -Math.PI / 2 + frac * Math.PI * 2); ctx.stroke();
      break;
    }
    case "plate":
      ctx.fillStyle = "#7d7f86"; ctx.fillRect(px + s * 0.12, py + s * 0.12, s * 0.76, s * 0.76);
      ctx.fillStyle = on ? "#5d5f66" : "#9c9ea6"; ctx.fillRect(px + s * 0.22, py + s * 0.22, s * 0.56, s * 0.56);
      break;
    case "wheel":
    case "windmill": {
      const spin = on ? t / 180 : 0;
      ctx.strokeStyle = c.t === "wheel" ? "#c99a5b" : "#efe6d2";
      if (c.t === "windmill") { ctx.fillStyle = "#8a6a45"; ctx.beginPath(); ctx.arc(cx, cy, s * 0.18, 0, 7); ctx.fill(); }
      for (let k = 0; k < 4; k++) {
        const a = spin + (k * Math.PI) / 2;
        ctx.beginPath(); ctx.moveTo(cx, cy); ctx.lineTo(cx + Math.cos(a) * s * 0.45, cy + Math.sin(a) * s * 0.45); ctx.stroke();
      }
      break;
    }
    case "coil":
      ctx.strokeStyle = "#c27a3a";
      ctx.beginPath();
      for (let k = 0; k <= 6; k++) ctx.lineTo(px + s * (0.15 + k * 0.117), py + s * (k % 2 ? 0.25 : 0.75));
      ctx.stroke();
      break;
  }
}

function drawOverlay(ctx: CanvasRenderingContext2D, c: Cell, x: number, y: number, s: number, t: number) {
  const px = x * s, py = y * s;
  if (c.water > 0) {
    if (c.frozen) {
      ctx.fillStyle = "rgba(214,240,252,0.92)";
      ctx.fillRect(px, py, s, s);
      ctx.strokeStyle = "rgba(255,255,255,0.9)";
      ctx.lineWidth = 1;
      ctx.beginPath(); ctx.moveTo(px + hash(x, y) * s, py); ctx.lineTo(px + s * 0.5, py + s * 0.5); ctx.lineTo(px + s, py + hash(y, x) * s); ctx.stroke();
    } else {
      const a = 0.35 + 0.5 * Math.min(1, c.water / 8);
      ctx.fillStyle = `rgba(52,128,206,${a})`;
      ctx.fillRect(px, py, s, s);
      ctx.strokeStyle = "rgba(255,255,255,0.25)";
      ctx.beginPath();
      const wy = py + s * (0.4 + 0.1 * Math.sin(t / 400 + x));
      ctx.moveTo(px + s * 0.2, wy); ctx.lineTo(px + s * 0.6, wy); ctx.stroke();
    }
  }
  if (c.cover === "oil" || c.cover === "grease") {
    ctx.fillStyle = c.cover === "oil" ? "rgba(36,18,48,0.78)" : "rgba(232,212,120,0.62)";
    ctx.beginPath();
    ctx.ellipse(px + s / 2, py + s / 2, s * (0.42 + hash(x, y) * 0.08), s * 0.38, hash(y, x) * 3, 0, 7);
    ctx.fill();
    ctx.fillStyle = "rgba(255,255,255,0.25)";
    ctx.beginPath(); ctx.ellipse(px + s * 0.38, py + s * 0.38, s * 0.12, s * 0.05, -0.6, 0, 7); ctx.fill();
  } else if (c.cover === "ash") {
    ctx.fillStyle = "rgba(70,68,66,0.55)";
    for (let k = 0; k < 5; k++) ctx.fillRect(px + hash(x, y, k) * s, py + hash(y, x, k) * s, s * 0.1, s * 0.1);
  }
  if (c.fire > 0) drawFire(ctx, px, py, s, c.fire, t, hash(x, y));
  if (c.charge > 0) {
    ctx.strokeStyle = `rgba(255,236,90,${0.35 + c.charge * 0.2})`;
    ctx.lineWidth = Math.max(1, s / 10);
    ctx.beginPath();
    for (let k = 0; k <= 4; k++) ctx.lineTo(px + (k * s) / 4, py + s * (0.3 + 0.4 * hash(x + k, y, t >> 6)));
    ctx.stroke();
  }
}

function drawFire(ctx: CanvasRenderingContext2D, px: number, py: number, s: number, n: number, t: number, seed: number) {
  const colors = ["#e8452c", "#ff8a2a", "#ffcf4a"];
  ctx.fillStyle = `rgba(255,120,30,${0.18 + n * 0.08})`;
  ctx.fillRect(px, py, s, s);
  for (let k = 0; k < n + 1; k++) {
    const f = Math.sin(t / 90 + seed * 40 + k * 2.1);
    const fx = px + s * (0.25 + 0.25 * k / Math.max(1, n) + 0.05 * f);
    const h = s * (0.35 + 0.12 * n / 3 + 0.08 * f);
    const base = py + s * 0.88;
    for (let layer = 0; layer < 3; layer++) {
      const sc = 1 - layer * 0.3;
      ctx.fillStyle = colors[layer]!;
      ctx.beginPath();
      ctx.moveTo(fx - s * 0.16 * sc, base);
      ctx.quadraticCurveTo(fx - s * 0.12 * sc, base - h * 0.5 * sc, fx + f * s * 0.04, base - h * sc);
      ctx.quadraticCurveTo(fx + s * 0.12 * sc, base - h * 0.5 * sc, fx + s * 0.16 * sc, base);
      ctx.fill();
    }
  }
}

function drawGas(ctx: CanvasRenderingContext2D, c: Cell, x: number, y: number, s: number, t: number) {
  if (!c.gas) return;
  const steam = c.gas === "steam";
  ctx.fillStyle = steam ? `rgba(246,248,252,${Math.min(0.75, 0.14 * c.gasAmt)})` : `rgba(70,66,64,${Math.min(0.6, 0.12 * c.gasAmt)})`;
  for (let k = 0; k < 3; k++) {
    const dx = Math.sin(t / 700 + hash(x, y, k) * 6) * s * 0.12;
    ctx.beginPath();
    ctx.arc(x * s + s * (0.25 + 0.25 * k) + dx, y * s + s * (0.35 + 0.3 * hash(y, x, k)), s * 0.3, 0, 7);
    ctx.fill();
  }
}

function drawEntity(ctx: CanvasRenderingContext2D, e: World["entities"][number], s: number, t: number, selected: boolean, v: View) {
  const def = defOf(e);
  const lev = isLevitating(e);
  const bob = lev ? -s * (0.18 + 0.06 * Math.sin(t / 220)) : 0;
  const cx = e.x * s + s / 2, cy = e.y * s + s / 2 + bob;
  if (lev) {
    ctx.fillStyle = "rgba(0,0,0,0.25)";
    ctx.beginPath(); ctx.ellipse(cx, e.y * s + s * 0.85, s * 0.3, s * 0.1, 0, 0, 7); ctx.fill();
  }
  ctx.lineWidth = Math.max(1, s / 14);
  ctx.strokeStyle = "#1b1d1f";
  switch (e.kind) {
    case "wizard": {
      const mine = e.owner === v.me || (!e.owner && v.me === "you");
      const robe = e.owner ? v.colorOf(e.owner) : "#6a50c8";
      if (mine) {
        ctx.strokeStyle = `rgba(255,255,255,${0.55 + 0.3 * Math.sin(t / 260)})`;
        ctx.lineWidth = Math.max(1.5, s / 9);
        ctx.beginPath(); ctx.arc(cx, cy, s * 0.5, 0, 7); ctx.stroke();
      }
      ctx.lineWidth = Math.max(1, s / 14);
      ctx.strokeStyle = "#1b1d1f";
      ctx.fillStyle = robe;
      ctx.beginPath(); ctx.arc(cx, cy + s * 0.08, s * 0.3, 0, 7); ctx.fill(); ctx.stroke();
      ctx.fillStyle = "#f2d7b6";
      ctx.beginPath(); ctx.arc(cx, cy - s * 0.02, s * 0.13, 0, 7); ctx.fill();
      ctx.fillStyle = "#33246f";
      ctx.beginPath(); ctx.moveTo(cx - s * 0.26, cy - s * 0.1); ctx.lineTo(cx + s * 0.06, cy - s * 0.62); ctx.lineTo(cx + s * 0.24, cy - s * 0.1); ctx.closePath(); ctx.fill(); ctx.stroke();
      ctx.fillStyle = "#ffe14d";
      ctx.beginPath(); ctx.arc(cx + s * 0.02, cy - s * 0.3, s * 0.05, 0, 7); ctx.fill();
      break;
    }
    case "goblin":
      ctx.fillStyle = "#6fae4a";
      ctx.beginPath(); ctx.moveTo(cx - s * 0.42, cy - s * 0.2); ctx.lineTo(cx - s * 0.2, cy - s * 0.05); ctx.lineTo(cx - s * 0.2, cy - s * 0.25); ctx.fill();
      ctx.beginPath(); ctx.moveTo(cx + s * 0.42, cy - s * 0.2); ctx.lineTo(cx + s * 0.2, cy - s * 0.05); ctx.lineTo(cx + s * 0.2, cy - s * 0.25); ctx.fill();
      ctx.beginPath(); ctx.arc(cx, cy, s * 0.3, 0, 7); ctx.fill(); ctx.stroke();
      ctx.fillStyle = "#ffe14d";
      ctx.fillRect(cx - s * 0.14, cy - s * 0.08, s * 0.09, s * 0.07); ctx.fillRect(cx + s * 0.05, cy - s * 0.08, s * 0.09, s * 0.07);
      break;
    case "crate":
      ctx.fillStyle = "#c08d50";
      ctx.fillRect(cx - s * 0.36, cy - s * 0.36, s * 0.72, s * 0.72); ctx.strokeRect(cx - s * 0.36, cy - s * 0.36, s * 0.72, s * 0.72);
      ctx.beginPath(); ctx.moveTo(cx - s * 0.36, cy - s * 0.36); ctx.lineTo(cx + s * 0.36, cy + s * 0.36); ctx.moveTo(cx + s * 0.36, cy - s * 0.36); ctx.lineTo(cx - s * 0.36, cy + s * 0.36); ctx.stroke();
      break;
    case "barrel":
      ctx.fillStyle = "#6b3f22";
      ctx.beginPath(); ctx.arc(cx, cy, s * 0.36, 0, 7); ctx.fill(); ctx.stroke();
      ctx.strokeStyle = "#2b2b2b";
      ctx.beginPath(); ctx.arc(cx, cy, s * 0.22, 0, 7); ctx.stroke();
      ctx.fillStyle = "#3a2346";
      ctx.beginPath(); ctx.arc(cx, cy, s * 0.1, 0, 7); ctx.fill();
      break;
    case "boulder": {
      const g = ctx.createRadialGradient(cx - s * 0.12, cy - s * 0.12, s * 0.05, cx, cy, s * 0.45);
      g.addColorStop(0, "#b5b5b0"); g.addColorStop(1, "#5d5e5a");
      ctx.fillStyle = g;
      ctx.beginPath(); ctx.arc(cx, cy, s * 0.44, 0, 7); ctx.fill(); ctx.stroke();
      break;
    }
  }
  if ((e.status.frozen ?? 0) > 0) {
    ctx.fillStyle = "rgba(190,235,255,0.7)";
    ctx.fillRect(cx - s * 0.45, cy - s * 0.45, s * 0.9, s * 0.9);
  }
  if ((e.status.wet ?? 0) > 0) {
    ctx.fillStyle = "#4aa3ff";
    ctx.beginPath(); ctx.arc(cx + s * 0.32, cy + s * 0.28, s * 0.09, 0, 7); ctx.fill();
  }
  if ((e.status.burning ?? 0) > 0) drawFire(ctx, cx - s * 0.5, cy - s * 0.75, s, 1, t, e.id);
  if (def.creature && e.hp < def.hp) {
    const f = Math.max(0, e.hp / def.hp);
    ctx.fillStyle = "#1b1d1f"; ctx.fillRect(cx - s * 0.4, cy - s * 0.52, s * 0.8, s * 0.12);
    ctx.fillStyle = f > 0.5 ? "#7bd66b" : f > 0.25 ? "#ffcf4a" : "#ff5a4a";
    ctx.fillRect(cx - s * 0.38, cy - s * 0.5, s * 0.76 * f, s * 0.08);
  }
  if (selected) {
    ctx.strokeStyle = "#c9b8ff";
    ctx.lineWidth = 2;
    ctx.setLineDash([3, 3]);
    ctx.strokeRect(e.x * s + 1, e.y * s + 1, s - 2, s - 2);
    ctx.setLineDash([]);
  }
}

function drawCritter(ctx: CanvasRenderingContext2D, c: World["critters"][number], s: number, t: number) {
  const def = RULES.critters.find((d) => d.id === c.kind)!;
  const cx = c.x * s + s / 2, cy = c.y * s + s / 2 + Math.sin(t / 300 + c.id) * s * 0.04;
  ctx.fillStyle = "rgba(20,20,24,0.55)";
  ctx.beginPath(); ctx.arc(cx, cy, s * 0.4, 0, 7); ctx.fill();
  ctx.strokeStyle = def.color;
  ctx.lineWidth = Math.max(1.5, s / 9);
  ctx.beginPath(); ctx.arc(cx, cy, s * 0.4, 0, 7); ctx.stroke();
  ctx.font = `${Math.round(s * 0.52)}px system-ui, "Apple Color Emoji", "Segoe UI Emoji", sans-serif`;
  ctx.textAlign = "center";
  ctx.textBaseline = "middle";
  ctx.fillText(def.glyph, cx, cy + s * 0.03);
  if (def.directional) arrow(ctx, cx, cy, c.dir, s * 0.62, def.color);
}

function arrow(ctx: CanvasRenderingContext2D, cx: number, cy: number, dir: Dir, len: number, color: string) {
  const ex = cx + DX[dir] * len, ey = cy + DY[dir] * len;
  ctx.strokeStyle = color;
  ctx.fillStyle = color;
  ctx.lineWidth = 2;
  ctx.beginPath(); ctx.moveTo(cx + DX[dir] * len * 0.55, cy + DY[dir] * len * 0.55); ctx.lineTo(ex, ey); ctx.stroke();
  const a = Math.atan2(ey - cy, ex - cx);
  ctx.beginPath();
  ctx.moveTo(ex + Math.cos(a) * 4, ey + Math.sin(a) * 4);
  ctx.lineTo(ex + Math.cos(a + 2.4) * 6, ey + Math.sin(a + 2.4) * 6);
  ctx.lineTo(ex + Math.cos(a - 2.4) * 6, ey + Math.sin(a - 2.4) * 6);
  ctx.fill();
}

function drawPreview(ctx: CanvasRenderingContext2D, v: View, s: number) {
  if (!v.preview.length) return;
  ctx.strokeStyle = v.previewColor;
  ctx.fillStyle = v.previewColor + "33";
  ctx.lineWidth = 2;
  for (const [x, y] of v.preview) {
    ctx.fillRect(x * s, y * s, s, s);
    ctx.strokeRect(x * s + 1, y * s + 1, s - 2, s - 2);
  }
}
