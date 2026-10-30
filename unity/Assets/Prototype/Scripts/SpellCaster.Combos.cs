using Chem;
using UnityEngine;

// Casting by elements (docs/SPELL_COMBOS.md): the wizard's side of World.Combos.
// Each button sends a ComboCommand (applied at once solo, stamped by the host
// online, like a cast); the meter drains in the core, on every peer; when it
// reaches zero the core reports it (World.CombosFired) and the player whose cast
// it is aims it from the crosshair and sends the usual CastCommand, since where
// a spell lands is only known as it goes off. While the meter drains the wizard
// walks at the rules' factor and Cancel drops the cast before anything else
// that button does. The radial (SpellRadialHud) stays as a debug tool.
public partial class SpellCaster
{
    const string SoloPlayer = "solo";

    CastOrbs orbs;
    SurfaceEffects feet;
    ChemGrid heard;
    // The frame a press cancelled the cast: that press is spent, even for what reads it after.
    int cancelledOnFrame = -1;

    /// <summary>This player's name in the core's casts: online their client's, solo a fixed one.</summary>
    public string CastPlayer => Net != null && Net.Online ? Net.LocalPlayer : SoloPlayer;

    /// <summary>The meter is draining (ItemHolder: the throw button cancels instead).</summary>
    public bool CastInProgress => cancelledOnFrame == Time.frameCount || (grid != null && grid.World != null && grid.World.CastOf(CastPlayer)?.Casting == true);

    void UpdateElements()
    {
        Listen();
        var world = grid.World;
        string player = CastPlayer;
        if (feet != null || TryGetComponent(out feet)) feet.CastFactor = (float)world.MoveFactorOf(player);
        orbs ??= new CastOrbs(transform, pose);
        var cast = world.CastOf(player);
        orbs.Show(cast?.First, cast?.Second);
        if ((shape != null && shape.Shifted) || (radial != null && radial.IsOpen)) return;

        var elements = world.Rules.Combos.Elements;
        for (int i = 0; i < elements.Count && i < controls.Elements.Length; i++)
            if (controls.Elements[i].WasPressedThisFrame()) Send(ComboCommand.Load(player, elements[i]));
        if (controls.CancelCast.WasPressedThisFrame() && cast?.Casting == true)
        {
            cancelledOnFrame = Time.frameCount;
            Send(ComboCommand.Cancel(player));
        }
        if (controls.DpadNext.WasPressedThisFrame()) Send(ComboCommand.Cycle(player, 1));
        if (controls.DpadPrevious.WasPressedThisFrame()) Send(ComboCommand.Cycle(player, -1));
        if (controls.DpadCast.WasPressedThisFrame()) Send(ComboCommand.CastSelected(player));
        if (controls.Grimoire.WasPressedThisFrame()) GrimoireMenu.Show();
    }

    void Send(ComboCommand command)
    {
        var online = Net;
        if (online != null && online.Online) online.RequestCombo(command);
        else grid.World.ApplyCombo(command);
    }

    // Hears the grid's ticks: a tick's casts that went off are only there until the next one.
    void Listen()
    {
        if (heard == grid) return;
        Unlisten();
        heard = grid;
        heard.TickDone += OnTick;
    }

    void Unlisten()
    {
        if (heard != null) heard.TickDone -= OnTick;
        heard = null;
    }

    void OnTick()
    {
        var world = grid.World;
        if (world.CombosFired.Count == 0) return;
        // Replaying the game so far as a late joiner, those casts were made long ago.
        if (Net != null && Net.Online && !Net.CaughtUp) return;
        string player = CastPlayer;
        foreach (var fired in world.CombosFired)
        {
            if (fired.Player != player) continue;
            orbs?.Slam();
            if (fired.Discovered) EventLog.Add($"New in your grimoire: {fired.Name}");
            CastAtCrosshair(fired.Spell);
        }
    }

    /// <summary>Aims a spell from the crosshair now (or a few cells ahead, with nothing under it) and casts it.</summary>
    void CastAtCrosshair(string spellId)
    {
        if (controls == null || !Aiming.PastSelf(Aiming.Ray(controls), transform, out crosshair))
        {
            crosshair = default;
            crosshair.point = transform.position + transform.forward * (AheadCells * grid.cellSize);
            crosshair.normal = Vector3.up;
        }
        var aim = Aim(spellId, CrosshairCell, out var error);
        if (error != null) EventLog.Add(error);
        if (aim.HasValue) Cast(new CastCommand(spellId, aim.Value.x, aim.Value.y, aim.Value.dir, 1, aim.Value.aimX, aim.Value.aimY));
    }

    const int AheadCells = 4;

    void OnDisable() => Unlisten();
}
