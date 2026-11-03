using Chem;
using UnityEngine;

// The wizard's side of a triggered cast's show (World.Triggered, docs/design/events-and-placing.md 2c):
// the cast a rule made on this player's step plays from their hand as a cast of the spell does (the
// projectile, the meteor from the sky, the impact, sound and light), without the gesture, since the
// wizard is walking, and landing when the core does. The cells are the core's, already changed (or
// marked for a delayed one); this is only the show. ChemGrid.RenderTriggered picks the wizard.
public partial class SpellCaster
{
    /// <summary>The player this wizard stands for in the core's stands, on every machine: its client's online, the solo one alone.</summary>
    public string StandPlayer => TryGetComponent<NetWizard>(out var wizard) && wizard.IsSpawned ? wizard.CastPlayer : SoloPlayer;

    /// <summary>
    /// Plays a triggered cast's show: what this spell's cast shows, landing in <paramref name="seconds"/> (its
    /// delay; at once for a cast that lands at once, a meteor then falling the shortest it can).
    /// </summary>
    public void PlayTriggered(TriggeredCast cast, float seconds)
    {
        if (grid == null || grid.World == null) return;
        var command = new CastCommand(cast.Spell, cast.X, cast.Y, cast.Dir);
        var def = grid.World.Rules.Spell(cast.Spell);
        var look = LookOf(cast.Spell);
        var landing = Landing(command);
        GameSounds.Play(GameSounds.CastOf(def), Hand);
        if (look is { fromSky: true }) Unleash(def, look, landing, command, null, seconds);
        else if (seconds > 0f) StartCoroutine(AtRelease(seconds, () => Unleash(def, look, landing, command, null)));
        else Unleash(def, look, landing, command, null);
    }
}
