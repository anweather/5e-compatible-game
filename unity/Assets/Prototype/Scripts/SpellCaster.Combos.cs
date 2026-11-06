using Chem;
using UnityEngine;

// Casting by elements (docs/SPELL_COMBOS.md): the wizard's side of World.Combos.
// Each button sends a ComboCommand (applied at once solo, stamped by the host
// online, like a cast); the meter drains in the core, on every peer; when it
// reaches zero the core reports it (FiredCastsHeard) and the player whose cast
// it is aims it from the crosshair and sends the usual CastCommand, since where
// a spell lands is only known as it goes off. While the meter drains the wizard
// walks at the rules' factor and Cancel drops the cast before anything else
// that button does. Holding the last element charges the cast: the meter runs on
// through the charge (rules/combos.json chargeTicks, a level at a time) and the
// core counts it, so every peer sees the same strength, and letting go (ElementHold
// knows the button) sets it off; a tap is charge 1. The radial (SpellRadialHud)
// stays as a debug tool.
public partial class SpellCaster
{
    /// <summary>The solo wizard's name in the core's casts.</summary>
    public const string SoloPlayer = "solo";

    SurfaceEffects feet;
    ChemGrid heard;
    readonly FiredCastsHeard firedCasts = new();
    // The frame a press cancelled the cast: that press is spent, even for what reads it after.
    int cancelledOnFrame = -1;
    readonly ElementHold hold = new();

    /// <summary>This player's name in the core's casts: online their client's, solo a fixed one.</summary>
    public string CastPlayer => Net != null && Net.Online ? Net.LocalPlayer : SoloPlayer;

    /// <summary>Online and still replaying the game so far: the casts in the core were made long ago (CastBeats).</summary>
    public bool Replaying => Net != null && Net.Online && !Net.CaughtUp;

    /// <summary>The meter is draining (ItemHolder: the throw button cancels instead).</summary>
    public bool CastInProgress => cancelledOnFrame == Time.frameCount || (grid != null && grid.World != null && grid.World.CastOf(CastPlayer)?.Casting == true);

    /// <summary>The charge the held element has built so far, or 0 when none is held (CastHud's steps).</summary>
    public int HoldCharge
    {
        get
        {
            var cast = grid.World.CastOf(CastPlayer);
            return cast?.Holding == true ? grid.World.HeldCharge(cast) : 0;
        }
    }

    // The spell a cast under way will go off as, and its charge so far; null with none under way.
    (string spell, int charge) HeldCast()
    {
        var world = grid.World;
        var cast = world.CastOf(CastPlayer);
        if (cast == null || !cast.Casting) return (null, 1);
        var dpad = world.DpadOf(cast.Player);
        if (cast.DpadCasting >= 0 && cast.DpadCasting < dpad.Count) return (dpad[cast.DpadCasting].Spell, 1);
        if (world.HandSpellUnderWay(cast.Player) is { } hand) return (hand, 1);
        var combo = world.ComboOf(cast);
        int charge = Mathf.Max(1, Mathf.Max(HoldCharge, cast.Charge));
        return (combo?.SpellAt(charge, world.MaxChargeOf(combo)), charge);
    }

    /// <summary>The spell a cast under way will go off as, at the charge it has so far; null if the elements make none yet.</summary>
    public string SpellOf(PlayerCast cast)
    {
        var world = grid.World;
        var dpad = world.DpadOf(cast.Player);
        if (cast.DpadCasting >= 0 && cast.DpadCasting < dpad.Count) return dpad[cast.DpadCasting].Spell;
        if (world.HandSpellUnderWay(cast.Player) is { } hand) return hand;
        var combo = world.ComboOf(cast);
        return combo?.SpellAt(Mathf.Max(1, cast.Charge), world.MaxChargeOf(combo));
    }

    void UpdateElements()
    {
        Listen();
        OnTick(); // a tick finished from outside (World.FinishTick) tells no one
        var world = grid.World;
        string player = CastPlayer;
        if (feet != null || TryGetComponent(out feet)) feet.CastFactor = (float)world.MoveFactorOf(player);
        var cast = world.CastOf(player);
        bool busy = (shape != null && shape.Shifted) || (radial != null && radial.IsOpen);

        bool handsOn = world.HandsOn(player);
        controls.HandPad = handsOn;
        Elements(player, cast, busy || handsOn);
        if (handsOn) Hands(player, busy);
        if (busy) return;
        if (controls.CancelCast.WasPressedThisFrame() && cast?.Casting == true)
        {
            cancelledOnFrame = Time.frameCount;
            hold.Drop();
            Send(ComboCommand.Cancel(player));
        }
        if (controls.DpadNext.WasPressedThisFrame()) Send(ComboCommand.Cycle(player, 1));
        if (controls.DpadPrevious.WasPressedThisFrame()) Send(ComboCommand.Cycle(player, -1));
        if (controls.DpadCast.WasPressedThisFrame()) Send(ComboCommand.CastSelected(player));
        if (controls.Grimoire.WasPressedThisFrame()) GrimoireMenu.Show();
    }

