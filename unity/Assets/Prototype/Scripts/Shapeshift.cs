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
    public const int None = -1;
    const float DefaultSeconds = 60f;
    const float PendingSeconds = 3f;

    public int Species { get; private set; } = None;
    public int Coat { get; private set; }
    public bool Shifted => Species != None;
    public string FormName => Shifted ? NameOf(Species) : null;
    public float SecondsLeft { get; private set; }
    public float FormHp { get; private set; }
    public float FormHpMax { get; private set; }

    ValleyFauna fauna;
    ChemGrid grid;
    PlayerControls controls;
    Vitals vitals;
    ItemHolder hands;
    bool local;
    int pendingSpecies = None, pendingCoat;
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
            if (grid == null) grid = ChemGrid.Current?.Grid;
            return grid;
        }
    }

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

    public bool ChangeBackByChoice() => ChangeBack($"You shake off the {FormName}'s shape", force: false);

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

    float AbsorbDamage(float amount)
    {
        if (!Shifted) return amount;
        FormHp -= amount;
        if (FormHp > 0f) return 0f;
        float rest = -FormHp;
        ChangeBack($"The blow knocks you out of the {FormName}'s shape", force: true);
        return rest;
    }

    (int species, int coat) AnimalAt(int x, int y)
    {
        var f = Fauna;
        if (f == null || Grid == null) return (None, 0);
        var c = Grid.CellCenter(x, y);
        float bestSq = 2.25f * Grid.cellSize * Grid.cellSize;
        int best = -1;
        for (int i = 0; i < f.Count; i++)
        {
            if (f.Body(f.SpeciesOf(i)).Habitat != Punchy.Fauna.Habitat.Land) continue;
            var p = f.State(i).Position;
            float d = (p.x - c.x) * (p.x - c.x) + (p.z - c.z) * (p.z - c.z);
            if (d >= bestSq) continue;
            bestSq = d;
            best = i;
        }
        return best < 0 ? (None, 0) : (f.SpeciesOf(best), f.Coat(best));
    }

    static float HitPoints(CreatureBody body)
    {
        float scale = (body.Size.x + body.Size.y) * 0.5f;
        return Mathf.Clamp(1f + Mathf.Round(60f * body.BodyLength * body.FrontRadius * scale * scale), 1f, 40f);
    }

    public static int Pack(int species, int coat) => species < 0 ? None : species + (coat << 8);

    void SetForm(int form)
    {
        Show(form);
        if (TryGetComponent<NetWizard>(out var net) && net.IsSpawned) net.SetForm(form);
    }

    public void Show(int form)
    {
        requested = form;
        int species = form < 0 ? None : form & 0xff;
        int coat = form < 0 ? 0 : form >> 8;
        if (species != None && (Fauna == null || Fauna.Body(species) == null)) (species, coat) = (None, 0);
        if (species == Species && coat == Coat) return;
        Species = species;
        Coat = coat;
        HideModel(Shifted);
        Puff();
        StartDrawing();
    }

    void Puff()
    {
        var look = TryGetComponent<SpellCaster>(out var caster) ? caster.LookOf(SpellIds.Shapeshift) : null;
        look?.PlayImpact(transform.position + Vector3.up * 0.5f, Quaternion.identity);
    }

    string NameOf(int species)
    {
        var name = Fauna != null ? Fauna.SpeciesName(species) : null;
        return string.IsNullOrEmpty(name) ? "animal" : name.ToLowerInvariant();
    }

    static string Article(string noun) => (noun[0] is 'a' or 'e' or 'i' or 'o' or 'u' ? "an " : "a ") + noun;

    void OnGUI()
    {
        HudScale.Apply();
        if (!local || !Shifted || !EventLog.HudVisible) return;
        hud ??= new GUIStyle(GUI.skin.label) { fontSize = 16, richText = true, alignment = TextAnchor.MiddleCenter };
        string text = $"<b>{FormName}</b>  {Mathf.CeilToInt(SecondsLeft)} s";
        GUI.Label(new Rect(HudScale.Width / 2f - 200f, HudScale.Height - 90f, 400f, 30f), text, hud);
    }
}
