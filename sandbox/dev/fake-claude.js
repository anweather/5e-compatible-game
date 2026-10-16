// A stand-in for the claude.ai artifact platform, for local development only.
// It implements just enough of `room`, `db` and `user` that two tabs of
// dist/dev.html on one machine behave like two friends in a room.
// Messages travel over BroadcastChannel; db documents live in localStorage.
(() => {
  const rid = () => Math.random().toString(36).slice(2, 10);
  const peer = rid() + rid();
  const uid = sessionStorage.getItem("fake.uid") || `u-${rid()}`;
  sessionStorage.setItem("fake.uid", uid);
  const since = Date.now();

  // ---------- room ----------
  const bus = new BroadcastChannel("fake-claude-room");
  const rooms = new Map(); // name -> state

  function makeRoom(name) {
    const st = { name, presence: {}, peers: new Map(), handlers: new Map(), peerFns: [], left: false };
    const self = () => ({ peer, by: uid, isMe: true, sameTab: true, kind: "viewer", guest: false, presence: Object.freeze({ ...st.presence }), updatedAt: Date.now() });
    const list = () => [self(), ...[...st.peers.values()].map((p) => ({ ...p, isMe: false, sameTab: false }))];
    const notify = () => { const peers = Object.freeze(list()); for (const fn of st.peerFns) fn({ peers, joined: [], left: [], updated: [] }); };
    const send = (msg) => bus.postMessage({ room: name, from: peer, by: uid, ...msg });
    st.notify = notify;
    st.announce = () => !st.left && send({ type: "presence", presence: st.presence });
    const api = {
      name,
      emit: async (topic, data) => {
        const json = JSON.stringify(data ?? null);
        if (json.length > 4096) throw Object.assign(new Error("data over 4 KiB"), { code: "invalid_argument" });
        send({ type: "emit", topic, data: JSON.parse(json) });
        // The real room echoes to the sender too.
        setTimeout(() => (st.handlers.get(topic) || []).forEach((fn) => fn({ topic, data: JSON.parse(json), peer, by: uid, isMe: true, sameTab: true, kind: "viewer", guest: false })), 0);
      },
      on: (topic, fn) => {
        st.handlers.set(topic, [...(st.handlers.get(topic) || []), fn]);
        return () => st.handlers.set(topic, (st.handlers.get(topic) || []).filter((f) => f !== fn));
      },
      presence: async (patch) => {
        for (const [k, v] of Object.entries(patch)) { if (v === null) delete st.presence[k]; else st.presence[k] = v; }
        st.announce();
        notify();
      },
      peers: () => Object.freeze(list()),
      onPeers: (fn) => { st.peerFns.push(fn); setTimeout(notify, 0); return () => { st.peerFns = st.peerFns.filter((f) => f !== fn); }; },
      connected: () => true,
      onConnection: (fn) => { setTimeout(() => fn(true), 0); return () => {}; },
      leave: async () => { st.left = true; send({ type: "bye" }); rooms.delete(name); },
    };
    rooms.set(name, st);
    st.api = api;
    send({ type: "hello" });
    st.announce();
    return api;
  }

  bus.onmessage = ({ data: m }) => {
    const st = rooms.get(m.room);
    if (!st || m.from === peer) return;
    if (m.type === "hello") return st.announce();
    if (m.type === "bye") { st.peers.delete(m.from); return st.notify(); }
    if (m.type === "presence") {
      st.peers.set(m.from, { peer: m.from, by: m.by, kind: "viewer", guest: false, presence: Object.freeze({ ...m.presence }), updatedAt: Date.now(), seen: Date.now() });
      return st.notify();
    }
    if (m.type === "emit") {
      const p = st.peers.get(m.from);
      if (p) p.seen = Date.now();
      for (const fn of st.handlers.get(m.topic) || []) fn({ topic: m.topic, data: m.data, peer: m.from, by: m.by, isMe: false, sameTab: false, kind: "viewer", guest: false });
    }
  };
  setInterval(() => {
    for (const st of rooms.values()) {
      st.announce();
      let changed = false;
      for (const [id, p] of st.peers) if (Date.now() - p.seen > 3500) { st.peers.delete(id); changed = true; }
      if (changed) st.notify();
    }
  }, 1000);
  addEventListener("pagehide", () => { for (const st of rooms.values()) bus.postMessage({ room: st.name, from: peer, type: "bye" }); });

  const lobby = makeRoom("~lobby");
  lobby.join = async (name) => rooms.get(name)?.api ?? makeRoom(name);

  // ---------- db ----------
  const dbBus = new BroadcastChannel("fake-claude-db");
  const subs = [];
  const key = (path) => `fakedb:${path}`;
  const read = (path) => { const v = localStorage.getItem(key(path)); return v ? JSON.parse(v) : undefined; };
  const changed = () => { for (const s of subs) s(); };
  dbBus.onmessage = changed;
  const docRef = (path) => ({
    id: path.split("/").pop(),
    path,
    get: async () => { const d = read(path); return { exists: d !== undefined, data: () => d }; },
    set: async (data) => { localStorage.setItem(key(path), JSON.stringify(data)); dbBus.postMessage(path); changed(); },
    update: async (data) => { localStorage.setItem(key(path), JSON.stringify({ ...read(path), ...data })); dbBus.postMessage(path); changed(); },
    delete: async () => { localStorage.removeItem(key(path)); dbBus.postMessage(path); changed(); },
  });
  const db = {
    doc: docRef,
    collection: (path) => ({
      path,
      doc: (id) => docRef(`${path}/${id ?? rid() + rid()}`),
      onSnapshot: (next) => {
        const fire = () => {
          const docs = [];
          for (let i = 0; i < localStorage.length; i++) {
            const k = localStorage.key(i);
            if (!k.startsWith(`fakedb:${path}/`)) continue;
            const rest = k.slice(`fakedb:${path}/`.length);
            if (rest.includes("/")) continue;
            const d = JSON.parse(localStorage.getItem(k));
            docs.push({ id: rest, data: () => d });
          }
          next({ docs, size: docs.length, empty: !docs.length });
        };
        subs.push(fire);
        setTimeout(fire, 0);
        return () => subs.splice(subs.indexOf(fire), 1);
      },
    }),
  };

  // ---------- user ----------
  const user = {
    id: async () => uid,
    isOwner: async () => true,
    canEdit: async () => true,
    can: async () => true,
    profiles: async (ids) => Object.fromEntries(ids.map((id) => [id, { id, name: `Player ${id.slice(-3)}` }])),
  };

  const caps = { room: lobby, db, user };
  window.claude = { use: async (name) => caps[name] ?? null };
})();
