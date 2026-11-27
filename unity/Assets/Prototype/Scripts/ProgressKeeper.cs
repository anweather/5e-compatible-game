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
    int? asIf;
    bool heard;
    float askedAt = float.NegativeInfinity;
    int extraHealth;

    public int Points => asIf ?? (HoldMoments.Offered && !fromTheSave ? World.AllPoints : file.Points);

    public bool FromTheSave => fromTheSave || !HoldMoments.Offered;

    public int Saved => file.Points;

    void Awake()
    {
        grid = GetComponent<ChemGrid>();
        ReadTheSave();
        if (file.Trouble != null) EventLog.Add(file.Trouble);
        AddPage();
    }

    public void ReadTheSave()
    {
        file = new ProgressFile(SaveFolder);
        file.Load();
    }

    public static string SaveFolder => ChemGrid.Folders.PlayerData ?? Application.persistentDataPath;

    void OnDestroy() => RemovePage();

    public void PlayFromTheSave(bool yes)
    {
        if (!HoldMoments.Offered || (fromTheSave == yes && asIf == null)) return;
        fromTheSave = yes;
        asIf = null;
        heard = false;
        EventLog.Add(fromTheSave ? $"Playing with your own unlocks: {file.Points} points" : "Playing with everything unlocked");
        EventLog.TrackedCaster?.BringPoints(Points);
    }

    public void PlayAsIf(int points)
    {
        asIf = points;
        heard = false;
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

    bool Saves => asIf == null && (!HoldMoments.Offered || fromTheSave);

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
