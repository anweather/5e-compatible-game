// Transports for the lockstep session: one for playing alone, one over the
// claude.ai artifact `room` (live messages) and `db` (snapshots, mods).
import type { PeerInfo, Snapshot, Transport } from "./net.ts";

// Minimal shapes of the platform capabilities this page uses.
export interface RoomMessage { data?: unknown; peer: string; sameTab: boolean; kind: string; by: string | null }
export interface RoomPeer { peer: string; by: string | null; sameTab: boolean; isMe: boolean; kind: string; presence: Record<string, unknown> }
export interface NamedRoom {
  readonly name: string;
  emit(topic: string, data?: unknown): Promise<void>;
  on(topic: string, fn: (m: RoomMessage) => void, onError?: (e: { code: string }) => void): () => void;
  presence(patch: Record<string, unknown>): Promise<void>;
  peers(): readonly RoomPeer[];
  onPeers(fn: (c: { peers: readonly RoomPeer[] }) => void, onError?: (e: { code: string }) => void): () => void;
  connected(): boolean;
  leave(): Promise<void>;
}
export interface Lobby extends Omit<NamedRoom, "name" | "leave"> {
  join(name: string): Promise<NamedRoom>;
}
export interface DocRef {
  get(): Promise<{ exists: boolean; data(): Record<string, unknown> | undefined }>;
  set(data: Record<string, unknown>): Promise<void>;
}
export interface Db {
  doc(path: string): DocRef;
  collection(path: string): {
    onSnapshot(next: (s: { docs: { id: string; data(): Record<string, unknown> }[] }) => void, err?: (e: unknown) => void): () => void;
    doc(id?: string): DocRef & { id: string };
  };
}

/** Playing alone: you are the whole room and the host. */
export class LocalTransport implements Transport {
  readonly me = "you";
  peers(): PeerInfo[] {
    return [{ id: this.me, since: 0, synced: true }];
  }
  emit() {}
  on() {}
  setPresence() {}
  async saveSnapshot() {}
  async loadSnapshot() {
    return null;
  }
}

/** A named room on the artifact, with snapshots kept in db at rooms/<code>. */
export class RoomTransport implements Transport {
  readonly me: string;
  readonly room: NamedRoom;
  readonly code: string;
  private db: Db | null;
  private extra: Record<string, unknown> = {};

  constructor(room: NamedRoom, db: Db | null, me: string) {
    this.room = room;
    this.db = db;
    this.code = room.name;
    this.me = me;
  }

  peers(): PeerInfo[] {
    return this.room.peers()
      .filter((p) => p.kind === "viewer")
      .map((p) => ({
        id: p.peer,
        since: typeof p.presence.since === "number" ? p.presence.since : Number.MAX_SAFE_INTEGER,
        synced: p.presence.synced === true,
      }));
  }
  emit(topic: string, data: unknown) {
    this.room.emit(topic, data).catch(() => { /* dropped; lockstep recovers */ });
  }
  on(topic: string, fn: (data: unknown, from: string) => void) {
    this.room.on(topic, (m) => {
      if (m.kind === "viewer") fn(m.data, m.peer);
    });
  }
  setPresence(p: { since: number; synced: boolean }) {
    void this.room.presence({ ...this.extra, since: p.since, synced: p.synced }).catch(() => {});
  }
  /** Display fields (colour, id) that ride along with the sync fields. */
  decorate(fields: Record<string, unknown>) {
    this.extra = { ...this.extra, ...fields };
    void this.room.presence(fields).catch(() => {});
  }
  async saveSnapshot(s: Snapshot) {
    if (!this.db) return;
    await this.db.doc(`rooms/${this.code}`).set({ n: s.n, e: s.e, world: s.world as unknown as Record<string, unknown>, at: Date.now() });
  }
  async loadSnapshot(): Promise<Snapshot | null> {
    if (!this.db) return null;
    const snap = await this.db.doc(`rooms/${this.code}`).get();
    if (!snap.exists) return null;
    const d = snap.data()!;
    return { n: d.n as number, e: d.e as string, world: d.world as Snapshot["world"] };
  }
  async leave() {
    await this.room.leave().catch(() => {});
  }
}

/** Joins a named room and waits until this page can see itself in it. */
export async function joinRoom(lobby: Lobby, code: string): Promise<NamedRoom & { me: string }> {
  const room = await lobby.join(code);
  for (let i = 0; i < 60; i++) {
    const self = room.peers().find((p) => p.sameTab);
    if (self) return Object.assign(room, { me: self.peer });
    await new Promise((r) => setTimeout(r, 100));
  }
  throw new Error("The room didn't answer. Try again in a moment.");
}

export function randomCode(): string {
  const words = ["ember", "frost", "gale", "rime", "spark", "mole", "newt", "frog", "moth", "finch", "grease", "steam"];
  const w = words[Math.floor(Math.random() * words.length)];
  return `${w}-${Math.floor(Math.random() * 9000 + 1000)}`;
}
