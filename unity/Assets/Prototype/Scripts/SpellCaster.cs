using System.Collections.Generic;
using Chem;
using UnityEngine;

// Picks a spell from rules/spells.json and casts it on the grid (controls
// in PlayerControls). Aiming matches the web sandbox: beams and Gust start
// next to the wizard and point at the target, everything else lands on the
// targeted cell. The cells it will touch are highlighted.
//
// The bar has three pages: Elements (spellIds, keys 1-8), Earth
// (earthSpellIds, keys 1-5) and Arcane (arcaneSpellIds), turned with Tab or
// the d-pad's left/right. The number keys pick on the page shown; Q and E
// step through all the pages as one list.
//
// A cast is an intent, (spell, x, y, direction): solo it is applied to the
// world when the show lands, and its shove to whatever moves (SpellForces);
// online it goes to NetChem, which applies it on every machine at the same
// tick, with where the caster stood (the centre of a burst's shove).
public class SpellCaster : MonoBehaviour
{
    public ChemGrid grid;
    public PlayerControls controls;
    public string[] spellIds = SpellIds.DefaultBar();
    [Tooltip("The bar's second page (Tab, or d-pad left/right): earth shaping.")]
    public string[] earthSpellIds = SpellIds.EarthBar();
    [Tooltip("The bar's third page: magic that moves you or things.")]
    public string[] arcaneSpellIds = SpellIds.ArcaneBar();
    [Tooltip("How each spell looks when cast. Without it, spells take effect with no show.")]
    public SpellVisuals visuals;

    /// <summary>The names of the bar's pages, in page order.</summary>
    public static readonly string[] PageNames = { "Elements", "Earth", "Arcane" };

    /// <summary>The page of the bar shown: 0 Elements, 1 Earth, 2 Arcane.</summary>
    public int Page { get; private set; }
    /// <summary>The selected slot on the page shown.</summary>
    public int Selected { get; private set; }
    public string SelectedName => SpellName(Selected);
    /// <summary>The spells on the page shown, in key order.</summary>
    public string[] Bar => PageSpells(Page);
    public string SelectedId => Bar[Selected];

    /// <summary>The spells on a page, in key order (empty if the page has none).</summary>
    public string[] PageSpells(int page) => page switch
    {
        0 => spellIds,
        1 => earthSpellIds,
        _ => arcaneSpellIds ?? System.Array.Empty<string>(),
    };

    readonly List<GameObject> markers = new();
    Material markerMat;
    CastPose pose;
    ItemHolder hands;
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
    }

    void OnEnable() => EventLog.Track(this);

    void Start()
    {
        markerMat = UrpMaterials.NewLit("Aim marker", new Color(1f, 0.85f, 0.3f), emission: new Color(1f, 0.8f, 0.2f) * 1.5f);
    }

    void OnDestroy() => Destroy(markerMat);

    /// <summary>The display name of the spell in a bar slot (its id if the rules don't know it).</summary>
    public string SpellName(int slot)
    {
        var world = grid != null ? grid.World : null;
        return world?.Rules.Spell(Bar[slot])?.Name ?? Bar[slot];
    }

    /// <summary>Selects a spell by id on whichever page has it; false if neither does.</summary>
    public bool Select(string spellId)
    {
        for (int page = 0; page < PageNames.Length; page++)
        {
            int slot = System.Array.IndexOf(PageSpells(page), spellId);
            if (slot < 0) continue;
            Page = page;
            Selected = slot;
            return true;
        }
        return false;
    }

    void Update()
    {
        if (controls == null || grid == null || grid.World == null) return;
        PickSpell();

        hasCrosshair = Aiming.PastSelf(Aiming.Ray(controls), transform, out crosshair);
        if (!hasCrosshair)
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

    void PickSpell()
    {
        if (controls.SwitchPage.WasPressedThisFrame()) NextPage();
        int slot = controls.PickedSlot();
        if (slot >= 0 && slot < Bar.Length) Selected = slot;
        if (controls.NextSpellPressed()) StepSpell(1);
        if (controls.PrevSpell.WasPressedThisFrame()) StepSpell(-1);
    }

    /// <summary>Turns to the next page that has spells (Tab), keeping the slot where it can.</summary>
    public void NextPage()
    {
        for (int i = 1; i <= PageNames.Length; i++)
        {
            int page = (Page + i) % PageNames.Length;
            if (PageSpells(page).Length == 0) continue;
            Page = page;
            Selected = Mathf.Min(Selected, Bar.Length - 1);
            return;
        }
    }

    // Steps through all the pages as one list, turning the page at either end.
    void StepSpell(int by)
    {
        int total = 0, at = 0;
        for (int p = 0; p < PageNames.Length; p++)
        {
            if (p == Page) at = total + Selected;
            total += PageSpells(p).Length;
        }
        at = (at + by + total) % total;
        for (int p = 0; p < PageNames.Length; p++)
        {
            int n = PageSpells(p).Length;
            if (at < n)
            {
                Page = p;
                Selected = at;
                return;
            }
            at -= n;
        }
    }

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
