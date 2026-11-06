// A run of the Hold with scripted wizards and no view (docs/design/the-hold.md, phase 4). A wizard stands on the wall nearest the
// spawners, so ground raiders can't reach it, and casts at the raider nearest the heart: a throw while the raider is out of short
// range, a thrust inside it, and both hands' combined spell when a crowd is in range and both are ready. It never moves, dodges,
// attunes mid-run, sweeps, or reads a wave: a stand-in for a player who keeps casting, not a good one.
using System;
using System.Collections.Generic;
using System.Linq;

namespace Chem.HoldCurve
{
    /// <summary>What a run did at the end of each wave.</summary>
    public sealed class WaveResult
    {
        public int Wave, Seconds, Health;
        public string Theme;
    }

    public sealed class HoldRun
    {
        const int CrowdForBoth = 3;

        readonly World world;
        readonly HandsDef table;
        readonly List<(int x, int y)> posts = new List<(int, int)>();
        readonly int peers;

        public List<WaveResult> Waves { get; } = new List<WaveResult>();
        public int LastWave => world.Hold?.Wave ?? 0;
        public int Ticks { get; private set; }
        public World World => world;

        public HoldRun(Rules rules, string worldDir, uint seed, int peers, string left, string right, string moment = null)
        {
            this.peers = peers;
            table = rules.Sim.Hold.Hands;
            var site = WorldHold.Load(worldDir);
            world = new World(rules, 576, 576, seed);
            Lay(worldDir, site);
            world.SetHold(site);
            var wall = BuildingRaster.Cells(BuildingRow.Read(site.Castle, site.Row, rules.Buildings), rules.Buildings)
                .Where(c => c.Part == BuildingPart.Wall && world.At(c.X, c.Y).T == "stone")
                .OrderBy(c => Far(c.X, c.Y, site.Spawners[0].Cells[0])).ToList();
            for (int i = 0; i < peers; i++) posts.Add((wall[i * 3].X, wall[i * 3].Y));
            var room = ((site.AttuningFrom.x + site.AttuningTo.x) / 2, (site.AttuningFrom.y + site.AttuningTo.y) / 2);
            for (int i = 0; i < peers; i++)
            {
                world.ApplyStrike(StrikeCommand.Stand(Name(i), room.Item1, room.Item2, 1000000));
                world.ApplyCombo(ComboCommand.Attune(Name(i), HandsDef.Left, left));
                world.ApplyCombo(ComboCommand.Attune(Name(i), HandsDef.Right, right));
            }
            if (moment != null) world.ApplyCombo(ComboCommand.Moment(Name(0), moment));
            else
            {
                world.ApplyStrike(StrikeCommand.Stand(Name(0), site.Plate.x, site.Plate.y, 1000000));
                for (int i = 0; i <= rules.Sim.Hold.PlateTicks; i++) world.Step();
            }
            for (int i = 0; i < peers; i++) world.ApplyStrike(StrikeCommand.Stand(Name(i), posts[i].x, posts[i].y, 1000000));
        }

        static string Name(int i) => "bot" + i;

        static int Far(int x, int y, (int x, int y) to) => Math.Abs(x - to.x) + Math.Abs(y - to.y);

        // Open grass, the map's trees, and the rows: what raiders walk on.
        void Lay(string worldDir, WorldHold site)
        {
            var rows = MapFile.ReadCells(worldDir).Replace("\r", "").Trim('\n').Split('\n');
            var cells = site.Spawners.SelectMany(s => s.Cells).Concat(new[] { site.Heart }).ToList();
            const int round = 40;
            for (int y = cells.Min(c => c.y) - round; y < cells.Max(c => c.y) + round; y++)
                for (int x = cells.Min(c => c.x) - round; x < cells.Max(c => c.x) + round; x++)
                    world.Paint(x, y, y < rows.Length && x < rows[y].Length && rows[y][x] == 'T' ? "tree" : "grass");
            WorldBuildings.Load(worldDir).LayInto(world);
        }

        /// <summary>Plays until the run ends, this wave is passed (with <paramref name="untilWave"/>), or the time runs out.</summary>
        public void Play(int mostSeconds, int untilWave = int.MaxValue)
        {
            int tps = world.Rules.Sim.TicksPerSecond, seen = world.Hold.Wave;
            for (; Ticks < mostSeconds * tps && world.HoldRunning && world.Hold.Wave <= untilWave; Ticks++)
            {
                for (int i = 0; i < peers; i++) Think(Name(i), posts[i]);
                world.Step();
                foreach (var fired in world.CombosFired) Aim(fired);
                if (world.Hold.Wave == seen) continue;
                Waves.Add(new WaveResult { Wave = seen, Seconds = Ticks / tps, Health = world.Hold.Health, Theme = world.Hold.Theme });
                seen = world.Hold.Wave;
            }
        }

        Raider Target((int x, int y) from, int range) => world.Raiders
            .Where(r => Dist(from, r) <= range)
            .OrderBy(r => Far(r.X, r.Y, world.HoldSite.Heart)).ThenBy(r => r.Id).FirstOrDefault();

        static double Dist((int x, int y) a, Raider r) => Math.Sqrt((a.x - r.X) * (double)(a.x - r.X) + (a.y - r.Y) * (double)(a.y - r.Y));

        void Think(string me, (int x, int y) post)
        {
            if (world.CastOf(me)?.Casting == true) return;
            int shortRange = table.RangeOf(HandsDef.Thrust), longRange = table.RangeOf(HandsDef.Throw);
            if (Target(post, longRange) == null) return;
            bool close = Target(post, shortRange) != null, crowd = world.Raiders.Count(r => Dist(post, r) <= longRange) >= CrowdForBoth;
            bool leftReady = world.HandCooling(me, HandsDef.Left) == 0, rightReady = world.HandCooling(me, HandsDef.Right) == 0;
            if (crowd && leftReady && rightReady)
            {
                world.ApplyCombo(ComboCommand.Hand(me, HandsDef.Right, !close));
                world.ApplyCombo(ComboCommand.Hand(me, HandsDef.Left, !close));
            }
            else if (rightReady) world.ApplyCombo(ComboCommand.Hand(me, HandsDef.Right, !close));
            else if (leftReady) world.ApplyCombo(ComboCommand.Hand(me, HandsDef.Left, !close));
        }

        void Aim(ComboFired fired)
        {
            if (fired.Spell == null) return;
            int i = int.Parse(fired.Player.AsSpan(3), provider: System.Globalization.CultureInfo.InvariantCulture);
            var me = posts[i];
            var target = Target(me, Math.Max(fired.Range, 1));
            if (target == null) return;
            var spell = world.Rules.Spell(fired.Spell);
            int aimX = target.X - me.x, aimY = target.Y - me.y;
            int dir = World.DirOf(aimX, aimY);
            if (spell.Origin != "self") world.Cast(new CastCommand(fired.Spell, target.X, target.Y, dir));
            else if (spell.Shape == "burst") world.Cast(new CastCommand(fired.Spell, me.x, me.y, dir));
            else world.Cast(new CastCommand(fired.Spell, me.x + Math.Sign(aimX), me.y + Math.Sign(aimY), dir, 1, aimX, aimY));
        }
    }
}
