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
            ("Player size: how tall or wide a player is", "nothing yet; the Shapeshift spell is the nearest thing a player can do"),
            ("Fauna size: how big turkeys, deer and the other ambient life are", "nothing yet"),
            ("Slippery terrain: only a cover can be slippery, not a terrain", "a cover tagged slippery spread over the ground, such as Grease"),
            ("Weather's cover: what rain, snow and fog lay on the ground", "weather's chances and strengths in sim can be tuned, and a cover's look changes how snow looks"),
            ("Area fill: covering the land past a spell's radius cap", "a burst of the capped radius, or a change to the terrain itself (\"all the grass burns like oil\") with set_material and set_look"),
            ("A building's rooms: walls and doors inside it", "a building's outside is a row: add_building, add_wing and set_building change its walls, openings and fill"),
        };

        /// <summary>What the rules can't reach yet, and the nearest thing that can be done.</summary>
        public static string Outside() =>
            "These aren't in the rules yet, so no tool can change them. Tell the player so, and offer the nearest thing:\n" +
            string.Join("\n", OutsideRules.Select(o => $"- {o.what}. Instead: {o.instead}."));
    }
}
