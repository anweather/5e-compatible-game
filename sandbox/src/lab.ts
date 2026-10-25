// The Experiments panel: mods as shared, votable experiments.
//
//   idea ──(you, Claude, or your own LLM)──▶ JSON patch ──check──▶ saved mod
//   mod ──switch on──▶ everyone's rules change at the same tick ──play──▶ vote
//   keep ──▶ Claude bakes it into rules/ for the next version; drop ──▶ archived
import { BASE_RULES, RULES, composeRules, validateRules, type ModPatch } from "./rules.ts";
import type { Db } from "./room.ts";

export interface ModDoc {
  id: string;
  title: string;
  idea: string;
  patch: ModPatch;
  author: string | null;
  created: number;
  status: "testing" | "keep" | "drop" | "merged";
}
type Vote = "keep" | "meh" | "drop";
interface VoteDoc { v: Record<string, Vote>; notes: Record<string, string> }

export interface Sample {
  json<T = unknown>(input: string, opts?: Record<string, unknown>): Promise<T>;
}
export interface UserCap {
  id(): Promise<string | null>;
  isOwner(): Promise<boolean>;
  profiles(ids: string[]): Promise<Record<string, { name: string }>>;
}

export interface LabDeps {
  root: HTMLElement;
  db: Db | null;
  user: UserCap | null;
  sample: Sample | null;
  /** Ids switched on in the current game, in order. */
  active(): string[];
  /** Asks the game to switch to exactly these mods. */
  setActive(ids: string[]): void;
}

function el<K extends keyof HTMLElementTagNameMap>(tag: K, props: Record<string, unknown> = {}, ...kids: (Node | string)[]): HTMLElementTagNameMap[K] {
  const e = document.createElement(tag);
  Object.assign(e, props);
  e.append(...kids);
  return e;
}

const TEMPLATE = `{
  "reactions": {
    "add": [
      { "id": "steam-scalds-grass", "element": "fire",
        "if": { "gas": "steam", "terrainTag": "flammable" },
        "do": [{ "extinguish": true }],
        "log": "Steam keeps the grass too damp to burn" }
    ]
  }
}`;

export class Lab {
  private d: LabDeps;
  private mods = new Map<string, ModDoc>();
  private votes = new Map<string, VoteDoc>();
  private uid: string | null = null;
  private owner = false;
  private names: Record<string, string> = {};
  private open = new Set<string>();
  private drafting = false;
  private draft = { title: "", idea: "", json: TEMPLATE, msg: "", busy: false };
  private localCount = 0;
  private lastActive = "";

  constructor(d: LabDeps) {
    this.d = d;
    this.render();
  }

  /** Called once the platform says which shared features this viewer has. */
  async connect(caps: { db: Db | null; user: UserCap | null; sample: Sample | null }) {
    this.d.db = caps.db;
    this.d.user = caps.user;
    this.d.sample = caps.sample;
    await this.init();
  }

  private async init() {
    this.uid = (await this.d.user?.id().catch(() => null)) ?? null;
    this.owner = (await this.d.user?.isOwner().catch(() => false)) ?? false;
    this.d.db?.collection("mods").onSnapshot((s) => {
      for (const doc of s.docs) {
        const x = doc.data() as Omit<ModDoc, "id">;
        if (x && x.patch && typeof x.title === "string") this.mods.set(doc.id, { ...x, id: doc.id });
      }
      void this.resolveNames();
      this.render();
    }, () => {});
    this.d.db?.collection("votes").onSnapshot((s) => {
      this.votes.clear();
      for (const doc of s.docs) this.votes.set(doc.id, doc.data() as unknown as VoteDoc);
      this.render();
    }, () => {});
    this.render();
  }

  private async resolveNames() {
    const ids = [...new Set([...this.mods.values()].map((m) => m.author).filter((a): a is string => !!a))];
    if (!ids.length || !this.d.user) return;
    const ps = await this.d.user.profiles(ids).catch(() => ({} as Record<string, { name: string }>));
    for (const id of ids) this.names[id] = ps[id]?.name || "a player";
    this.render();
  }

