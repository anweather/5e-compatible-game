// Lockstep multiplayer. Every page runs the same deterministic simulation;
// one page (the host) orders everyone's intents into numbered batches, and
// every page applies the same batches in the same order.
//
//   player ──intent──▶ host ──batch {n, e, s, i[], h?}──▶ everyone
//
// A page that misses a batch, sees a new host, or fails a checksum asks for
// a snapshot ("need"); the host saves one to shared storage and says so
// ("snap"). Solo play is the same code with a transport of one.
import { RULES, composeRules, installRules, type ModPatch } from "./rules.ts";
import { step } from "./sim.ts";
import { type Intent, applyIntent, sanitizeIntent } from "./intents.ts";
import { type World, type WorldSnapshot, deserializeWorld, hashWorld, playerOf, sanitizeWorld, serializeWorld } from "./world.ts";

export interface PeerInfo {
  id: string;
  /** When this page joined (its own clock). The longest-standing synced page hosts. */
  since: number;
  synced: boolean;
}

export interface Batch {
  n: number;
  /** Epoch: the host that produced it. A new host means everyone resyncs. */
  e: string;
  /** 1 = advance the simulation one tick after applying the intents. */
  s: 0 | 1;
  i: [string, Intent][];
  /** Checksum of the world after this batch, every few batches. */
  h?: string;
}

export interface Snapshot {
  n: number;
  e: string;
  world: WorldSnapshot;
}

export interface Transport {
  readonly me: string;
  peers(): PeerInfo[];
  emit(topic: "intent" | "tick" | "need" | "snap", data: unknown): void;
  on(topic: "intent" | "tick" | "need" | "snap", fn: (data: unknown, from: string) => void): void;
  setPresence(p: { since: number; synced: boolean }): void;
  saveSnapshot(s: Snapshot): Promise<void>;
  loadSnapshot(): Promise<Snapshot | null>;
}

/** Fetches mod patches by id. Resolves null while any of them isn't available yet. */
export type ModSource = (ids: string[]) => Promise<ModPatch[] | null>;

const MAX_BATCH_BYTES = 3200; // a room message carries at most 4 KiB
const HASH_EVERY = 20;
const MAX_PER_PEER = 12;

export type SyncState = "solo" | "hosting" | "synced" | "syncing";

export class Lockstep {
  world: World;
  /** Last batch applied. */
  n = 0;
  epoch: string;
  private lastQueued = 0;
  private pending: [string, Intent][] = [];
  private queue: { b: Batch; ours: boolean }[] = [];
  private buffer: Batch[] = [];
  private processing = false;
  private syncing = false;
  private synced: boolean;
  private since: number;
  private acc = 0;
  private needTimer: ReturnType<typeof setTimeout> | null = null;
  private snapSaving = false;
  private installedKey = "";
  private patchCache = new Map<string, ModPatch>();

  onNotice: (text: string) => void = () => {};
  onWorld: (w: World) => void = () => {};
  onState: (s: SyncState) => void = () => {};

  private t: Transport;
  private retryMs: number;
  private mods: ModSource;

  constructor(t: Transport, world: World, mods: ModSource, opts: { joinExisting?: boolean; now?: number; retryMs?: number } = {}) {
    this.t = t;
    this.mods = mods;
    this.retryMs = opts.retryMs ?? 2500;
    this.world = world;
    this.epoch = t.me;
    this.since = opts.now ?? Date.now();
    this.synced = !opts.joinExisting;
    t.setPresence({ since: this.since, synced: this.synced });
    t.on("intent", (d, from) => this.onIntent(d, from));
    t.on("tick", (d, from) => this.onTick(d as Batch, from));
    t.on("need", (_d, from) => void this.onNeed(from));
    t.on("snap", (d, from) => void this.onSnap(d as { n: number; to: string }, from));
    if (opts.joinExisting) this.resync();
  }

  /** The page that orders batches: longest-standing synced page (ties by id). */
  host(): string {
    const peers = this.t.peers();
    const pool = peers.filter((p) => p.synced);
    const pick = (pool.length ? pool : peers).slice().sort((a, b) => a.since - b.since || (a.id < b.id ? -1 : 1))[0];
    return pick?.id ?? this.t.me;
  }
  isHost(): boolean {
    return this.synced && this.host() === this.t.me;
  }
  state(): SyncState {
    if (this.syncing || !this.synced) return "syncing";
    if (this.t.peers().length <= 1) return "solo";
    return this.isHost() ? "hosting" : "synced";
  }

