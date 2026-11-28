// What a world mode is to the world (D22, docs/design/minigames.md): a class above the core, made for one world and
// installed on it once (World.Install), that the world calls at fixed places, in the order the modes were installed.
// Every member does nothing until a mode overrides it, and overriding one is how a mode hears of it: its slot (its part
// of the save, the casts checksum text and the dragon's start, each written where the world calls it), the events
// (a map laid, a chunk reset by the dragon, a spell on one of its bodies, its bodies' steps) and the acts of a kind the
// world doesn't know. A joiner's world takes the same modes, in the same order, through MadeFor. The Hold's PlayModes is
// one; golf's GolfMode is another. The Hold's, its progress', the defense field's and the course's state was the world's
// before it was PlayModes', so it keeps the places it had in the save and the checksums (SaveAt, TextAt: the save's
// head, after the open world's fills, after the dragon; after the modes' slots, after the stands, before the hands, and
// the defense field's own hash), where a new mode writes in its slot. The rest are what the world asks of the modes as
// it runs: their tick (the Rest stage, after the raiders), a moment, the defense and course commands, a spell zone
// against a cast, the stands they read, and what a run holds for a wizard's hands (what's held back and open, points
// brought, the kit, an attuning barred, the upgrades picked, a pick).
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

        public virtual void SaveAt(SavePlace place, JObject save)
        {
        }

        public virtual string TextAt(TextPlace place) => "";

        public virtual void Tick()
        {
        }

        public virtual bool StartMoment(string id) => false;

        public virtual bool Apply(DefenseCommand command) => false;

        public virtual bool Apply(CourseCommand command) => false;

        public virtual SpellZone ZoneAgainst(SpellDef spell, int x, int y) => null;

        public virtual bool ReadsStand(int x, int y) => false;

        public virtual bool HoldsBack(string player) => false;

        public virtual bool Opens(string player, Func<UnlockDef, bool> gives) => true;

        public virtual bool Bring(string player, int points) => false;

        public virtual bool KitFor(string player) => false;

        public virtual bool AttuneBarred(string player, string element) => false;

        public virtual IEnumerable<UpgradeDef> UpgradesOf(string player) => Array.Empty<UpgradeDef>();

        public virtual bool Pick(string player, string upgrade) => false;

        public virtual bool KnowsAct(string kind, string what) => false;

        public virtual int Act(string kind, string what, List<(int x, int y)> cells) => 0;

        public virtual string ActStays(string kind) => null;
    }

    public enum SavePlace
    {
        Head,
        AfterOpen,
        AfterDragon,
    }

    public enum TextPlace
    {
        AfterSlots,
        AfterStands,
        BeforeHands,
        Field,
    }
}