  /** For the lockstep session: patches by id, or null if one isn't here yet. */
  source = async (ids: string[]): Promise<ModPatch[] | null> => {
    const out: ModPatch[] = [];
    for (const id of ids) {
      let m = this.mods.get(id);
      if (!m && this.d.db) {
        const snap = await this.d.db.doc(`mods/${id}`).get().catch(() => null);
        const x = snap?.exists ? (snap.data() as Omit<ModDoc, "id">) : null;
        if (x?.patch) this.mods.set(id, (m = { ...x, id }));
      }
      if (!m) return null;
      out.push(m.patch);
    }
    return out;
  };

  /** Re-render when the game's active set changes. */
  tick() {
    const key = this.d.active().join("|");
    if (key !== this.lastActive) {
      this.lastActive = key;
      this.render();
    }
  }

  private check(patch: ModPatch): string[] {
    const activePatches = this.d.active().map((id) => this.mods.get(id)?.patch).filter((p): p is ModPatch => !!p);
    return validateRules(composeRules([...activePatches, patch]));
  }

  private parseDraft(): { patch?: ModPatch; errors: string[] } {
    let patch: ModPatch;
    try {
      patch = JSON.parse(this.draft.json) as ModPatch;
    } catch (e) {
      return { errors: [`That isn't valid JSON: ${(e as Error).message}`] };
    }
    if (!patch || typeof patch !== "object" || Array.isArray(patch)) return { errors: ["A patch is a JSON object; see the template."] };
    // An AI answering the brief replies {"title", "patch"}: accept that shape too.
    const wrapped = patch as { title?: unknown; patch?: unknown };
    if (wrapped.patch && typeof wrapped.patch === "object") {
      if (typeof wrapped.title === "string" && !this.draft.title) this.draft.title = wrapped.title.slice(0, 80);
      patch = wrapped.patch as ModPatch;
    }
    return { patch, errors: this.check(patch) };
  }

  private async askClaude(fixErrors?: string[]) {
    if (!this.d.sample || this.draft.busy) return;
    if (!this.draft.idea.trim() && !fixErrors) {
      this.draft.msg = "Describe the idea first, in plain words.";
      return this.render();
    }
    this.draft.busy = true;
    this.draft.msg = fixErrors ? "Asking Claude to fix it..." : "Asking Claude to write the patch...";
    this.render();
    const prompt = buildPrompt(this.draft.idea, fixErrors ? this.draft.json : null, fixErrors ?? []);
    try {
      const out = await this.d.sample.json<{ title?: string; patch?: ModPatch }>(prompt, { modelTier: "default" });
      if (!out?.patch) throw new Error("no patch in the answer");
      if (out.title && !this.draft.title) this.draft.title = out.title.slice(0, 80);
      this.draft.json = JSON.stringify(out.patch, null, 2);
      const { errors } = this.parseDraft();
      this.draft.msg = errors.length ? `Claude's patch has problems: ${errors.slice(0, 3).join("; ")}` : "Claude wrote a patch. It checks out; read it, then save it.";
    } catch (e) {
      const code = (e as { code?: string }).code;
      this.draft.msg = code === "not_granted" ? "This page wasn't allowed to ask Claude." : code === "rate_limited" ? "Too many requests just now. Try again in a minute." : `Claude couldn't write it: ${(e as Error).message ?? "unknown error"}`;
    } finally {
      this.draft.busy = false;
      this.render();
    }
  }

  private async save(andTurnOn: boolean) {
    const { patch, errors } = this.parseDraft();
    if (!patch || errors.length) {
      this.draft.msg = errors.slice(0, 4).join("; ");
      return this.render();
    }
    const title = this.draft.title.trim() || "Untitled experiment";
    const body = { title: title.slice(0, 80), idea: this.draft.idea.trim().slice(0, 1000), patch, author: this.uid, created: Date.now(), status: "testing" as const };
    let id: string;
    if (this.d.db) {
      const ref = this.d.db.collection("mods").doc();
      try {
        await ref.set(body as unknown as Record<string, unknown>);
      } catch {
        this.draft.msg = "Couldn't save it. You need Contributor access to this page to share experiments.";
        return this.render();
      }
      id = ref.id;
    } else {
      id = `local-${++this.localCount}`;
    }
    this.mods.set(id, { ...body, id });
    this.drafting = false;
    this.draft = { title: "", idea: "", json: TEMPLATE, msg: "", busy: false };
    if (andTurnOn) this.d.setActive([...this.d.active(), id]);
    this.render();
  }

