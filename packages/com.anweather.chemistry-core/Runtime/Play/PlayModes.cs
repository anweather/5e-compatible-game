// The game modes a world plays (docs/design/architecture-review.md step 9, D9, D22): the defense field, the Hold and the
// Gauntlet's course, Chem.Play's systems, each with its own state, and the cost field the first two work out their ways
// in. A WorldMode: the world calls it in install order where it always called these. Its state keeps the places it had
// in the save and the checksums when the world kept it: "defense" in the save's head, "hold" after the open world's
// fills, "course" after the dragon; the zones and the runs after the modes' slots in the casts checksum text, the Hold
// after the stands, the run's wizards after their points; and the defense field's own hash. A map's hold.json lays the
// Hold's site. Install puts them on a world once, before it ticks or loads what a mode does: every place that makes a
// world to play (the view, the runners, the tools, a peer) installs them, and a joiner's world takes them from the one
// it replaces (World.InstallModesOf). World.Play() is how code outside the core reaches them.
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Chem
{
    public sealed class PlayModes : WorldMode
    {
        readonly WayField ways = new WayField();

        PlayModes(World world)
            : base(world)
        {
            Course = new CourseSystem(world);
            Defense = new DefenseSystem(world, ways);
            Hold = new HoldSystem(world, ways, Defense);
        }

        public static World Install(World world)
        {
            world.Install(new PlayModes(world));
            return world;
        }

        public CourseSystem Course { get; }

        public DefenseSystem Defense { get; }

        public HoldSystem Hold { get; }

        public override WorldMode MadeFor(World world) => new PlayModes(world);

        public override void LoadSlot(JObject save)
        {
            if (save == null) return;
            Course.Load(save);
            Hold.Load(save);
            Defense.Load(save);
        }

        public override void SaveAt(SavePlace place, JObject save)
        {
            if (place == SavePlace.Head) Defense.Save(save);
            else if (place == SavePlace.AfterOpen) Hold.Save(save);
            else if (place == SavePlace.AfterDragon) Course.Save(save);
        }

        public override string TextAt(TextPlace place) => place switch
        {
            TextPlace.AfterSlots => Course.ZoneText() + Course.Text(),
            TextPlace.AfterStands => Hold.Text(),
            TextPlace.AfterPoints => Hold.RunText(),
            TextPlace.Field => Defense.FieldText(),
            _ => "",
        };

        public override void MapLaid(string dir)
        {
            if (WorldHold.Load(dir) is { } site && World.InBounds(site.Heart.x, site.Heart.y)) Hold.Set(site);
        }

        public override void Tick()
        {
            Defense.Tick();
            Hold.Tick();
        }

        public override bool StartMoment(string id) => Hold.StartMoment(id);

        public override bool Apply(DefenseCommand command)
        {
            Defense.Apply(command);
            return true;
        }

        public override bool Apply(CourseCommand command)
        {
            Course.Apply(command);
            return true;
        }

        public override SpellZone ZoneAgainst(SpellDef spell, int x, int y) => Course.ZoneAgainst(spell, x, y);

        public override bool ReadsStand(int x, int y) => Hold.ReadsStand(x, y);

        public override bool InRun => Hold.Running;

        public override bool InMoment => Hold.State?.Moment != null;

        public override bool KitFor(string player) => Hold.KitFor(player);

        public override bool AttuneBarred(string player, string element) => Hold.AttuneBarred(player, element);

        public override IEnumerable<UpgradeDef> UpgradesOf(string player) => Hold.UpgradesOf(player);

        public override bool Pick(string player, string upgrade) => Hold.Pick(player, upgrade);
    }
}
