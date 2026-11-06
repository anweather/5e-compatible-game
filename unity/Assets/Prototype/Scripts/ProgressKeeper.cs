using Chem;
using UnityEngine;
using UnityEngine.InputSystem;

// The player's progress in the Hold (docs/design/the-hold.md, phase 5; J-038): their points live in their own save, not in any world.
// This reads the save, tells every peer what it says by a command whenever the world doesn't know (a new world, a reset, joining), and
// writes what the world has earned. It also shows the offer between waves, plainly: 1, 2 and 3 pick. Dev builds and the Editor start
// with everything, as a playtest needs; F7 there plays as the save says.
[RequireComponent(typeof(ChemGrid))]
public class ProgressKeeper : MonoBehaviour
{
    const float AskEvery = 1f, Width = 420f, RowHeight = 30f, Top = 130f;

    ChemGrid grid;
    ProgressFile file;
    bool fromTheSave;
    float askedAt = float.NegativeInfinity;
    GUIStyle style;

    /// <summary>The points this player should have here: everything in a dev build, unless asked for the save's.</summary>
    public int Points => HoldMoments.Offered && !fromTheSave ? World.AllPoints : file.Points;

    void Awake()
    {
        grid = GetComponent<ChemGrid>();
        file = new ProgressFile(ChemGrid.Folders.PlayerData ?? Application.persistentDataPath);
        file.Load();
        if (file.Trouble != null) EventLog.Add(file.Trouble);
    }

    void Update()
    {
        var world = grid.World;
        var caster = EventLog.TrackedCaster;
        if (world?.HoldSite == null || caster == null || caster.Replaying) return;
        string me = caster.CastPlayer;
        if (HoldMoments.Offered && Keyboard.current != null && Keyboard.current.f7Key.wasPressedThisFrame)
        {
            fromTheSave = !fromTheSave;
            EventLog.Add(fromTheSave ? "Playing with your own unlocks" : "Playing with everything unlocked");
            caster.BringPoints(Points);
        }
        int? have = world.PointsOf(me);
        if (have == Points || (have > Points && !Saves)) { }
        else if (have > Points) file.Save(have.Value);
        else if (Time.unscaledTime - askedAt > AskEvery)
        {
            askedAt = Time.unscaledTime;
            caster.BringPoints(Points);
        }
        if (caster.TryGetComponent<Vitals>(out var vitals)) vitals.SetExtraHealth(world.HealthBonusOf(me));
        Picks(caster, world.RunOf(me));
    }

    // Earned points are the save's only when they're real ones, not a dev build's everything.
    bool Saves => !HoldMoments.Offered || fromTheSave;

    void Picks(SpellCaster caster, RunWizard run)
    {
        var keys = Keyboard.current;
        if (run == null || run.Offer.Count == 0 || keys == null) return;
        var numbers = new[] { keys.digit1Key, keys.digit2Key, keys.digit3Key };
        for (int i = 0; i < numbers.Length && i < run.Offer.Count; i++)
            if (numbers[i].wasPressedThisFrame) caster.PickUpgrade(run.Offer[i]);
    }

    void OnGUI()
    {
        var world = grid.World;
        var caster = EventLog.TrackedCaster;
        var run = caster != null ? world?.RunOf(caster.CastPlayer) : null;
        if (run == null || run.Offer.Count == 0 || !EventLog.HudVisible) return;
        HudScale.Apply();
        style ??= new GUIStyle(GUI.skin.button) { fontSize = 18 };
        for (int i = 0; i < run.Offer.Count; i++)
        {
            var rect = new Rect(HudScale.Width / 2f - Width / 2f, Top + i * RowHeight, Width, RowHeight - 2f);
            if (GUI.Button(rect, $"{i + 1}. {world.Rules.Sim.Hold.Upgrade(run.Offer[i])?.Name}", style)) caster.PickUpgrade(run.Offer[i]);
        }
    }
}
