// What Claude can't change through the rules yet. The one place that says so: when one
// of these becomes data (J-057, J-058), take its line out.
using System.Linq;

namespace Chem.Mcp
{
    public sealed partial class Workbench
    {
        static readonly (string what, string instead)[] OutsideRules =
        {
            ("Heat glow: the colour of hot metal and hot stone", "nothing yet: a material's own look and colour can change, not what heat does to it"),
            ("One animal apart from its kind: the turkey near you, not all turkeys, or steering one to a place (\"take the deer to the lake\"). Animals live in each player's own view, so none can be picked out or led", "act brings one out where it's wanted (act deer near lake), set_species changes the whole species (its speed, size and habits), and a new species and act bring a different one out beside you"),
            ("The lake's level, and the sea's: they're fixed in the land", "water poured somewhere runs and pools; Create Water, or a flood, in a place"),
            ("A mountain over 48 m in one go: a land stamp rises at most 1.5 m a cell of radius (land.steepest), so its sides stay climbable, and reaches 32 cells at most, so 100 m comes out as the tallest the radius allows", "act land with radius 32 and rise 48, the tallest one stamp; a spell raises or digs only a few metres"),
            ("Slippery terrain: only a cover can be slippery, not a terrain", "a cover tagged slippery spread over the ground, such as Grease"),
            ("Weather's cover: what rain, snow and fog lay on the ground", "weather's chances and strengths in sim can be tuned, and a cover's look changes how snow looks"),
            ("Area fill: covering the land past a spell's radius cap", "scatter for many trees, bushes, a cover or things over an area near a place (\"50 trees near the lake\"), a burst of the capped radius, or a change to the terrain itself (\"all the grass burns like oil\") with set_material and set_look"),
            ("A creature's colours part by part: one animal wears one coat over all its slots, and a graft's coat own isn't drawn yet", "coat or coats colour the whole animal, hair its hair; a part with a colour of its own (hooves, eyes) keeps it"),
            ("A building's rooms: walls and doors inside it", "a building's outside is a row: add_building, add_wing and set_building change its walls, openings and fill"),
        };

        /// <summary>What the rules can't reach yet, and the nearest thing that can be done.</summary>
        public static string Outside() =>
            "These aren't in the rules yet, so no tool can change them. Tell the player so, and offer the nearest thing:\n" +
            string.Join("\n", OutsideRules.Select(o => $"- {o.what}. Instead: {o.instead}."));
    }
}
