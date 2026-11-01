using Chem;
using Chem.Mcp;
using UnityEngine;

// Where the player at this keyboard stands, for the rules MCP's here() and a look "around me".
// The tools ask off the main thread, so RulesMcpServer keeps a copy up to date here each frame
// and they read that. Whoever asks through this game is this player (GameRoomDesk).
public sealed class GameHere : IHere
{
    volatile object latest;
    SpellCaster mine;

    public bool StandIn => false;

    public HereSpot? Asking() => latest is HereSpot spot ? spot : null;

    /// <summary>Once a frame on the main thread: this player, their slot, their cell and the cell they aim at.</summary>
    public void Follow()
    {
        if (mine == null)
        {
            var wizard = WizardRig.FindLocal();
            if (wizard == null || !wizard.TryGetComponent(out mine))
            {
                latest = null;
                return;
            }
        }
        var switcher = Object.FindAnyObjectByType<RuleSwitcher>();
        var grid = switcher != null ? switcher.GetComponent<ChemGrid>() : null;
        if (grid == null || grid.World == null) return;
        var at = grid.CellAt(mine.transform.position);
        var aim = mine.LookingAt;
        latest = new HereSpot(switcher.Me, at.x, at.y, aim.HasValue ? (aim.Value.x, aim.Value.y) : null, ChemGrid.SlotOf(mine));
    }
}
