// What a world mode is to the world (D22, docs/design/minigames.md): a class above the core, made for one world and
// installed on it once (World.Install), that the world calls at fixed places, in the order the modes were installed.
// Every member does nothing until a mode overrides it, and overriding one is how a mode hears of it: its slot (its part
// of the save, the casts checksum text and the dragon's start, each written where the world calls it), the events
// (a map laid, a chunk reset by the dragon, a spell on one of its bodies, its bodies' steps) and the acts of a kind the
// world doesn't know. A joiner's world takes the same modes, in the same order, through MadeFor. The Hold's PlayModes is
// one (and the world's IWorldModes too); golf's GolfMode is another.
using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Chem
{
    public abstract class WorldMode
    {
        static readonly Body[] NoBodies = Array.Empty<Body>();

        protected WorldMode(World world)
        {
            World = world ?? throw new ArgumentNullException(nameof(world));
        }

        public World World { get; }

        public abstract WorldMode MadeFor(World world);

        public virtual void LoadSlot(JObject save)
        {
        }

        public virtual void SaveSlot(JObject save)
        {
        }

        public virtual string SlotText() => "";

        public virtual void KeepStart()
        {
        }

        public virtual void SaveStart(JObject start)
        {
        }

        public virtual void MapLaid(string dir)
        {
        }

        public virtual void ChunkReset(int cx, int cy)
        {
        }

        public virtual IReadOnlyList<Body> Bodies => NoBodies;

        public virtual void SpellStruck(Body body, SpellDef spell, int dir)
        {
        }

        public virtual void SpellLanded(Body body, SpellDef spell, List<(int x, int y)> cells, (int x, int y) landed, int dir, int charge)
        {
        }

        public virtual bool BodyMoves(Body body) => true;

        public virtual int BodyEvery(Body body) => 1;

        public virtual bool BodyMayEnter(Body body, int x, int y) => true;

        public virtual void BodyLeft(Body body)
        {
        }

        public virtual void BodyBounced(Body body, int x, int y)
        {
        }

        public virtual bool BodyArrived(Body body) => false;

        public virtual void BodyStopped(Body body)
        {
        }

        public virtual bool KnowsAct(string kind, string what) => false;

        public virtual int Act(string kind, string what, List<(int x, int y)> cells) => 0;

        public virtual string ActStays(string kind) => null;
    }
}