  /** Do something. The host queues it; everyone else sends it to the host. */
  submit(intent: Intent) {
    if (this.isHost()) this.pending.push([this.t.me, intent]);
    else this.t.emit("intent", intent);
  }

  /** Called every animation frame. Only the host's clock produces batches. */
  frame(dtMs: number) {
    if (!this.isHost() || this.processing) return;
    this.reconcilePlayers();
    const ctl = this.world.ctl;
    const interval = 1000 / (RULES.sim.ticksPerSecond * ctl.speed);
    let made = 0;
    if (ctl.playing) {
      this.acc = Math.min(this.acc + dtMs, interval * 4);
      while (this.acc >= interval && made < 4) {
        this.acc -= interval;
        this.produce(1);
        made++;
      }
    } else {
      this.acc = 0;
    }
    if (!made && this.pending.length) this.produce(this.pending.some(([, it]) => it.k === "step") ? 1 : 0);
  }

  /** Make sure every page in the room has a wizard and no departed page does. */
  private reconcilePlayers() {
    const here = new Set(this.t.peers().map((p) => p.id));
    here.add(this.t.me);
    const owned = new Set(this.world.entities.filter((e) => e.owner).map((e) => e.owner!));
    const queued = new Set(this.pending.filter(([, it]) => it.k === "join" || it.k === "leave").map(([, it]) => (it as { peer: string }).peer));
    for (const id of [...here].sort()) if (!owned.has(id) && !queued.has(id)) this.pending.push([this.t.me, { k: "join", peer: id }]);
    for (const id of [...owned].sort()) if (!here.has(id) && !queued.has(id)) this.pending.push([this.t.me, { k: "leave", peer: id }]);
  }

  private produce(s: 0 | 1) {
    const take: [string, Intent][] = [];
    let bytes = 0;
    while (this.pending.length) {
      const size = JSON.stringify(this.pending[0]).length;
      if (take.length && bytes + size > MAX_BATCH_BYTES) break;
      take.push(this.pending.shift()!);
      bytes += size;
    }
    const batch: Batch = { n: this.lastQueued + 1, e: this.t.me, s, i: take };
    this.epoch = this.t.me;
    this.enqueue(batch, true);
  }

  private onIntent(raw: unknown, from: string) {
    if (!this.isHost()) return;
    const it = sanitizeIntent(raw);
    if (!it) return;
    if (this.pending.filter(([p]) => p === from).length >= MAX_PER_PEER) return;
    this.pending.push([from, it]);
  }

  private onTick(b: Batch, from: string) {
    if (from === this.t.me || this.isHost()) return;
    if (!b || typeof b.n !== "number" || from !== b.e) return;
    if (this.syncing) {
      this.buffer.push(b);
      return;
    }
    if (from !== this.host()) return;
    if (b.e !== this.epoch) return this.resync(); // new host: start from its snapshot
    if (b.n <= this.lastQueued) return; // duplicate
    if (b.n !== this.lastQueued + 1) return this.resync(); // missed one
    this.enqueue(b, false);
  }

  private enqueue(b: Batch, emitAfter: boolean) {
    this.lastQueued = b.n;
    this.queue.push({ b, ours: emitAfter });
    void this.drain();
  }

  private async drain() {
    if (this.processing) return;
    this.processing = true;
    try {
      while (this.queue.length) {
        const { b, ours } = this.queue.shift()!;
        await this.apply(b);
        if (ours) {
          const out: Batch = { n: b.n, e: b.e, s: b.s, i: b.i };
          if (b.n % HASH_EVERY === 0) out.h = hashWorld(this.world);
          this.t.emit("tick", out);
        } else if (b.h && b.h !== hashWorld(this.world)) {
          this.queue = [];
          this.resync();
          break;
        }
      }
    } finally {
      this.processing = false;
    }
  }

  /** Installs the rules this world's mods call for, if another world changed them. */
  private async ensureRules(ids: string[]): Promise<boolean> {
    const key = ids.join("|");
    if (key === this.installedKey) return true;
    const patches = await this.patchesFor(ids);
    if (!patches) return false;
    installRules(composeRules(patches));
    this.installedKey = key;
    return true;
  }

