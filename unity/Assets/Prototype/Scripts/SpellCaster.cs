using System.Collections;
using System.Collections.Generic;
using Chem;
using UnityEngine;

// Picks a spell from rules/spells.json and casts it on the grid (controls
// in PlayerControls). Aiming matches the web sandbox: beams and Gust start
// next to the wizard and point at the target, everything else lands on the
// targeted cell. The cells it will touch are highlighted. Shapeshift aims at
// an animal instead; in an animal's shape, Cast only changes back.
//
// The spell to cast is picked with the spell radial (SpellRadialHud: hold Tab
// or LB, element then spell), or Select(id) from anywhere else (demos, the
// probe). Every spell in rules/spells.json can be picked; nothing to set up.
//
// A cast is an intent, (spell, x, y, direction): solo it is applied to the
// world when the show lands, and its shove to whatever moves (SpellForces);
// online it goes to NetChem, which applies it on every machine at the same
// tick, with where the caster stood (the centre of a burst's shove).
public partial class SpellCaster : MonoBehaviour
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
    Shapeshift shape;
    NetChem net;
    RaycastHit crosshair;
    bool hasCrosshair;
    // Holding Cast charges a spell that can be (ElementHold.SecondsPerLevel a level, to World.MaxCharge); release casts it.
    // The element that eats things away (Acid Splash): its splash, hiss and eaten stain.
    const string AcidElement = "acid", HeatElement = "heat", SandElement = "sand", LightningElement = "lightning";
    float chargeStart = -1f;
    GUIStyle pips;

    /// <summary>The charge the held Cast has built so far (1 when not charging).</summary>
    public int Charge => chargeStart < 0f ? 1 : ElementHold.ChargeOf(Time.time - chargeStart, World.MaxCharge);
    /// <summary>How long Cast has been held charging, or -1 when it isn't (CastPose holds the wind-up).</summary>
    public float ChargingSeconds => chargeStart < 0f ? -1f : Time.time - chargeStart;
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
        TryGetComponent(out shape);
    }

    void OnEnable() => EventLog.Track(this);

    void Start()
    {
        markerMat = UrpMaterials.NewLit("Aim marker", new Color(1f, 0.85f, 0.3f), emission: new Color(1f, 0.8f, 0.2f) * 1.5f);
    }

    void OnDestroy()
    {
        Destroy(markerMat);
        orbs?.Destroy();
    }

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
        // In an animal's shape, Cast only changes back (WhileShifted).
        if (controls == null || grid == null || grid.World == null) return;
        UpdateElements();
        if (WhileShifted()) return;

        hasCrosshair = Aiming.PastSelf(Aiming.Ray(controls), transform, out crosshair);
        // While the spell radial is open the player is choosing, not aiming: no markers, no cast.
        if (!hasCrosshair || (radial != null && radial.IsOpen))
        {
            ShowMarkers(null);
            chargeStart = -1f;
            return;
        }
        var target = CrosshairCell;
        var aim = Aim(SelectedId, target, out var error);
        var spell = grid.World.Rules.Spell(SelectedId);
        ShowMarkers(aim.HasValue && spell != null ? grid.World.SpellCells(spell, aim.Value.x, aim.Value.y, aim.Value.dir, Charge, aim.Value.aimX, aim.Value.aimY) : null);

        if (!CastNow(spell)) return;
        int charge = Charge;
        chargeStart = -1f;
        if (error != null) { EventLog.Add(error); return; }
        if (!aim.HasValue) return;
        // A blink lands on the exact spot it found from the crosshair (a roof, a rock's top), in that spot's square.
        if (spell?.Effect == SpellShapes.TeleportEffect && blinkLanding.HasValue)
        {
            BlinkPoint = blinkLanding;
            var at = grid.CellAt(blinkLanding.Value);
            Cast(SelectedId, at.x, at.y, 1, charge);
            return;
        }
        Cast(new CastCommand(SelectedId, aim.Value.x, aim.Value.y, aim.Value.dir, charge, aim.Value.aimX, aim.Value.aimY));
    }

    // Whether Cast lets the spell go this frame: a spell that charges starts charging on the press
    // and goes on the release; the rest go on the press.
    bool CastNow(SpellDef spell)
    {
        bool charges = Chargeable(spell);
        if (charges && controls.Cast.WasPressedThisFrame())
        {
            chargeStart = Time.time;
            return false;
        }
        return charges ? controls.Cast.WasReleasedThisFrame() : controls.Cast.WasPressedThisFrame();
    }

    // In an animal's shape (Shapeshift) the only cast is changing back. True while shifted.
    bool WhileShifted()
    {
        if (shape == null || !shape.Shifted) return false;
        ShowMarkers(null);
        chargeStart = -1f;
        if (controls.Cast.WasPressedThisFrame() && (radial == null || !radial.IsOpen)) shape.ChangeBackByChoice();
        return true;
    }

    /// <summary>Whether holding Cast makes this spell stronger (World.Chargeable).</summary>
    public static bool Chargeable(SpellDef spell) => World.Chargeable(spell);

    // Three pips under the crosshair fill as the held Cast charges.
    void OnGUI()
    {
        if (chargeStart < 0f || !EventLog.HudVisible) return;
        HudScale.Apply();
        pips ??= new GUIStyle(GUI.skin.label) { fontSize = 22, richText = true, alignment = TextAnchor.MiddleCenter };
        int charge = Charge;
        var text = new System.Text.StringBuilder();
        for (int i = 1; i <= World.MaxCharge; i++) text.Append(i <= charge ? "<color=#ffd24a>●</color> " : "<color=#ffffff55>●</color> ");
        GUI.Label(new Rect(HudScale.Width / 2f - 60f, HudScale.Height / 2f + 18f, 120f, 30f), text.ToString(), pips);
    }

    Vector2Int CrosshairCell => grid.CellAt(crosshair.point - crosshair.normal * 0.05f);

    // --- casting ---------------------------------------------------------------------

    // The cast is shown at once (gesture, projectile) and takes effect when it
    // lands. Online, the cast goes to the host and lands on every machine at
    // the same tick, timed to roughly when the projectile arrives. The demos
    // cast through this too.
    public void Cast(string spellId, int x, int y, int dir, int charge = 1) => Cast(new CastCommand(spellId, x, y, dir, charge));

    /// <summary>A cast, perhaps aimed off the grid's four ways (CastCommand.AimX, AimY).</summary>
    public void Cast(CastCommand cast)
    {
        if (shape != null && shape.Shifted)
        {
            EventLog.Add($"You can't cast spells in the {shape.FormName}'s shape. Cast to change back first.");
            return;
        }
        var online = Net;
        if (online != null && online.Online)
        {
            // Just joined and still replaying the game so far: the world on this screen is behind
            // the host's, so aiming at it would cast at what isn't there any more. A moment's wait.
            if (!online.CaughtUp)
            {
                EventLog.Add("Still catching up with the game: cast again in a moment", "Still catching up");
                return;
            }
            int delay = Mathf.CeilToInt(FlightSeconds(cast) * grid.World.Rules.Sim.TicksPerSecond);
            online.RequestCast(cast, delay, transform.position);
            return;
        }
        PlayCastEffect(cast, () =>
        {
            grid.World.Cast(cast.Spell, cast.X, cast.Y, cast.Dir, cast.Charge, cast.AimX, cast.AimY);
            SpellForces.Apply(grid, cast, transform);
        });
    }

    /// <summary>
    /// Shows a cast: the casting gesture, the projectile if the spell has one, and the effect
    /// where it lands. <paramref name="onArrive"/> runs once, when it lands (at once without a projectile).
    /// </summary>
    public void PlayCastEffect(string spellId, int x, int y, int dir, System.Action onArrive = null) =>
        PlayCastEffect(new CastCommand(spellId, x, y, dir), onArrive);

    public void PlayCastEffect(CastCommand cast, System.Action onArrive = null)
    {
        string spellId = cast.Spell;
        var look = LookOf(spellId);
        var landing = Landing(cast);
        var def = grid != null && grid.World != null ? grid.World.Rules.Spell(spellId) : null;
        var gesture = CastGestures.For(def, look);
        if (pose != null) pose.Play(gesture, landing, look?.glow ?? Color.white);
        GameSounds.Play(GameSounds.CastOf(def), Hand);
        // Acid: what it can eat is noted now, before it lands, so the cells it eats get the eaten stain.
        if (def?.Element == AcidElement && grid != null) grid.WatchAcid(cast);
        // A blink shows its mist where the caster leaves as well as where they land.
        if (def?.Effect == SpellShapes.TeleportEffect && look?.impact != null)
            Destroy(SpellVisuals.Spawn(look.impact, transform.position, Quaternion.identity), look.impactSeconds);
        // The spell leaves the hands at the gesture's release (the throw, the pull down); FlightSeconds counts it in.
        float release = CastGestures.Of(gesture).release;
        if (release > 0f) StartCoroutine(AtRelease(release, () => Unleash(def, look, landing, cast.Charge, onArrive)));
        else Unleash(def, look, landing, cast.Charge, onArrive);
    }

    static IEnumerator AtRelease(float seconds, System.Action then)
    {
        yield return new WaitForSeconds(seconds);
        then();
    }

    // The elements with a burst of their own where they land (BurstFx); sand blasts from the hand.
    void ElementBurst(SpellDef def, Vector3 landing, int charge)
    {
        float size = 1f + 0.5f * (World.ClampCharge(charge) - 1);
        switch (def?.Element)
        {
            case AcidElement:
                BurstFx.Play(BurstFx.Kind.AcidSplash, landing + Vector3.up * 0.2f, size);
                break;
            case HeatElement when def.Needs != null:
                Jet(landing, size);
                break;
            case HeatElement:
                BurstFx.Play(BurstFx.Kind.Forge, landing + Vector3.up * 0.1f, size);
                break;
            case SandElement:
                BurstFx.Play(BurstFx.Kind.SandBlast, Hand, size, Quaternion.LookRotation(landing - Hand));
                break;
        }
        if (def?.Scatter != null) BurstFx.Play(BurstFx.Kind.Debris, landing, size);
    }

    // A Geyser's jet out of the water; on dry ground, only a little puff.
    void Jet(Vector3 landing, float size)
    {
        var cell = grid.CellAt(landing);
        var c = grid.World.At(cell.x, cell.y);
        if (c != null && c.Water > 0 && !c.Frozen) BurstFx.Play(BurstFx.Kind.Geyser, landing, size, Quaternion.Euler(-90f, 0f, 0f));
        else BurstFx.Play(BurstFx.Kind.Chips, landing, 0.6f);
    }

    // A bolt from the hand to where it strikes, and the charge runs on from there (ChemGrid.Charge).
    void Bolt(Vector3 landing)
    {
        ArcFx.Play(Hand, landing, 0.16f, 0f, 0.45f);
        ArcFx.Play(Hand, landing, 0.07f, 0.04f, 0.4f);
        grid.ChargeStartsAt(landing);
    }

    // The projectile, meteor or storm, or the effect at once; onArrive when it lands.
    void Unleash(SpellDef def, SpellVisuals.Entry look, Vector3 landing, int charge, System.Action onArrive)
    {
        void Land()
        {
            look?.PlayImpact(landing, Quaternion.LookRotation(Flat(landing - Hand)));
            ElementBurst(def, landing, charge);
            GameSounds.Play(GameSounds.LandingOf(def), landing);
            onArrive?.Invoke();
        }

        if (def?.Element == LightningElement && def.Shape == SpellShapes.Line) Bolt(landing);
        if (look == null) Land();
        else if (look.fromSky)
            MeteorFall.Launch(landing, transform.position, look, () =>
            {
                GameSounds.Play(GameSounds.LandingOf(def), landing);
                onArrive?.Invoke();
            }, this);
        else Travel(look, def, charge, landing, Land);
    }

    // How a show that doesn't fall from the sky gets where it lands: a storm, a beam, a projectile, or at once.
    void Travel(SpellVisuals.Entry look, SpellDef def, int charge, Vector3 landing, System.Action land)
    {
        if (look.storm) Storm(def, charge, landing, land);
        else if (look.beam) FrostBeam.Launch(Hand, landing + Vector3.up * BeamLift, look.glow, land, this);
        else if (look.projectile != null) SpellProjectile.Launch(look.projectile, Hand, landing, land, this);
        else land();
    }

    // A storm over the spell's squares (a burst's radius each way of the target), for the spell's
    // duration if it has one, else a few seconds; it lands when the first of it reaches the ground.
    void Storm(SpellDef def, int charge, Vector3 landing, System.Action land)
    {
        int radius = def != null ? def.Radius + World.ChargedGrowth(def, charge) : 0;
        float halfWidth = (radius + 0.5f) * grid.cellSize;
        float seconds = def != null && def.Duration > 0 ? def.Duration / (float)grid.World.Rules.Sim.TicksPerSecond : SleetStorm.Seconds;
        SleetStorm.Launch(landing, halfWidth, seconds, transform.position, land, this);
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

    // A beam ends this far over where a line's show lands, about knee high, so it runs out low over the ground.
    const float BeamLift = 0.4f;

    /// <summary>The gesture a spell is cast with (CastGestures.For: its look's, or one from its rules).</summary>
    public CastGesture GestureOf(string spellId) =>
        CastGestures.For(grid != null && grid.World != null ? grid.World.Rules.Spell(spellId) : null, LookOf(spellId));

    /// <summary>The colour of the glow in the caster's hand for a spell.</summary>
    public Color GlowOf(string spellId) => LookOf(spellId)?.glow ?? Color.white;

    Vector3 Hand => pose != null ? pose.HandPosition : transform.position + Vector3.up * 1.3f + transform.forward * 0.4f;

    // Where the spell's show lands: the far end of a beam, the caster for a burst, otherwise the target cell.
    Vector3 Landing(CastCommand cast)
    {
        var spell = grid.World.Rules.Spell(cast.Spell);
        var (x, y) = (cast.X, cast.Y);
        if (spell != null && spell.Shape == SpellShapes.Line)
        {
            var cells = grid.World.SpellCells(spell, x, y, cast.Dir, cast.Charge, cast.AimX, cast.AimY);
            if (cells.Count > 0) (x, y) = cells[cells.Count - 1];
            if (grid.World.Sought(spell, cast.X, cast.Y) is (int, int) sought) (x, y) = sought;
        }
        else if (spell != null && spell.Shape == SpellShapes.Burst && spell.Origin == "self") return transform.position + Vector3.up;
        return grid.CellCenter(x, y) + Vector3.up * (grid.SurfaceY(x, y) + 0.3f);
    }

    static Vector3 Flat(Vector3 v)
    {
        v.y = 0f;
        return v.sqrMagnitude > 1e-4f ? v : Vector3.forward;
    }

    float FlightSeconds(CastCommand cast)
    {
        var look = LookOf(cast.Spell);
        // The gesture's wind-up comes first: the spell leaves the hands at its release (PlayCastEffect).
        float release = CastGestures.Of(GestureOf(cast.Spell)).release;
        if (look != null && look.fromSky) return release + MeteorFall.Seconds;
        if (look != null && look.storm) return release + SleetStorm.LandSeconds;
        if (look != null && look.beam) return release + FrostBeam.ReachSeconds;
        return release + (look?.projectile != null ? Vector3.Distance(Hand, Landing(cast)) / SpellProjectile.DefaultSpeed : 0f);
    }

    // --- aiming ------------------------------------------------------------------------

    (int x, int y, int dir, int aimX, int aimY)? Aim(string spellId, Vector2Int target, out string error)
    {
        error = null;
        var spell = grid.World.Rules.Spell(spellId);
        if (spell == null) { error = $"{spellId} isn't in rules/spells.json"; return null; }
        if (spell.Effect == SpellShapes.ShapeshiftEffect) return AimShift(spell, out error);
        if (!grid.World.InBounds(target.x, target.y)) return null;
        var me = grid.CellAt(transform.position);
        if (spell.Effect == SpellShapes.TeleportEffect || spell.Effect == SpellShapes.CarryEffect)
            return AimMove(spell, me, target, out error);
        // A line or a cone points where the crosshair is, off the grid's four ways if it is; it
        // starts beside the caster on the nearest of the four.
        var (aimX, aimY) = AimOf(spell);
        int? dir = aimX != 0 || aimY != 0 ? World.DirOf(aimX, aimY) : DirFrom(me, target);
        if (spell.Origin != "self") return (target.x, target.y, dir ?? 1, aimX, aimY);
        if (spell.Shape == SpellShapes.Burst) return (me.x, me.y, dir ?? 1, 0, 0);
        if (dir == null) { error = $"Click away from the wizard to point {spell.Name} that way."; return null; }
        var (dx, dy) = Step(dir.Value);
        return (me.x + dx, me.y + dy, dir.Value, aimX, aimY);
    }

    // Misty Step and Mage Hand land on one square: a blink may land short of the crosshair (Reach), so
    // it marks where it will.
    (int x, int y, int dir, int aimX, int aimY)? AimMove(SpellDef spell, Vector2Int me, Vector2Int target, out string error)
    {
        bool blink = spell.Effect == SpellShapes.TeleportEffect;
        error = blink ? CheckBlink(spell, target) : MageHand.Check(grid, spell, hands, me, target.x, target.y, Charge);
        if (error != null) return null;
        var lands = blink && blinkLanding.HasValue ? grid.CellAt(blinkLanding.Value) : target;
        return (lands.x, lands.y, 1, 0, 0);
    }

    // Shapeshift lands on the animal the crosshair is on (Shapeshift.Aim), marking its square.
    (int x, int y, int dir, int aimX, int aimY)? AimShift(SpellDef spell, out string error)
    {
        if (shape == null) { error = $"{spell.Name} needs a body to change"; return null; }
        error = shape.Aim(spell, Aiming.Ray(controls), out var cell);
        if (error != null || !grid.World.InBounds(cell.x, cell.y)) return null;
        return (cell.x, cell.y, 1, 0, 0);
    }

    /// <summary>Fine steps per square of an aim's vector: the most slant a line can take is one square in this many.</summary>
    const int AimSteps = 16;

    // From the caster to the crosshair, in sixteenths of a square on the grid (x east, y south),
    // snapped to the grid's four ways near them (World.SnapAim); (0, 0) for other shapes.
    (int, int) AimOf(SpellDef spell)
    {
        if (spell.Shape != SpellShapes.Line && spell.Shape != SpellShapes.Cone) return (0, 0);
        var d = crosshair.point - transform.position;
        return World.SnapAim(Mathf.RoundToInt(d.x / grid.cellSize * AimSteps), Mathf.RoundToInt(-d.z / grid.cellSize * AimSteps));
    }

    // Misty Step checks what the crosshair points at when it's in the target square (a roof, a rock,
    // or its side, which lands on its top), or the farthest spot it can reach towards it, else the
    // top of the square. Held, it reaches further (MistyStep.Range).
    string CheckBlink(SpellDef spell, Vector2Int target)
    {
        blinkLanding = null;
        if (!hasCrosshair || CrosshairCell != target) return MistyStep.Check(grid, spell, transform, target.x, target.y, Charge);
        string why = MistyStep.Reach(grid, spell, transform, crosshair, out var landing, Charge);
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
