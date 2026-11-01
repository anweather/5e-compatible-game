// The world tools that read (docs/design/world-tools.md G1): places() and here().
// Places come from world/places.json beside rules/ (Places.cs); where the asking
// player stands comes from an IHere the game fills in, or the stand-in a headless
// run has. Read-only: nothing in the world or the rules changes.
using System;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Chem.Mcp
{
    /// <summary>Where the player asking stands: their name (as the core keys players), their cell, and the cell they look at.</summary>
    public readonly struct HereSpot
    {
        public HereSpot(string player, int x, int y, (int x, int y)? looking = null)
        {
            Player = player;
            X = x;
            Y = y;
            Looking = looking;
        }

        public string Player { get; }
        public int X { get; }
        public int Y { get; }
        /// <summary>The cell under the player's aim, or null when they aim at nothing.</summary>
        public (int x, int y)? Looking { get; }
    }

    /// <summary>
    /// The player asking, as the running game knows them: the game fills this in (the view knows who is
    /// at the keyboard and where they stand), and a headless run uses a StandInHere. Called off the
    /// game's main thread, so the game answers from a copy it keeps up to date.
    /// </summary>
    public interface IHere
    {
        /// <summary>The player asking and where they stand; null when no player is in the game.</summary>
        HereSpot? Asking();

        /// <summary>Whether this is a stand-in (no game attached), which here() says.</summary>
        bool StandIn { get; }
    }

    /// <summary>A headless run's player: the host, at a cell given when the server starts (--here x,y).</summary>
    public sealed class StandInHere : IHere
    {
        readonly HereSpot? spot;

        public StandInHere(HereSpot? spot) { this.spot = spot; }

        public HereSpot? Asking() => spot;
        public bool StandIn => true;
    }

    public sealed partial class Workbench
    {
        /// <summary>The world's places (world/places.json beside rules/): what places() lists and a look's where may name.</summary>
        public WorldPlaces Places => Budgets.Places;

        /// <summary>
        /// Where the asking player stands. A headless run stands in the host at the middle of the valley's
        /// grid; the game sets its own.
        /// </summary>
        public IHere Here { get; set; } = new StandInHere(new HereSpot(HostName, 288, 288));

        /// <summary>places(): every named place, one line each.</summary>
        public string ListPlaces()
        {
            if (Places.Count == 0) return "The world has no named places (world/places.json is missing or empty).";
            var sb = new StringBuilder($"{Places.Count} places (world/places.json; cells are [x, y], y growing south, 1.5 m each):");
            foreach (var p in Places.All)
            {
                var b = p.Bounds;
                sb.Append(FormattableString.Invariant($"\n- {p.Id}: {p.Name}, {Article(p.Kind)} {p.Kind}, {p.Count} cells from [{b.X}, {b.Y}] to [{b.Right - 1}, {b.Bottom - 1}], middle [{p.Middle.x}, {p.Middle.y}]"));
                sb.Append(p.Generated ? $" (from {p.From})" : " (by hand)");
            }
            sb.Append("\nA look's where names one by id: {\"place\": \"<id>\", \"within\": cells round it}.");
            return sb.ToString();
        }

        /// <summary>here(): where the asking player stands, the place they're in, what's near, and what they look at.</summary>
        public string DescribeHere(int within = 120)
        {
            var spot = Here?.Asking();
            if (spot == null) return "No player is in the game, so there's no here; a look round a player needs one.";
            var me = spot.Value;
            var sb = new StringBuilder(string.Format(CultureInfo.InvariantCulture, "You are {0}, at cell [{1}, {2}]", me.Player, me.X, me.Y));
            if (Here.StandIn) sb.Append(" (a stand-in: no game is attached, so this is where a headless run puts the host)");
            sb.Append('.');
            var inside = Places.All.Where(p => p.Contains(me.X, me.Y)).ToList();
            sb.Append(inside.Count == 0 ? "\nIn no named place." : $"\nIn {string.Join(" and ", inside.Select(p => $"{p.Name} ({p.Id}, {Article(p.Kind)} {p.Kind})"))}.");
            var near = Places.All.Where(p => !inside.Contains(p)).Select(p => (place: p, d: p.DistanceTo(me.X, me.Y)))
                .Where(n => n.d <= within).OrderBy(n => n.d).ThenBy(n => n.place.Id, StringComparer.Ordinal).ToList();
            if (near.Count == 0) sb.Append(FormattableString.Invariant($"\nNo named place within {within} cells."));
            else
            {
                sb.Append(FormattableString.Invariant($"\nNear, within {within} cells:"));
                foreach (var (place, d) in near)
                    sb.Append(FormattableString.Invariant($"\n- {place.Name} ({place.Id}, {Article(place.Kind)} {place.Kind}): {Math.Round(d)} cells {Toward(me.X, me.Y, place)}"));
            }
            if (me.Looking is (int lx, int ly))
            {
                var at = Places.All.Where(p => p.Contains(lx, ly)).Select(p => p.Name).ToList();
                sb.Append(FormattableString.Invariant($"\nLooking at cell [{lx}, {ly}]")).Append(at.Count == 0 ? "." : $", in {string.Join(" and ", at)}.");
            }
            sb.Append("\nA look round you: set_look with where {\"around\": \"me\", \"within\": 10}; it follows you as you move, and lapses while you're not in the game.");
            return sb.ToString();
        }

        // The way to a place's nearest cell, in compass words (y grows south).
        static string Toward(int x, int y, Place place)
        {
            var (nx, ny) = place.Cells().OrderBy(c => (long)(c.x - x) * (c.x - x) + (long)(c.y - y) * (c.y - y)).ThenBy(c => c.y).ThenBy(c => c.x).First();
            double angle = Math.Atan2(-(ny - y), nx - x) * 180 / Math.PI;
            string[] ways = { "east", "north-east", "north", "north-west", "west", "south-west", "south", "south-east" };
            return ways[(int)Math.Round((angle + 360) % 360 / 45) % 8];
        }

        static string Article(string word) => !string.IsNullOrEmpty(word) && "aeiou".Contains(word[0]) ? "an" : "a";

        /// <summary>The player "me" means: the one asking, or the reason there's none.</summary>
        string Me() =>
            Here?.Asking()?.Player ?? throw new RulesToolException("No player is in the game, so \"around\": \"me\" has no one to follow; give a place or a cell instead.");
    }
}