  private async vote(modId: string, v: Vote | null, note?: string) {
    if (!this.uid || !this.d.db) return;
    const mine: VoteDoc = structuredClone(this.votes.get(this.uid) ?? { v: {}, notes: {} });
    if (v) mine.v[modId] = v;
    if (note !== undefined) mine.notes[modId] = note.slice(0, 280);
    this.votes.set(this.uid, mine);
    this.render();
    await this.d.db.doc(`votes/${this.uid}`).set(mine as unknown as Record<string, unknown>).catch(() => {});
  }

  private async setStatus(modId: string, status: ModDoc["status"]) {
    const m = this.mods.get(modId);
    if (!m || !this.d.db) return;
    m.status = status;
    this.render();
    const { id: _id, ...body } = m;
    await this.d.db.doc(`mods/${modId}`).set(body as unknown as Record<string, unknown>).catch(() => {});
  }

  private tally(modId: string) {
    const t = { keep: 0, meh: 0, drop: 0 };
    for (const d of this.votes.values()) {
      const v = d.v?.[modId];
      if (v) t[v]++;
    }
    return t;
  }

  render() {
    const root = this.d.root;
    const active = this.d.active();
    const head = el("div", { className: "lab-head" },
      el("h2", {}, "Experiments"),
      el("button", { type: "button", className: "btn", textContent: this.drafting ? "Close" : "New experiment", onclick: () => { this.drafting = !this.drafting; this.render(); } }));
    const intro = el("p", { className: "hint" },
      this.d.db
        ? "Mods change the rule tables live, for everyone in the room. Switch one on, play, then vote. Kept ones get baked into the next version."
        : "Experiments are only kept on the published page. Here they last until you reload.");
    const kids: Node[] = [head, intro];
    if (this.drafting) kids.push(this.renderDraft());
    const list = [...this.mods.values()].sort((a, b) => b.created - a.created);
    if (!list.length && !this.drafting) kids.push(el("p", { className: "empty" }, "No experiments yet. Start one with New experiment."));
    for (const m of list) kids.push(this.renderMod(m, active.includes(m.id)));
    if (active.length) {
      kids.push(el("button", { type: "button", className: "btn", textContent: "Switch all mods off", onclick: () => this.d.setActive([]) }));
    }
    root.replaceChildren(...kids);
  }

