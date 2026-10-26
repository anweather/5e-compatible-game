using Unity.Cinemachine;
using Unity.Netcode;
using UnityEngine;

// A player's wizard in multiplayer. The prefab ships with its input, movement
// and spellcasting switched off; on spawn the owner switches them on and
// takes the camera, while everyone else's copy stays a puppet moved by its
// NetworkTransform and NetworkAnimator (both owner-authoritative).
//
// Every copy has an ItemHolder, so an item someone holds rides in their hand
// on every screen (NetItem); only the owner's copy has controls. A shove
// another machine works out for this wizard (a barrel blast on the host) is
// sent to the owner, who applies it (Knockback).
[RequireComponent(typeof(SpellCaster), typeof(SurfaceEffects), typeof(PlayerControls))]
public class NetWizard : NetworkBehaviour
{
    public override void OnNetworkSpawn()
    {
        var grid = FindFirstObjectByType<ChemGrid>();
        var caster = GetComponent<SpellCaster>();
        caster.grid = grid;

        if (!IsOwner)
        {
            name = $"Wizard (player {OwnerClientId + 1})";
            WizardRig.PrepareForAnimationEvents(gameObject);
            return;
        }

        name = WizardRig.Name;
        if (grid != null) transform.position = SpawnPoint(grid);

        WizardRig.SetPlayerDriven(gameObject, true);
        var surface = GetComponent<SurfaceEffects>();
        surface.grid = grid;
        surface.enabled = true;
        if (TryGetComponent<Knockback>(out var knockback)) knockback.grid = grid;
        var controls = GetComponent<PlayerControls>();
        controls.enabled = true;
        caster.controls = controls;
        caster.enabled = true;
        var follow = TakeCamera(controls);
        if (TryGetComponent<ItemHolder>(out var hands))
        {
            hands.controls = controls;
            hands.view = follow;
        }
    }

    // A wizard that leaves lets go of what it holds first, or the item (riding in its hand) would go with it.
    public override void OnNetworkDespawn()
    {
        if (TryGetComponent<ItemHolder>(out var hands)) hands.LetGoOnLeaving();
    }

    /// <summary>Sends a shove to this wizard's owner, whose machine moves it.</summary>
    public void PushOwner(Vector3 impulse) => PushRpc(impulse);

    /// <summary>
    /// Owner only: moves this wizard straight to a point (Misty Step), flagged as a teleport so
    /// the other machines jump it there instead of sliding it across.
    /// </summary>
    public void Teleport(Vector3 to)
    {
        if (TryGetComponent<Unity.Netcode.Components.NetworkTransform>(out var net) && net.CanCommitToTransform)
            net.Teleport(to, transform.rotation, transform.localScale);
        else transform.position = to;
    }

    [Rpc(SendTo.Owner)]
    void PushRpc(Vector3 impulse)
    {
        if (TryGetComponent<Knockback>(out var knockback)) knockback.Push(impulse);
    }

    // Players are spread around the start cell so they don't spawn inside each other.
    Vector3 SpawnPoint(ChemGrid grid)
    {
        float angle = OwnerClientId * 90f * Mathf.Deg2Rad;
        var start = grid.CellCenter(grid.PlayerStart.x, grid.PlayerStart.y);
        return start + new Vector3(Mathf.Cos(angle), 0.1f, Mathf.Sin(angle)) * grid.cellSize;
    }

    CinemachineCamera TakeCamera(PlayerControls controls)
    {
        var follow = FindFirstObjectByType<CinemachineCamera>();
        if (follow == null) return null;
        follow.Target.TrackingTarget = transform.Find(WizardRig.CameraRoot);
        if (follow.TryGetComponent<CameraZoom>(out var zoom)) zoom.controls = controls;
        return follow;
    }
}