    // The element buttons: a press loads and starts the hold, and letting go sends its charge.
    void Elements(string player, PlayerCast cast, bool busy)
    {
        var elements = grid.World.Rules.Combos.Elements;
        for (int i = 0; !busy && i < elements.Count && i < controls.Elements.Length; i++)
            if (controls.Elements[i].WasPressedThisFrame() && cast?.TakesElement != false && Send(ComboCommand.Load(player, elements[i], true)))
                hold.Press(i);
        // Read from the button, not its release this frame: whatever turns the controls off lets go too,
        // or the cast would wait for ever. Below the press, so a tap inside one frame lets go at once.
        if (hold.Holding && !controls.Elements[hold.Button].IsPressed())
        {
            Send(ComboCommand.Release(player));
            hold.Drop();
        }
    }

    /// <summary>
    /// Loads an element as its button does, through the same ComboCommand (the playtest bot presses
    /// here: it drives no real device), held until <see cref="LetGo"/> if <paramref name="held"/>.
    /// False if the core refused it; online, true once it's sent.
    /// </summary>
    public bool LoadElement(string element, bool held = false) => grid != null && grid.World != null && Send(ComboCommand.Load(CastPlayer, element, held));

    /// <summary>Lets go of an element loaded held, at the charge its meter has reached.</summary>
    public bool LetGo() => grid != null && grid.World != null && Send(ComboCommand.Release(CastPlayer));

    /// <summary>Drops a cast under way, as Cancel does (WizardDown: a wizard that falls loses it).</summary>
    public void DropCast()
    {
        hold.Drop();
        if (grid != null && grid.World != null && grid.World.CastOf(CastPlayer)?.Casting == true) Send(ComboCommand.Cancel(CastPlayer));
    }

    /// <summary>
    /// Where the crosshair is instead, for the marker and the casts, while set: the playtest bot, which
    /// has no mouse and points its casts at cells, and the probe. Null for players.
    /// </summary>
    public Vector3? AimOverride { get; set; }

    bool Send(ComboCommand command)
    {
        var online = Net;
        if (online == null || !online.Online) return grid.World.ApplyCombo(command);
        online.RequestCombo(command);
        return true;
    }

    // Hears the grid's ticks, and every frame in UpdateElements: FiredCastsHeard gives each cast once.
    void Listen()
    {
        if (heard == grid) return;
        Unlisten();
        heard = grid;
        firedCasts.StartFrom(grid.World);
        heard.TickDone += OnTick;
    }

    void Unlisten()
    {
        if (heard != null) heard.TickDone -= OnTick;
        heard = null;
    }

    void OnTick()
    {
        var fireds = firedCasts.Take(grid.World);
        // Replaying the game so far as a late joiner, those casts were made long ago.
        if (fireds.Count == 0 || Replaying) return;
        string player = CastPlayer;
        foreach (var fired in fireds)
        {
            if (fired.Player != player) continue;
            if (fired.Discovered) EventLog.Add($"New in your grimoire: {fired.Name}");
            if (fired.Range > 0 || fired.Act != null) FromHand(fired);
            else CastAtCrosshair(fired.Spell, fired.Charge);
        }
    }

    /// <summary>Aims a spell from the crosshair now (or a few cells ahead, with nothing under it) and casts it.</summary>
    void CastAtCrosshair(string spellId, int charge, int range = 0)
    {
        if (!FindCrosshair())
        {
            crosshair = default;
            crosshair.point = transform.position + transform.forward * (AheadCells * grid.cellSize);
            crosshair.normal = Vector3.up;
        }
        var me = grid.CellAt(transform.position);
        var (toX, toY) = HandsDef.Within(me.x, me.y, CrosshairCell.x, CrosshairCell.y, range);
        var aim = Aim(spellId, new Vector2Int(toX, toY), out var error);
        if (error != null) EventLog.Add(error);
        if (range > 0 && aim.HasValue) LastHandAim = new Vector2Int(aim.Value.x, aim.Value.y);
        if (aim.HasValue) Cast(new CastCommand(spellId, aim.Value.x, aim.Value.y, aim.Value.dir, charge, aim.Value.aimX, aim.Value.aimY, range > 0 ? CastPlayer : null));
    }

    const int AheadCells = 4;

    void OnDisable()
    {
        Unlisten();
        marker?.Hide();
    }
}
