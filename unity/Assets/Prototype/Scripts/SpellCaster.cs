using System.Collections;
using System.Collections.Generic;
using Chem;
using UnityEngine;

// Picks a spell from rules/spells.json and casts it on the grid (controls
// in PlayerControls). Aiming matches the web sandbox: beams and Gust start
// next to the wizard and point at the target, everything else lands on the
// targeted cell. A marker on the ground shows where (AimMarker). Shapeshift aims at
// an animal instead; in an animal's shape, Cast only changes back.
//
// The hands cast what they hold (SpellCaster.Hands.cs). In the debug layout (F2) Cast
// casts the spell picked in the spell list (SpellListMenu: Tab) or with the spell radial
// (SpellRadialHud: hold LB on a pad, element then spell), or Select(id) from anywhere else
// (demos, the probe). Every spell in rules/spells.json can be picked; nothing to set up.
//
// A cast is an intent, (spell, x, y, direction): solo it is applied to the
// world when the show lands, and its shove to whatever moves (SpellForces);
// online it goes to NetChem, which applies it on every machine at the same
// tick, with where the caster stood (the centre of a burst's shove).
public partial class SpellCaster : Caster
{
    static readonly Registry<SpellCaster> registry = new();

    public static new SpellCaster[] All => registry.All();

    public static new SpellCaster[] AllByInstanceId => registry.AllByInstanceId();

    public static new SpellCaster InPlay => registry.First;

    public ChemGrid grid;
    public PlayerControls controls;
    [Tooltip("How each spell looks when cast. Without it, spells take effect with no show.")]
    public SpellVisuals visuals;

    public string SelectedId { get; private set; } = SpellIds.FireBolt;
    public string SelectedName => grid != null && grid.World != null ? grid.World.Rules.Spell(SelectedId)?.Name ?? SelectedId : SelectedId;

    AimMarker marker;
    CastPose pose;
    CastBeats beats;
    ItemHolder hands;
    SpellRadialHud radial;
    Shapeshift shape;
    RaycastHit crosshair;
    bool hasCrosshair;
    const string AcidElement = "acid", HeatElement = "heat", SandElement = "sand", LightningElement = "lightning";
    float chargeStart = -1f;
    GUIStyle pips;

    public int Charge => chargeStart < 0f ? 1 : ElementHold.ChargeOf(Time.time - chargeStart, World.MaxCharge, grid.World.SecondsPerLevel);
    public float ChargingSeconds => chargeStart < 0f ? -1f : Time.time - chargeStart;
    Vector3? blinkLanding;

    public Vector3? BlinkPoint { get; set; }

    void Awake()
    {
        registry.Join(this);
        JoinCasters();
        TryGetComponent(out pose);
        TryGetComponent(out beats);
        TryGetComponent(out hands);
        TryGetComponent(out radial);
        TryGetComponent(out shape);
    }

    void OnEnable() => EventLog.Track(this);

    void OnDestroy()
    {
        registry.Leave(this);
        LeaveCasters();
        marker?.Destroy();
    }

    public Vector2Int? LookingAt => hasCrosshair && !skyAim && grid != null ? CrosshairCell : null;

    public int CastsStarted { get; private set; }

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
        if (visuals != null) visuals.ReadyShows(grid.World.Rules, grid.CurrentRules?.Base);
        ShowPool.ReadyNext();
        UpdateElements();
        if (WhileShifted()) return;

        hasCrosshair = FindCrosshair();
        if (!hasCrosshair || (radial != null && radial.IsOpen))
        {
            marker?.Hide();
            chargeStart = -1f;
            return;
        }
        var spell = grid.World.Rules.Spell(SelectedId);
        LockOnto(AimingFor(spell));
        var target = CrosshairCell;
        var aim = Aim(SelectedId, target, out var error);
        ShowAim(target, spell, aim);

