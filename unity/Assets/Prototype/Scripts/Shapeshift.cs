using Chem;
using Punchy.Fauna;
using UnityEngine;

// Shapeshift (rules/spells.json): the wizard takes the shape of one of the
// valley's animals (ValleyFauna) it aims at, our reading of Polymorph cast on
// yourself. While shifted the wizard's own model is hidden and the animal's
// body is drawn in its place, walking and running with the wizard's moves
// (Shapeshift.Draw.cs); it has the animal's speeds, size and jump
// (Shapeshift.Body.cs), casts nothing and holds nothing (SpellCaster,
// ItemHolder), and the animals treat it as one of their own (ValleyFauna).
// Any cast button changes back, whenever you like: an element, the d-pad's
// cast or Cast (the only thing casting does while shifted). The shape
// wears off after the spell's duration (times the charge), and when the
// animal form's hit points run out (Vitals.Absorb: damage the animal takes
// first; what's left carries on to the wizard), with a puff of green mist.
//
// The cast travels like any other (SpellCaster.Cast: a log line, the show);
// it changes no squares. When it lands, the caster's own machine takes the
// shape (SpellForces calls Land), and online NetWizard carries the form (its
// species and coat) to everyone else, where Show draws the animal for that
// wizard, its gait following the synced transform.
public partial class Shapeshift : MonoBehaviour
{
    /// <summary>No animal: the wizard as it is.</summary>
    public const int None = -1;
    const float DefaultSeconds = 60f;
    // How long an aimed animal stays the one a cast takes (online the cast lands a tick or two later).
    const float PendingSeconds = 3f;

    /// <summary>The species (an index in ValleyFauna's list) whose shape the wizard has, or None.</summary>
    public int Species { get; private set; } = None;
    /// <summary>The coat (colouring) of that shape.</summary>
    public int Coat { get; private set; }
    public bool Shifted => Species != None;
    /// <summary>The shape's name ("red fox"), or null when not shifted.</summary>
    public string FormName => Shifted ? NameOf(Species) : null;
    /// <summary>Seconds until the shape wears off (the caster's machine only).</summary>
    public float SecondsLeft { get; private set; }
    /// <summary>The animal form's hit points left, and its most (the caster's machine only).</summary>
    public float FormHp { get; private set; }
    public float FormHpMax { get; private set; }

    ValleyFauna fauna;
    ChemGrid grid;
    PlayerControls controls;
    Vitals vitals;
    ItemHolder hands;
    // This machine's own wizard took the shape (it runs the timer and the body).
    bool local;
    int pendingSpecies = None, pendingCoat;
    // The form asked for (Show), which may wait for the animals to be built.
    int requested = None;
    float pendingAt = -100f;
    GUIStyle hud;

    void Awake()
    {
        TryGetComponent(out controls);
        TryGetComponent(out vitals);
        TryGetComponent(out hands);
        AwakeBody();
    }

    ValleyFauna Fauna
    {
        get
        {
            if (fauna == null) fauna = FindAnyObjectByType<ValleyFauna>();
            return fauna;
        }
    }

    ChemGrid Grid
    {
        get
        {
            if (grid == null) grid = FindAnyObjectByType<ChemGrid>();
            return grid;
        }
    }

    // --- aiming and casting (the caster's machine) -------------------------------------

    /// <summary>
    /// Why the crosshair's <paramref name="ray"/> can't take a shape, or null with the square of
    /// the animal it points at; that animal is the one the cast will take (Land).
    /// </summary>
    public string Aim(SpellDef spell, Ray ray, out Vector2Int cell)
    {
        cell = default;
        var f = Fauna;
        int i = f != null && Grid != null ? f.Aimed(ray, 200f) : -1;
        if (i < 0) return "Aim at an animal to take its shape";
        int range = spell.Range > 0 ? spell.Range : 12;
        cell = Grid.CellAt(f.State(i).Position);
        var me = Grid.CellAt(transform.position);
        if (Mathf.Max(Mathf.Abs(cell.x - me.x), Mathf.Abs(cell.y - me.y)) > range)
            return $"Too far. {spell.Name} reaches {range * 5} ft ({range} squares).";
        pendingSpecies = f.SpeciesOf(i);
        pendingCoat = f.Coat(i);
        pendingAt = Time.time;
        return null;
    }

    /// <summary>
    /// The cast landed (SpellForces, on the caster's machine): takes the shape of the animal it was
    /// aimed at, or else the one standing nearest square (x, y). Returns whether it took a shape.
    /// </summary>
    public bool Land(SpellDef spell, int x, int y, int charge)
    {
        var (species, coat) = Time.time - pendingAt <= PendingSeconds && pendingSpecies != None ? (pendingSpecies, pendingCoat) : AnimalAt(x, y);
        pendingSpecies = None;
        var animal = Fauna != null ? Fauna.Body(species) : null;
        if (animal == null)
        {
            EventLog.Add($"{spell.Name} fizzles: aim at an animal to take its shape");
            return false;
        }
        if (local && Shifted) RestoreBody();
        if (hands != null && hands.Held != null) hands.Drop();
        local = true;
        float seconds = spell.Duration > 0 ? spell.Duration / (float)Grid.World.Rules.Sim.TicksPerSecond : DefaultSeconds;
        SecondsLeft = seconds * World.ClampCharge(charge);
        FormHpMax = FormHp = HitPoints(animal);
        if (vitals != null) vitals.Absorb = AbsorbDamage;
        ShapeBody(animal);
        SetForm(Pack(species, coat));
        EventLog.Add($"You take the shape of {Article(NameOf(species))} for {Mathf.RoundToInt(SecondsLeft)} s. Cast to change back.");
        return true;
    }