  private renderMod(m: ModDoc, on: boolean): HTMLElement {
    const t = this.tally(m.id);
    const mine = this.uid ? this.votes.get(this.uid)?.v?.[m.id] : undefined;
    const toggle = el("input", { type: "checkbox", checked: on, id: `mod-${m.id}` });
    toggle.onchange = () => {
      const ids = this.d.active().filter((x) => x !== m.id);
      this.d.setActive(toggle.checked ? [...ids, m.id] : ids);
    };
    const label = el("label", { htmlFor: `mod-${m.id}`, className: "mod-title" }, m.title);
    const status = el("span", { className: `status ${m.status}` }, m.status === "testing" ? "testing" : m.status === "keep" ? "keeping" : m.status === "drop" ? "dropped" : "merged");
    const voteBtn = (v: Vote, text: string) => el("button", {
      type: "button", className: "vote", textContent: `${text} ${t[v]}`, disabled: !this.uid || !this.d.db,
      onclick: () => void this.vote(m.id, mine === v ? null : v),
    });
    const votes = el("div", { className: "votes" }, voteBtn("keep", "Keep"), voteBtn("meh", "Meh"), voteBtn("drop", "Drop"));
    votes.querySelectorAll("button").forEach((b, i) => b.setAttribute("aria-pressed", String(mine === (["keep", "meh", "drop"] as const)[i])));
    const row = el("div", { className: `mod${on ? " on" : ""}` }, el("div", { className: "mod-line" }, toggle, label, status), votes);
    const more = el("details", { open: this.open.has(m.id) });
    more.ontoggle = () => (more.open ? this.open.add(m.id) : this.open.delete(m.id));
    more.append(el("summary", {}, "Details"));
    if (m.idea) more.append(el("p", {}, m.idea));
    more.append(el("p", { className: "hint" }, `By ${m.author ? (this.names[m.author] ?? "a player") : "you, on this page"} · ${new Date(m.created).toLocaleString()}`));
    const notes = [...this.votes.values()].map((d) => d.notes?.[m.id]).filter((n): n is string => !!n);
    if (notes.length) more.append(el("ul", { className: "notes" }, ...notes.map((n) => el("li", {}, n))));
    if (this.uid && this.d.db) {
      const note = el("input", { type: "text", placeholder: "What happened? What was fun?", value: this.votes.get(this.uid)?.notes?.[m.id] ?? "", id: `note-${m.id}` });
      note.onchange = () => void this.vote(m.id, null, note.value);
      more.append(note);
    }
    more.append(el("pre", { className: "patch" }, JSON.stringify(m.patch, null, 2)));
    more.append(el("button", { type: "button", className: "btn", textContent: "Remix into a new experiment", onclick: () => {
      this.drafting = true;
      this.draft = { title: `${m.title} (remix)`, idea: m.idea, json: JSON.stringify(m.patch, null, 2), msg: "", busy: false };
      this.render();
    } }));
    if (this.owner && this.d.db) {
      const sel = el("select", { id: `status-${m.id}` }, ...(["testing", "keep", "drop", "merged"] as const).map((s) => el("option", { value: s, selected: m.status === s }, s)));
      sel.onchange = () => void this.setStatus(m.id, sel.value as ModDoc["status"]);
      more.append(el("label", { className: "hint" }, "Status (owner): ", sel));
    }
    row.append(more);
    return row;
  }

  private renderDraft(): HTMLElement {
    const title = el("input", { type: "text", id: "draft-title", placeholder: "Name it: Ice cracks under weight", value: this.draft.title });
    title.oninput = () => (this.draft.title = title.value);
    const idea = el("textarea", { id: "draft-idea", rows: 3, placeholder: "The idea in plain words: frozen ponds crack when something heavy stands on them, and drop it in the water.", value: this.draft.idea });
    idea.oninput = () => (this.draft.idea = idea.value);
    const json = el("textarea", { id: "draft-json", rows: 10, className: "code", value: this.draft.json, spellcheck: false });
    json.oninput = () => (this.draft.json = json.value);
    const buttons = el("div", { className: "row" });
    if (this.d.sample) {
      buttons.append(el("button", { type: "button", className: "btn", disabled: this.draft.busy, textContent: "Write it with Claude", onclick: () => void this.askClaude() }));
    }
    const copyBrief = el("button", { type: "button", className: "btn", textContent: "Copy brief for another AI" });
    copyBrief.onclick = () => {
      const brief = buildPrompt(this.draft.idea, null, []);
      navigator.clipboard.writeText(brief).then(
        () => { this.draft.msg = "Copied. Paste it into any AI, then paste the patch it returns into the box above."; this.render(); },
        () => {
          // Clipboard refused: put the brief in the patch box, selected, to copy by hand.
          this.draft.json = brief;
          this.draft.msg = "Couldn't reach the clipboard. The brief is in the patch box, selected; copy it, then replace it with the AI's answer.";
          this.render();
          (this.d.root.querySelector("#draft-json") as HTMLTextAreaElement | null)?.select();
        },
      );
    };
    buttons.append(copyBrief);
    buttons.append(
      el("button", { type: "button", className: "btn", textContent: "Check", onclick: () => {
        const { errors } = this.parseDraft();
        this.draft.msg = errors.length ? errors.slice(0, 4).join("; ") : "Looks valid. It will merge cleanly with the mods that are on.";
        this.render();
      } }),
      el("button", { type: "button", className: "btn primary", textContent: "Save and switch on", onclick: () => void this.save(true) }),
    );
    if (this.d.sample && this.draft.msg.includes("problems")) {
      buttons.append(el("button", { type: "button", className: "btn", textContent: "Ask Claude to fix it", onclick: () => void this.askClaude(this.parseDraft().errors) }));
    }
    const kids: Node[] = [
      el("label", { htmlFor: "draft-title" }, "Name"), title,
      el("label", { htmlFor: "draft-idea" }, "Idea"), idea,
      el("label", { htmlFor: "draft-json" }, "Patch (JSON, data only; see docs/MODDING.md)"), json,
      buttons,
    ];
    if (this.draft.msg) kids.push(el("p", { className: "hint", role: "status" }, this.draft.msg));
    return el("div", { className: "draft" }, ...kids);
  }
}