        if (!CastNow(spell)) return;
        int charge = Charge;
        chargeStart = -1f;
        if (error != null) { EventLog.Add(error); return; }
        if (!aim.HasValue) return;
        if (spell?.Effect == SpellShapes.TeleportEffect && blinkLanding.HasValue)
        {
            BlinkPoint = blinkLanding;
            var at = grid.CellAt(blinkLanding.Value);
            Cast(SelectedId, at.x, at.y, 1, charge);
            return;
        }
        StrikeAtLocked();
        Cast(new CastCommand(SelectedId, aim.Value.x, aim.Value.y, aim.Value.dir, charge, aim.Value.aimX, aim.Value.aimY));
    }

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

    bool WhileShifted()
    {
        if (shape == null || !shape.Shifted) return false;
        marker?.Hide();
        chargeStart = -1f;
        if (controls.AnyCastPressed() && (radial == null || !radial.IsOpen)) shape.ChangeBackByChoice();
        return true;
    }

    public static bool Chargeable(SpellDef spell) => World.Chargeable(spell);

    void OnGUI()
    {
        DrawLock();
        if (chargeStart < 0f || !EventLog.HudVisible) return;
        HudScale.Apply();
        pips ??= new GUIStyle(GUI.skin.label) { fontSize = 22, richText = true, alignment = TextAnchor.MiddleCenter };
        int charge = Charge;
        var text = new System.Text.StringBuilder();
        for (int i = 1; i <= World.MaxCharge; i++) text.Append(i <= charge ? "<color=#ffd24a>●</color> " : "<color=#ffffff55>●</color> ");
        GUI.Label(new Rect(HudScale.Width / 2f - 60f, HudScale.Height / 2f + 18f, 120f, 30f), text.ToString(), pips);
    }

    bool FindCrosshair()
    {
        skyAim = false;
        hasAimRay = false;
        if (AimOverride.HasValue)
        {
            crosshair = default;
            crosshair.point = AimOverride.Value;
            crosshair.normal = Vector3.up;
            return true;
        }
        return RayCrosshair();
    }

    Vector2Int CrosshairCell => grid.CellAt(crosshair.point - crosshair.normal * 0.05f);

    public void Cast(string spellId, int x, int y, int dir, int charge = 1) => Cast(new CastCommand(spellId, x, y, dir, charge));

    public void Cast(CastCommand cast)
    {
        CastsStarted++;
        if (shape != null && shape.Shifted)
        {
            EventLog.Add($"You can't cast spells in the {shape.FormName}'s shape. Cast to change back first.");
            return;
        }
        var online = Peers.Of(grid);
        if (online.Online())
        {
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

    public void PlayCastEffect(string spellId, int x, int y, int dir, System.Action onArrive = null) =>
        PlayCastEffect(new CastCommand(spellId, x, y, dir), onArrive);

    public void PlayCastEffect(CastCommand cast, System.Action onArrive = null)
    {
        string spellId = cast.Spell;
        var look = LookOf(spellId);
        var landing = Landing(cast);
        var def = grid != null && grid.World != null ? grid.World.Rules.Spell(spellId) : null;
        var gesture = CastGestures.For(def, look);
        var strike = strikeAt;
        strikeAt = null;
        if (pose != null) pose.Play(gesture, landing, look?.glow ?? Color.white);
        if (beats == null || !beats.InHands(spellId)) GameSounds.Play(GameSounds.CastOf(def), Hand);
        if (def?.Element == AcidElement && grid != null) grid.WatchAcid(cast);
        if (def?.Effect == SpellShapes.TeleportEffect && look?.impact != null)
            look.PlayImpact(transform.position, Quaternion.identity);
        float release = CastGestures.Of(gesture).release;
        if (beats != null) beats.Announce(spellId, landing, release);
        if (release > 0f) StartCoroutine(AtRelease(release, () => Unleash(def, look, landing, cast, onArrive, strike)));
        else Unleash(def, look, landing, cast, onArrive, strike);
    }

    static IEnumerator AtRelease(float seconds, System.Action then)
    {
        yield return new WaitForSeconds(seconds);
        then();
    }

    void ElementBurst(SpellDef def, SpellVisuals.Entry look, Vector3 landing, int charge)
    {
        float size = SpellVisuals.Entry.BurstSize(look, def, charge);
        Color? color = look?.glow;
        switch (def?.Element)
        {
            case AcidElement:
                BurstFx.Play(BurstFx.Kind.AcidSplash, landing + Vector3.up * 0.2f, size, look: color);
                break;
            case HeatElement when def.Needs != null:
                Jet(landing, size, color);
                break;
            case HeatElement:
                BurstFx.Play(BurstFx.Kind.Forge, landing + Vector3.up * 0.1f, size, look: color);
                break;
            case SandElement:
                BurstFx.Play(BurstFx.Kind.SandBlast, Hand, size, Quaternion.LookRotation(landing - Hand), look: color);
                break;
        }
        if (def?.Scatter != null) BurstFx.Play(BurstFx.Kind.Debris, landing, size, look: color);
    }

    void Spires(SpellDef def, CastCommand cast)
    {
        var cells = grid.World.SpellCells(def, cast.X, cast.Y, cast.Dir, cast.Charge, cast.AimX, cast.AimY);
        float when = Time.time;
        foreach (var (x, y) in cells)
        {
            grid.SpiresRiseAt(x, y, when);
            var raider = grid.World.RaiderAt(x, y);
            if (raider != null && raider.Alive) grid.SpireUnder(raider, x, y, when);
            when += SpireEvery;
        }
    }

    const float SpireEvery = 0.06f;

    void Jet(Vector3 landing, float size, Color? look)
    {
        var cell = grid.CellAt(landing);
        var c = grid.World.At(cell.x, cell.y);
        if (c != null && c.Water > 0 && !c.Frozen) BurstFx.Play(BurstFx.Kind.Geyser, landing, size, Quaternion.Euler(-90f, 0f, 0f), look: look);
        else BurstFx.Play(BurstFx.Kind.Chips, landing, 0.6f);
    }

    void Bolt(Vector3 landing, Color? look)
    {
        ArcFx.Play(Hand, landing, 0.16f, 0f, 0.45f, look);
        ArcFx.Play(Hand, landing, 0.07f, 0.04f, 0.4f, look);
        grid.ChargeStartsAt(landing);
    }

    void Unleash(SpellDef def, SpellVisuals.Entry look, Vector3 landing, CastCommand cast, System.Action onArrive, Vector3? strike = null, float fall = MeteorFall.Seconds)
    {
        using var unleashing = UnleashMarker.Auto();
        int charge = cast.Charge;
        void Land()
        {
            using var landed = LandMarker.Auto();
            look?.PlayImpact(landing, Quaternion.LookRotation(Flat(landing - Hand)), look.ScaleFor(def, charge));
            ElementBurst(def, look, landing, charge);
            if (def != null && def.Jagged > 0) Spires(def, cast);
            LightInLook(def, look, cast);
            GameSounds.Play(GameSounds.LandingOf(def), landing);
            CastImpact.Landed(def, landing, charge);
            onArrive?.Invoke();
        }

        if (def?.Element == LightningElement && def.Shape == SpellShapes.Line) Bolt(strike ?? landing, look?.glow);
        if (look == null) Land();
        else if (look.fromSky)
            MeteorFall.Launch(landing, transform.position, look, () =>
            {
                LightInLook(def, look, cast);
                GameSounds.Play(GameSounds.LandingOf(def), landing);
                onArrive?.Invoke();
            }, this, fall, blastScale: look.ScaleFor(def, charge));
        else Travel(look, def, charge, landing, Land, strike);
    }

    static readonly Unity.Profiling.ProfilerMarker UnleashMarker = new("SpellCaster.Unleash"), LandMarker = new("SpellCaster.Land");

    void Travel(SpellVisuals.Entry look, SpellDef def, int charge, Vector3 landing, System.Action land, Vector3? strike)
    {
        if (look.storm) Storm(look, def, charge, landing, land);
        else if (look.beam) FrostBeam.Launch(Hand, strike ?? landing + Vector3.up * BeamLift, look.glow, land, this, look.Tint);
        else if (look.projectile != null) SpellProjectile.Launch(look, Hand, strike ?? landing, land, this, look.ScaleFor(def, 1));
        else land();
    }

    void Storm(SpellVisuals.Entry look, SpellDef def, int charge, Vector3 landing, System.Action land)
    {
        int radius = def != null ? def.Radius + World.ChargedGrowth(def, charge) : 0;
        float halfWidth = (radius + 0.5f) * grid.cellSize;
        float seconds = def != null && def.Duration > 0 ? def.Duration / (float)grid.World.Rules.Sim.TicksPerSecond : SleetStorm.Seconds;
        SleetStorm.Launch(landing, halfWidth, seconds, transform.position, land, this, look.Tint);
    }

    public override Color? LookGlow(string spellId) => LookOf(spellId)?.glow;

    public override int Slot => TryGetComponent<NetWizard>(out var net) ? (int)net.OwnerClientId : 0;

    public override bool FireLookOf(string spellId, out LookTint tint, out Color glow)
    {
        var look = LookOf(spellId);
        bool keeps = look is { KeepsFire: true };
        tint = keeps ? look.Tint : default;
        glow = keeps ? look.glow : default;
        return keeps;
    }

    void LightInLook(SpellDef def, SpellVisuals.Entry look, CastCommand cast)
    {
        if (look != null) grid.LightInLook(this, def, look.KeepsFire, cast);
    }

    public SpellVisuals.Entry LookOf(string spellId) => visuals != null && grid != null ? visuals.For(grid.World?.Rules, spellId, grid.CurrentRules?.Base) : null;

    public bool FallsFromSky(string spellId) => LookOf(spellId) is { fromSky: true } or { storm: true };

    const float BeamLift = 0.4f;

    public CastGesture GestureOf(string spellId) =>
        CastGestures.For(grid != null && grid.World != null ? grid.World.Rules.Spell(spellId) : null, LookOf(spellId));

    public Color GlowOf(string spellId) => LookOf(spellId)?.glow ?? Color.white;

    public LookTint TintOf(string spellId) => spellId != null && LookOf(spellId) is { } look ? look.Tint : default;

    public LookTint OrbTint(string element, string spellId)
    {
        var look = spellId != null ? LookOf(spellId) : null;
        if (look != null && (look.ChoseColour || look.Tint.Any)) return look.Tint;
        return grid != null ? grid.ElementTint(element, HudDraw.ElementColor(element)) : default;
    }

    Vector3 Hand => pose != null ? pose.HandPosition : transform.position + Vector3.up * 1.3f + transform.forward * 0.4f;

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

    public float FlightSeconds(CastCommand cast)
    {
        var look = LookOf(cast.Spell);
        float release = CastGestures.Of(GestureOf(cast.Spell)).release;
        if (look != null && look.fromSky) return release + MeteorFall.Seconds;
        if (look != null && look.storm) return release + SleetStorm.LandSeconds;
        if (look != null && look.beam) return release + FrostBeam.ReachSeconds;
        return release + (look?.projectile != null ? Vector3.Distance(Hand, Landing(cast)) / SpellProjectile.DefaultSpeed : 0f);
    }

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
        var (aimX, aimY) = AimOf(spell);
        int? dir = aimX != 0 || aimY != 0 ? World.DirOf(aimX, aimY) : DirFrom(me, target);
        if (spell.Origin != "self") return (target.x, target.y, dir ?? 1, aimX, aimY);
        if (spell.Shape == SpellShapes.Burst) return (me.x, me.y, dir ?? 1, 0, 0);
        if (dir == null) { error = $"Click away from the wizard to point {spell.Name} that way."; return null; }
        var (dx, dy) = Step(dir.Value);
        if (LockedOn != null && spell.Shape == SpellShapes.Line) (aimX, aimY) = AimOf(spell, grid.CellCenter(me.x + dx, me.y + dy));
        return (me.x + dx, me.y + dy, dir.Value, aimX, aimY);
    }

    (int x, int y, int dir, int aimX, int aimY)? AimMove(SpellDef spell, Vector2Int me, Vector2Int target, out string error)
    {
        bool blink = spell.Effect == SpellShapes.TeleportEffect;
        error = blink ? CheckBlink(spell, target) : MageHand.Check(grid, spell, hands, me, target.x, target.y, Charge);
        if (error != null) return null;
        var lands = blink && blinkLanding.HasValue ? grid.CellAt(blinkLanding.Value) : target;
        return (lands.x, lands.y, 1, 0, 0);
    }

    (int x, int y, int dir, int aimX, int aimY)? AimShift(SpellDef spell, out string error)
    {
        if (shape == null) { error = $"{spell.Name} needs a body to change"; return null; }
        error = shape.Aim(spell, Aiming.Ray(controls), out var cell);
        if (error != null || !grid.World.InBounds(cell.x, cell.y)) return null;
        return (cell.x, cell.y, 1, 0, 0);
    }

    const int AimSteps = 16;

    (int, int) AimOf(SpellDef spell) => AimOf(spell, transform.position);

    (int, int) AimOf(SpellDef spell, Vector3 from)
    {
        if (spell.Shape != SpellShapes.Line && spell.Shape != SpellShapes.Cone) return (0, 0);
        var d = crosshair.point - from;
        return World.SnapAim(Mathf.RoundToInt(d.x / grid.cellSize * AimSteps), Mathf.RoundToInt(-d.z / grid.cellSize * AimSteps));
    }

    string CheckBlink(SpellDef spell, Vector2Int target)
    {
        blinkLanding = null;
        if (!hasCrosshair || CrosshairCell != target) return MistyStep.Check(grid, spell, transform, target.x, target.y, Charge);
        string why = MistyStep.Reach(grid, spell, transform, crosshair, out var landing, Charge);
        if (why == null) blinkLanding = landing;
        return why;
    }

    static int? DirFrom(Vector2Int a, Vector2Int b)
    {
        int dx = b.x - a.x, dy = b.y - a.y;
        if (dx == 0 && dy == 0) return null;
        if (Mathf.Abs(dx) >= Mathf.Abs(dy)) return dx > 0 ? 1 : 3;
        return dy > 0 ? 2 : 0;
    }

    static (int, int) Step(int dir) => dir switch { 0 => (0, -1), 1 => (1, 0), 2 => (0, 1), _ => (-1, 0) };

    public AimMarker Marker => marker;

    static readonly Unity.Profiling.ProfilerMarker ShowAimMarker = new("SpellCaster.ShowAim");

    void ShowAim(Vector2Int target, SpellDef spell, (int x, int y, int dir, int aimX, int aimY)? aim)
    {
        using var marked = ShowAimMarker.Auto();
        marker ??= new AimMarker(grid, grid.aimShader != null ? grid.aimShader : Shader.Find("Prototype/AimMarker"));
        int charge = Charge;
        var (held, heldCharge) = HeldCast();
        if (held != null)
        {
            spell = grid.World.Rules.Spell(held);
            charge = heldCharge;
            aim = Aim(held, target, out _);
        }
        else if (controls.ElementPad && (controls.HandPad || controls.UsingGamepad)) aim = null;
        if (spell == null || !aim.HasValue)
        {
            marker.Hide();
            return;
        }
        var a = aim.Value;
        var cast = new CastCommand(spell.Id, a.x, a.y, a.dir, charge, a.aimX, a.aimY);
        marker.Show(spell, grid.World.SpellCells(spell, a.x, a.y, a.dir, charge, a.aimX, a.aimY), cast, GlowOf(spell.Id));
    }
}
