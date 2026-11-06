using Chem;
using UnityEngine;
using UnityEngine.InputSystem;

// The player's progress in the Hold (docs/design/the-hold.md, phase 5; J-038): their points live in their own save, not in any world.
// This reads the save, tells every peer what it says by a command whenever the world doesn't know (a new world, a reset, joining), and
// writes what the world has earned. Dev builds and the Editor start with everything, as a playtest needs; F7 there plays as the save
// says. What the player sees of it is in the other parts: the offer between waves and the run's picks (ProgressKeeper.Offer.cs), and
// the Esc menu's page and a run's end (ProgressKeeper.Page.cs).
[RequireComponent(typeof(ChemGrid))]
public partial class ProgressKeeper : MonoBehaviour
{
    const float AskEvery = 1f;

    ChemGrid grid;
    ProgressFile file;
    bool fromTheSave;
    // Whether the world has had the points this side brings since F7 last changed them.
    bool heard;
    float askedAt = float.NegativeInfinity;
    int extraHealth;

    /// <summary>The points this player should have here: everything in a dev build, unless asked for the save's.</summary>
    public int Points => HoldMoments.Offered && !fromTheSave ? World.AllPoints : file.Points;

    /// <summary>Whether this dev build plays as the save says (F7); always so in a released player.</summary>
    public bool FromTheSave => fromTheSave || !HoldMoments.Offered;

    /// <summary>The points in the player's own save, whatever this build plays with.</summary>
    public int Saved => file.Points;

    void Awake()
    {
        grid = GetComponent<ChemGrid>();
        file = new ProgressFile(ChemGrid.Folders.PlayerData ?? Application.persistentDataPath);
        file.Load();
        if (file.Trouble != null) EventLog.Add(file.Trouble);
        AddPage();
    }

    void OnDestroy() => RemovePage();

    /// <summary>F7's switch, for the probe too: play as the save says, or with everything.</summary>
    public void PlayFromTheSave(bool yes)
    {
        if (!HoldMoments.Offered || fromTheSave == yes) return;
        fromTheSave = yes;
        heard = false;
        EventLog.Add(fromTheSave ? $"Playing with your own unlocks: {file.Points} points" : "Playing with everything unlocked");
        EventLog.TrackedCaster?.BringPoints(Points);
    }

    void Update()
    {
        var world = grid.World;
        var caster = EventLog.TrackedCaster;
        if (world?.HoldSite == null || caster == null || caster.Replaying) return;
        string me = caster.CastPlayer;
        if (HoldMoments.Offered && Keyboard.current != null && Keyboard.current.f7Key.wasPressedThisFrame) PlayFromTheSave(!fromTheSave);
        int? have = world.PointsOf(me);
        // More than was brought is earned here, but only once the world has what was brought: before that it may hold
        // the other side of F7, a dev build's everything, which is never a save's.
        if (have == Points) heard = true;
        else if (have > Points && Saves && heard) file.Save(have.Value);
        else if (Time.unscaledTime - askedAt > AskEvery)
        {
            askedAt = Time.unscaledTime;
            caster.BringPoints(Points);
        }
        Health(world, caster, me);
        WatchRun(world, me);
        Picks(caster, world.RunOf(me), world);
    }

    // Earned points are the save's only when they're real ones, not a dev build's everything.
    bool Saves => !HoldMoments.Offered || fromTheSave;

    // Health upgrades (Tough) raise the wizard's maximum, and the player hears it.
    void Health(World world, SpellCaster caster, string me)
    {
        int extra = world.HealthBonusOf(me);
        if (!caster.TryGetComponent<Vitals>(out var vitals)) return;
        vitals.SetExtraHealth(extra);
        if (extra == extraHealth) return;
        if (extra > extraHealth) EventLog.Add($"{extra - extraHealth} more health: {Mathf.CeilToInt(vitals.HealthMax)} in all");
        extraHealth = extra;
    }
}