  private async patchesFor(ids: string[]): Promise<ModPatch[] | null> {
    if (!ids.length) return [];
    const missing = ids.filter((id) => !this.patchCache.has(id));
    if (missing.length) {
      for (let attempt = 0; attempt < 20; attempt++) {
        const got = await this.mods(missing);
        if (got) {
          missing.forEach((id, i) => this.patchCache.set(id, got[i]!));
          break;
        }
        await new Promise((r) => setTimeout(r, 300));
      }
      if (missing.some((id) => !this.patchCache.has(id))) return null;
    }
    return ids.map((id) => this.patchCache.get(id)!);
  }

  private async apply(b: Batch) {
    await this.ensureRules(this.world.mods);
    for (const [from, it] of b.i) {
      const env: { patches?: ModPatch[] } = {};
      if (it.k === "mods") {
        const p = await this.patchesFor(it.ids);
        if (!p) {
          if (from === this.t.me) this.onNotice("Couldn't load that mod. Try again in a moment.");
          continue;
        }
        env.patches = p;
      }
      const before = this.world;
      const { world, notice } = applyIntent(this.world, from, it, env);
      if (it.k === "mods" && !notice) this.installedKey = it.ids.join("|");
      if (it.k === "mods" && notice) await this.ensureRules(this.world.mods); // refused: put the old rules back
      this.world = world;
      if (world !== before) this.onWorld(world);
      if (notice && from === this.t.me) this.onNotice(notice);
    }
    if (b.s) step(this.world);
    this.n = b.n;
  }

  // ---------- recovery ----------

  private resync() {
    if (this.isHost()) return;
    this.syncing = true;
    this.synced = false;
    this.buffer = [];
    this.queue = [];
    this.t.setPresence({ since: this.since, synced: false });
    this.onState("syncing");
    this.askForSnapshot();
  }

  private askForSnapshot() {
    if (this.needTimer) clearTimeout(this.needTimer);
    this.t.emit("need", {});
    this.needTimer = setTimeout(() => {
      if (!this.syncing) return;
      // Nobody answered. If no synced page exists, this one starts the room.
      const others = this.t.peers().filter((p) => p.id !== this.t.me && p.synced);
      if (!others.length) this.bootstrap();
      else this.askForSnapshot();
    }, this.retryMs);
  }

  private bootstrap() {
    this.syncing = false;
    this.synced = true;
    this.epoch = this.t.me;
    this.lastQueued = this.n;
    this.t.setPresence({ since: this.since, synced: true });
    this.onState(this.state());
  }

  private async onNeed(from: string) {
    if (!this.isHost() || from === this.t.me || this.snapSaving) return;
    this.snapSaving = true;
    try {
      while (this.processing) await new Promise((r) => setTimeout(r, 10));
      const snap: Snapshot = { n: this.n, e: this.t.me, world: serializeWorld(this.world) };
      await this.t.saveSnapshot(snap);
      this.t.emit("snap", { n: snap.n, to: from });
    } finally {
      this.snapSaving = false;
    }
  }

  private async onSnap(msg: { n: number; to: string }, from: string) {
    if (!this.syncing || msg?.to !== this.t.me || from !== this.host()) return;
    const snap = await this.t.loadSnapshot();
    if (!snap || snap.n !== msg.n) return this.askForSnapshot();
    if (!(await this.ensureRules(snap.world.mods))) return this.askForSnapshot();
    this.world = deserializeWorld(snap.world);
    sanitizeWorld(this.world);
    this.n = snap.n;
    this.lastQueued = snap.n;
    this.epoch = snap.e;
    this.syncing = false;
    this.synced = true;
    if (this.needTimer) clearTimeout(this.needTimer);
    this.t.setPresence({ since: this.since, synced: true });
    this.onWorld(this.world);
    this.onState(this.state());
    const later = this.buffer.filter((b) => b.n > snap.n && b.e === snap.e).sort((a, b) => a.n - b.n);
    this.buffer = [];
    for (const b of later) {
      if (b.n !== this.lastQueued + 1) return this.resync();
      this.enqueue(b, false);
    }
  }

  /** This page's own wizard, for the UI. */
  me() {
    return playerOf(this.world, this.t.me);
  }
}