/** The whole brief for an LLM writing a mod: schema, current tables, the idea. */
export function buildPrompt(idea: string, previous: string | null, errors: string[]): string {
  const current = {
    elements: RULES.elements, terrain: RULES.terrain, covers: RULES.covers, gases: RULES.gases,
    cellRules: RULES.cellRules, entityRules: RULES.entityRules, spells: RULES.spells, critters: RULES.critters,
    entities: RULES.entities, machines: RULES.machines, sim: RULES.sim,
  };
  return [
    "You write data-only mods for a top-down 2D spell-chemistry sandbox built on 5E (SRD 5.2.1) spells.",
    "A mod is a JSON patch over the rule tables. Reply with JSON only, shaped {\"title\": string, \"patch\": Patch}.",
    "",
    "Patch fields (all optional): elements (string[] of new element names); terrain, covers, gases, entities, machines (objects keyed by id: add or replace);",
    "reactions: {add?: CellRule[], addEntity?: EntityRule[], remove?: string[] of rule ids}; spells: SpellDef[] and critters: CritterDef[] (add or replace by id); sim: partial object deep-merged over sim.",
    "A CellRule is {id, element, if?: Cond, do: Effect[], log?, notable?, stop?}. New rules go to the top of their element's list, so they win; rules run in order and stop ends the list.",
    "Cond keys: cover (string[]), fire (true|false|min number), fireBelow, water (bool, unfrozen water present), frozen, gas (id or \"any\"), terrainTag, notTerrainTag, conductive.",
    "Effect objects have exactly one of: ignite (number or \"cover\"), boil (n), melt, freeze, extinguish, condense, addWater (n or \"power\"), setCover (id|null), spreadCover (n), break, setTerrain (id), raise (terrain id), conduct (n), fan, pushGas, clearGas, mark (\"wind\"), gas (id) with n.",
    "Entity rules: if {status?, flammable?}; effects status {name: ticks}, clear (status name), damageMul (n).",
    "Spell shapes: point, line, square, burst, target. Spells may set origin \"self\", length, size, radius, push, wind, damage [n, sides], power, element, blurb, name, level.",
    "A spell with effect \"terraform\" shapes the ground: exactly one of raise or dig (whole steps from 1, relative) or height (raise each cell to that height, within sim.height min..max; higher cells untouched), optional terrain (a non-blocking terrain id the cells become) and taper (true: a hill or crater over radius). Heights stay within sim.height {min, max}; a cell raised to sim.height.blockAt or more is a wall. A line with origin self starts under the caster's feet.",
    "Critters: {id, name, glyph (one emoji), color, mode \"emit\"|\"act\", element, power?, every? (ticks), radius?, length?, directional?, blurb}.",
    "You cannot invent new effect or condition verbs; those need code. Express the idea with the existing verbs, tags and elements, or by adding terrain, covers, gases, spells and critters.",
    "Use lowercase-dash ids that don't collide unless you mean to replace. Keep the patch small and focused on the idea. Log lines are short, plain English.",
    "",
    "Current rule tables:",
    JSON.stringify(current),
    "",
    previous ? `Your previous patch failed validation with: ${errors.join("; ")}\nPrevious patch:\n${previous}\nFix it.` : "",
    `Idea: ${idea || "(see previous patch)"}`,
  ].join("\n");
}

export { BASE_RULES };
