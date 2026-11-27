using Chem;
using UnityEngine;
using UnityEngine.InputSystem;

// The wizard's side of World.Combos: casting from the hands (SpellCaster.Hands.cs, D16), the
// kit on the d-pad, Cancel, and the spell list's buttons. Each button sends a ComboCommand
// (applied at once solo, stamped by the host online, like a cast); the meter drains in the
// core, on every peer; when it reaches zero the core reports it (FiredCastsHeard) and the
// player whose cast it is aims it from the crosshair and sends the usual CastCommand, since
// where a spell lands is only known as it goes off. While the meter drains the wizard walks
// at the rules' factor and Cancel drops the cast before anything else that button does. The
// element path (World.Combos' Load, an ordered pair of elements) has no buttons now, and nothing here sends it: the
// bot casts with the hands (CastByHands), and only the probes that test the element path's own beats and looks load
// elements (ProbeScenario.LoadElement). The radial (SpellRadialHud) stays as a debug layout (F2).
public partial class SpellCaster
{
    public const string SoloPlayer = "solo";

    SurfaceEffects feet;
    ChemGrid heard;
    readonly FiredCastsHeard firedCasts = new();
    int cancelledOnFrame = -1;

    public string CastPlayer => Peers.Of(grid).Player;

    public bool Replaying => !Peers.Of(grid).CaughtUp;

    public bool CastInProgress => cancelledOnFrame == Time.frameCount || (grid != null && grid.World != null && grid.World.CastOf(CastPlayer)?.Casting == true);

    public int HoldCharge
    {
        get
        {
            var cast = grid.World.CastOf(CastPlayer);
            return cast?.Holding == true ? grid.World.HeldCharge(cast) : 0;
        }
    }

    (string spell, int charge) HeldCast()
    {
        var world = grid.World;
        var cast = world.CastOf(CastPlayer);
        if (cast == null || !cast.Casting) return (null, 1);
        var dpad = world.DpadOf(cast.Player);
        if (cast.DpadCasting >= 0 && cast.DpadCasting < dpad.Count) return (dpad[cast.DpadCasting].Spell, 1);
        if (world.HandSpellUnderWay(cast.Player) is { } hand) return (hand, HeldHandCharge(world, cast.Player));
        var combo = world.ComboOf(cast);
        int charge = Mathf.Max(1, Mathf.Max(HoldCharge, cast.Charge));
        return (combo?.SpellAt(charge, world.MaxChargeOf(combo)), charge);
    }

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
        OnTick();
        var world = grid.World;
        string player = CastPlayer;
        if (feet != null || TryGetComponent(out feet)) feet.CastFactor = (float)world.MoveFactorOf(player);
        var cast = world.CastOf(player);
        bool busy = (shape != null && shape.Shifted) || (radial != null && radial.IsOpen);

        bool handsOn = world.HandsOn(player) && controls.ElementPad;
        controls.HandPad = handsOn;
        bool menuWasOpen = HandsMenu.OpenFor(this);
        if (handsOn && !busy) HandsMenu.Feed(this, controls.OpenSpellList.IsPressed(), controls.OpenSpellList.activeControl?.device as Gamepad);
        else if (HandsMenu.OpenFor(this)) HandsMenu.Close();
        busy |= menuWasOpen || HandsMenu.OpenFor(this);
        Hands(player, busy || !handsOn);
        if (busy) return;
        if (controls.CancelCast.WasPressedThisFrame() && cast?.Casting == true)
        {
            cancelledOnFrame = Time.frameCount;
            DropHeld();
            Send(ComboCommand.Cancel(player));
        }
        if (controls.DpadNext.WasPressedThisFrame()) Send(ComboCommand.Cycle(player, 1));
        if (controls.DpadPrevious.WasPressedThisFrame()) Send(ComboCommand.Cycle(player, -1));
        if (controls.DpadCast.WasPressedThisFrame()) Send(ComboCommand.CastSelected(player));
        if (!handsOn && controls.OpenSpellList.WasPressedThisFrame()) SpellListMenu.Show(this, true);
    }

    public bool SelectKit(int slot)
    {
        if (grid == null || grid.World == null) return false;
        var dpad = grid.World.DpadOf(CastPlayer);
        var now = grid.World.SelectedDpad(CastPlayer);
        int at = 0;
        for (int i = 0; i < dpad.Count; i++)
            if (dpad[i] == now) at = i;
        if (slot < 0 || slot >= dpad.Count) return false;
        for (int step = System.Math.Sign(slot - at), left = System.Math.Abs(slot - at); left > 0; left--)
            if (!Send(ComboCommand.Cycle(CastPlayer, step))) return false;
        return true;
    }

    public bool LetGo() => grid != null && grid.World != null && Send(ComboCommand.Release(CastPlayer));

    public void DropCast()
    {
        DropHeld();
        if (grid != null && grid.World != null && grid.World.CastOf(CastPlayer)?.Casting == true) Send(ComboCommand.Cancel(CastPlayer));
    }

    public Vector3? AimOverride { get; set; }

    bool Send(ComboCommand command)
    {
        var online = Peers.Of(grid);
        if (!online.Online()) return grid.World.ApplyCombo(command);
        online.RequestCombo(command);
        return true;
    }

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

    void CastAtCrosshair(string spellId, int charge, int range = 0)
    {
        if (!FindCrosshair())
        {
            crosshair = default;
            crosshair.point = transform.position + transform.forward * (AheadCells * grid.cellSize);
            crosshair.normal = Vector3.up;
        }
        LockOnto(grid.World.Rules.Spell(spellId));
        if (range > 0) LastLocked = LockedOn;
        var me = grid.CellAt(transform.position);
        var (toX, toY) = HandsDef.Within(me.x, me.y, CrosshairCell.x, CrosshairCell.y, range);
        var aim = Aim(spellId, new Vector2Int(toX, toY), out var error);
        if (error != null) EventLog.Add(error);
        if (range > 0 && aim.HasValue) LastHandAim = new Vector2Int(aim.Value.x, aim.Value.y);
        if (range > 0 && aim.HasValue) LastHandCast = new CastCommand(spellId, aim.Value.x, aim.Value.y, aim.Value.dir, charge, aim.Value.aimX, aim.Value.aimY);
        StrikeAtLocked();
        if (aim.HasValue) Cast(new CastCommand(spellId, aim.Value.x, aim.Value.y, aim.Value.dir, charge, aim.Value.aimX, aim.Value.aimY, range > 0 ? CastPlayer : null));
    }

    const int AheadCells = 4;

    void OnDisable()
    {
        Unlisten();
        marker?.Hide();
    }
}