    /// <summary>A cast button pressed while shifted (SpellCaster): back to the wizard, if there's room to stand.</summary>
    public bool ChangeBackByChoice() => ChangeBack($"You shake off the {FormName}'s shape", force: false);

    /// <summary>
    /// Back to the wizard's own shape, with <paramref name="line"/> in the log. Unless
    /// <paramref name="force"/>d (the spell ran out, the animal dropped), only where there's room to stand.
    /// </summary>
    public bool ChangeBack(string line, bool force)
    {
        if (!Shifted) return false;
        if (local && !force && !RoomToStand())
        {
            EventLog.Add("There's no room to stand up as yourself here");
            return false;
        }
        if (local)
        {
            RestoreBody();
            if (vitals != null) vitals.Absorb = null;
        }
        local = false;
        SetForm(None);
        EventLog.Add(line);
        return true;
    }

    void Update()
    {
        if (!local || !Shifted) return;
        SecondsLeft -= Time.deltaTime;
        if (SecondsLeft <= 0f) ChangeBack($"The {FormName}'s shape wears off in a puff of mist", force: true);
    }

    // Damage reaches the animal first; at 0 the wizard changes back and the rest carries on (Vitals).
    float AbsorbDamage(float amount)
    {
        if (!Shifted) return amount;
        FormHp -= amount;
        if (FormHp > 0f) return 0f;
        float rest = -FormHp;
        ChangeBack($"The blow knocks you out of the {FormName}'s shape", force: true);
        return rest;
    }

    // The animal nearest the middle of a square, within a square and a half: a cast by square (demos, the probe).
    (int species, int coat) AnimalAt(int x, int y)
    {
        var f = Fauna;
        if (f == null || Grid == null) return (None, 0);
        var c = Grid.CellCenter(x, y);
        float bestSq = 2.25f * Grid.cellSize * Grid.cellSize;
        int best = -1;
        for (int i = 0; i < f.Count; i++)
        {
            var p = f.State(i).Position;
            float d = (p.x - c.x) * (p.x - c.x) + (p.z - c.z) * (p.z - c.z);
            if (d >= bestSq) continue;
            bestSq = d;
            best = i;
        }
        return best < 0 ? (None, 0) : (f.SpeciesOf(best), f.Coat(best));
    }

    // A rough count by bulk, near the SRD's beasts: a rabbit 2, a fox 3, a wolf 9, a deer 15, a bear 23.
    static float HitPoints(CreatureBody body)
    {
        float scale = (body.Size.x + body.Size.y) * 0.5f;
        return Mathf.Clamp(1f + Mathf.Round(60f * body.BodyLength * body.FrontRadius * scale * scale), 1f, 40f);
    }

    // --- the form, on every machine -------------------------------------------------------

    /// <summary>A form as one number for the network: None, or the species with its coat above it.</summary>
    public static int Pack(int species, int coat) => species < 0 ? None : species + (coat << 8);

    // Here, and online on everyone else's screen through NetWizard.
    void SetForm(int form)
    {
        Show(form);
        if (TryGetComponent<NetWizard>(out var net) && net.IsSpawned) net.SetForm(form);
    }

    /// <summary>Shows this wizard in a form (Pack): an animal's body in place of the wizard's, with a puff of mist; None for the wizard.</summary>
    public void Show(int form)
    {
        requested = form;
        int species = form < 0 ? None : form & 0xff;
        int coat = form < 0 ? 0 : form >> 8;
        // Not ready yet (a late joiner, before the animals are built): LateUpdate tries again.
        if (species != None && (Fauna == null || Fauna.Body(species) == null)) (species, coat) = (None, 0);
        if (species == Species && coat == Coat) return;
        Species = species;
        Coat = coat;
        HideModel(Shifted);
        Puff();
        StartDrawing();
    }

    // Green mist where the wizard stands, the spell's own look (SpellVisuals).
    void Puff()
    {
        var look = TryGetComponent<SpellCaster>(out var caster) && caster.visuals != null ? caster.visuals.For(SpellIds.Shapeshift) : null;
        look?.PlayImpact(transform.position + Vector3.up * 0.5f, Quaternion.identity);
    }

    string NameOf(int species)
    {
        var name = Fauna != null ? Fauna.SpeciesName(species) : null;
        if (string.IsNullOrEmpty(name)) return "animal";
        var text = new System.Text.StringBuilder();
        foreach (char ch in name)
        {
            if (char.IsUpper(ch) && text.Length > 0) text.Append(' ');
            text.Append(char.ToLowerInvariant(ch));
        }
        return text.ToString();
    }

    static string Article(string noun) => (noun[0] is 'a' or 'e' or 'i' or 'o' or 'u' ? "an " : "a ") + noun;

    // How long is left, and how to change back, under the crosshair.
    void OnGUI()
    {
        HudScale.Apply();
        if (!local || !Shifted || !EventLog.HudVisible) return;
        hud ??= new GUIStyle(GUI.skin.label) { fontSize = 16, richText = true, alignment = TextAnchor.MiddleCenter };
        string key = controls != null && controls.UsingGamepad ? "Any element button" : "Z X C V or left click";
        string text = $"<b>{FormName}</b>  {Mathf.CeilToInt(SecondsLeft)} s   <color=#ffffffaa>{key}: change back</color>";
        GUI.Label(new Rect(HudScale.Width / 2f - 200f, HudScale.Height - 90f, 400f, 30f), text, hud);
    }
}
