using System.Collections.Generic;
using Chem;
using UnityEngine;

// Picks a spell from rules/spells.json and casts it on the grid (controls
// in PlayerControls). Aiming matches the web sandbox: beams and Gust start
// next to the wizard and point at the target, everything else lands on the
// targeted cell. The cells it will touch are highlighted.
//
// The spell to cast is picked with the spell radial (SpellRadialHud: hold Tab
// or LB, element then spell), or Select(id) from anywhere else (demos, the
// probe). Every spell in rules/spells.json can be picked; nothing to set up.
//
// A cast is an intent, (spell, x, y, direction): solo it is applied to the
// world when the show lands, and its shove to whatever moves (SpellForces);
// online it goes to NetChem, which applies it on every machine at the same
// tick, with where the caster stood (the centre of a burst's shove).
public class SpellCaster : MonoBehaviour
{
    public ChemGrid grid;
    public PlayerControls controls;
    [Tooltip("How each spell looks when cast. Without it, spells take effect with no show.")]
    public SpellVisuals visuals;

    /// <summary>The spell a cast casts.</summary>
    public string SelectedId { get; private set; } = SpellIds.FireBolt;
    /// <summary>Its display name (its id if the rules don't know it).</summary>
    public string SelectedName => grid != null && grid.World != null ? grid.World.Rules.Spell(SelectedId)?.Name ?? SelectedId : SelectedId;

    readonly List<GameObject> markers = new();
    Material markerMat;
    CastPose pose;
    ItemHolder hands;
    SpellRadialHud radial;
    NetChem net;
    RaycastHit crosshair;
    bool hasCrosshair;
    Vector3? blinkLanding;

    /// <summary>
    /// Where a Misty Step cast from the crosshair lands: the exact spot it pointed at (a roof, the top
    /// of a rock), kept on the caster's own machine until the blink runs (MistyStep.Blink).
    /// </summary>
    public Vector3? BlinkPoint { get; set; }

    void Awake()
    {
        TryGetComponent(out pose);
        TryGetComponent(out hands);
        TryGetComponent(out radial);
    }

    void OnEnable() => EventLog.Track(this);

    void Start()
    {
        markerMat = UrpMaterials.NewLit("Aim marker", new Color(1f, 0.85f, 0.3f), emission: new Color(1f, 0.8f, 0.2f) * 1.5f);
    }

    void OnDestroy() => Destroy(markerMat);

    /// <summary>Selects a spell by id; false (and nothing changes) if the rules in force don't have it.</summary>
    public bool Select(string spellId)
    {
        if (string.IsNullOrEmpty(spellId)) return false;
        if (grid != null && grid.World != null && grid.World.Rules.Spell(spellId) == null) return false;
        SelectedId = spellId;
        return true;
    }

    void Update()
    {
        if (controls == null || grid == null || grid.World == null) return;

        hasCrosshair = Aiming.PastSelf(Aiming.Ray(controls), transform, out crosshair);
        // While the spell radial is open the player is choosing, not aiming: no markers, no cast.
        if (!hasCrosshair || (radial != null && radial.IsOpen))
        {
            ShowMarkers(null);
            return;
        }
        var target = CrosshairCell;
        var aim = Aim(target, out var error);
        var spell = grid.World.Rules.Spell(SelectedId);
        ShowMarkers(aim.HasValue && spell != null ? grid.World.SpellCells(spell, aim.Value.x, aim.Value.y, aim.Value.dir) : null);

        if (!controls.Cast.WasPressedThisFrame()) return;
        if (error != null) { EventLog.Add(error); return; }
        if (!aim.HasValue) return;
        // A blink lands on the exact spot it found from the crosshair (a roof, a rock's top), in that spot's square.
        if (spell?.Effect == SpellShapes.TeleportEffect && blinkLanding.HasValue)
        {
            BlinkPoint = blinkLanding;
            var at = grid.CellAt(blinkLanding.Value);
            Cast(SelectedId, at.x, at.y, 1);
            return;
        }
        Cast(SelectedId, aim.Value.x, aim.Value.y, aim.Value.dir);
    }

    Vector2Int CrosshairCell => grid.CellAt(crosshair.point - crosshair.normal * 0.05f);

    // --- casting ---------------------------------------------------------------------

    // The cast is shown at once (gesture, projectile) and takes effect when it
    // lands. Online, the cast goes to the host and lands on every machine at
    // the same tick, timed to roughly when the projectile arrives. The demos
    // cast through this too.
    public void Cast(string spellId, int x, int y, int dir)
    {
        var online = Net;
        if (online != null && online.Online)
        {
            int delay = Mathf.CeilToInt(FlightSeconds(spellId, x, y, dir) * grid.World.Rules.Sim.TicksPerSecond);
            online.RequestCast(spellId, x, y, dir, delay, transform.position);
            return;
        }
        PlayCastEffect(spellId, x, y, dir, () =>
        {
            grid.World.Cast(spellId, x, y, dir);
            SpellForces.Apply(grid, spellId, x, y, dir, transform);
        });
    }

    /// <summary>
    /// Shows a cast: the casting gesture, the projectile if the spell has one, and the effect
    /// where it lands. <paramref name="onArrive"/> runs once, when it lands (at once without a projectile).
    /// </summary>
    public void PlayCastEffect(string spellId, int x, int y, int dir, System.Action onArrive = null)
    {
        var look = LookOf(spellId);
        var landing = Landing(spellId, x, y, dir);
        var def = grid != null && grid.World != null ? grid.World.Rules.Spell(spellId) : null;
        if (pose != null) pose.Play(landing, look?.glow ?? Color.white);
        GameSounds.Play(GameSounds.CastOf(def), Hand);
        // A blink shows its mist where the caster leaves as well as where they land.
        if (def?.Effect == SpellShapes.TeleportEffect && look?.impact != null)
            Destroy(Instantiate(look.impact, transform.position, Quaternion.identity), look.impactSeconds);

        void Land()
        {
            if (look?.impact != null) Destroy(Instantiate(look.impact, landing, Quaternion.LookRotation(Flat(landing - Hand))), look.impactSeconds);
            GameSounds.Play(GameSounds.LandingOf(def), landing);
            onArrive?.Invoke();
        }

        if (look?.projectile != null) SpellProjectile.Launch(look.projectile, Hand, landing, Land, this);
        else Land();
    }

    // Found lazily: a networked wizard gets its grid assigned after Awake.
    NetChem Net
    {
        get
        {
            if (net == null && grid != null) net = grid.GetComponent<NetChem>();
            return net;
        }
    }

    SpellVisuals.Entry LookOf(string spellId) => visuals != null ? visuals.For(spellId) : null;

    Vector3 Hand => pose != null ? pose.HandPosition : transform.position + Vector3.up * 1.3f + transform.forward * 0.4f;

    // Where the spell's show lands: the far end of a beam, the caster for a burst, otherwise the target cell.
    Vector3 Landing(string spellId, int x, int y, int dir)
    {
        var spell = grid.World.Rules.Spell(spellId);
        if (spell != null && spell.Shape == SpellShapes.Line)
        {
            var cells = grid.World.SpellCells(spell, x, y, dir);
            if (cells.Count > 0) (x, y) = cells[cells.Count - 1];
        }
        else if (spell != null && spell.Shape == SpellShapes.Burst) return transform.position + Vector3.up;
        return grid.CellCenter(x, y) + Vector3.up * (grid.SurfaceY(x, y) + 0.3f);
    }

    static Vector3 Flat(Vector3 v)
    {
        v.y = 0f;
        return v.sqrMagnitude > 1e-4f ? v : Vector3.forward;
    }

    float FlightSeconds(string spellId, int x, int y, int dir)
    {
        var look = LookOf(spellId);
        return look?.projectile != null ? Vector3.Distance(Hand, Landing(spellId, x, y, dir)) / SpellProjectile.DefaultSpeed : 0f;
    }

    // --- aiming ------------------------------------------------------------------------

    (int x, int y, int dir)? Aim(Vector2Int target, out string error)
    {
        error = null;
        var spell = grid.World.Rules.Spell(SelectedId);
        if (spell == null) { error = $"{SelectedId} isn't in rules/spells.json"; return null; }
        if (!grid.World.InBounds(target.x, target.y)) return null;
        var me = grid.CellAt(transform.position);
        if (spell.Effect == SpellShapes.TeleportEffect || spell.Effect == SpellShapes.CarryEffect)
        {
            error = spell.Effect == SpellShapes.TeleportEffect
                ? CheckBlink(spell, target)
                : MageHand.Check(grid, spell, hands, me, target.x, target.y);
            return error == null ? (target.x, target.y, 1) : null;
        }
        int? dir = DirFrom(me, target);
        if (spell.Origin != "self") return (target.x, target.y, dir ?? 1);
        if (spell.Shape == SpellShapes.Burst) return (me.x, me.y, dir ?? 1);
        if (dir == null) { error = $"Click away from the wizard to point {spell.Name} that way."; return null; }
        var (dx, dy) = Step(dir.Value);
        return (me.x + dx, me.y + dy, dir.Value);
    }

    // Misty Step checks what the crosshair points at when it's in the target square (a roof, a rock,
    // or its side, which lands on its top), else the top of the square.
    string CheckBlink(SpellDef spell, Vector2Int target)
    {
        blinkLanding = null;
        if (!hasCrosshair || CrosshairCell != target) return MistyStep.Check(grid, spell, transform, target.x, target.y);
        string why = MistyStep.Check(grid, spell, transform, crosshair, out var landing);
        if (why == null) blinkLanding = landing;
        return why;
    }

    // 0 = north (y-1), 1 = east, 2 = south, 3 = west: the sandbox's directions.
    static int? DirFrom(Vector2Int a, Vector2Int b)
    {
        int dx = b.x - a.x, dy = b.y - a.y;
        if (dx == 0 && dy == 0) return null;
        if (Mathf.Abs(dx) >= Mathf.Abs(dy)) return dx > 0 ? 1 : 3;
        return dy > 0 ? 2 : 0;
    }

    static (int, int) Step(int dir) => dir switch { 0 => (0, -1), 1 => (1, 0), 2 => (0, 1), _ => (-1, 0) };

    void ShowMarkers(List<(int x, int y)> cells)
    {
        int n = cells?.Count ?? 0;
        while (markers.Count < n) markers.Add(NewMarker());
        for (int i = 0; i < markers.Count; i++)
        {
            bool on = i < n;
            markers[i].SetActive(on);
            if (!on) continue;
            var (x, y) = cells[i];
            var c = grid.CellCenter(x, y);
            markers[i].transform.position = new Vector3(c.x, grid.SurfaceY(x, y) + 0.06f, c.z);
            markers[i].transform.localScale = new Vector3(0.3f * grid.cellSize, 0.02f, 0.3f * grid.cellSize);
        }
    }

    GameObject NewMarker()
    {
        var m = Primitives.Visual(PrimitiveType.Cube, "Aim");
        var r = m.GetComponent<Renderer>();
        r.sharedMaterial = markerMat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return m;
    }
}
