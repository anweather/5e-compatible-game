using Unity.Cinemachine;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

// A player's wizard in multiplayer. The prefab ships with its input, movement
// and spellcasting switched off; on spawn the owner switches them on and
// takes the camera, while everyone else's copy stays a puppet moved by its
// NetworkTransform and NetworkAnimator (both owner-authoritative).
//
// Starter Assets components are reached by name so this compiles before the
// Asset Store packs are restored.
public class NetWizard : NetworkBehaviour
{
    static readonly string[] OwnerOnlyByName = { "StarterAssetsInputs", "ThirdPersonController", "BasicRigidBodyPush" };

    public override void OnNetworkSpawn()
    {
        var grid = FindFirstObjectByType<ChemGrid>();
        var caster = GetComponent<SpellCaster>();
        caster.grid = grid;

        if (!IsOwner)
        {
            name = $"Wizard (player {OwnerClientId + 1})";
            return;
        }

        name = "Wizard";
        if (grid != null)
        {
            // Spread players around the start cell so they don't spawn inside each other.
            float angle = OwnerClientId * 90f * Mathf.Deg2Rad;
            var start = grid.CellCenter(grid.PlayerStart.x, grid.PlayerStart.y);
            transform.position = start + new Vector3(Mathf.Cos(angle), 0.1f, Mathf.Sin(angle)) * grid.cellSize;
        }

        GetComponent<CharacterController>().enabled = true;
        GetComponent<PlayerInput>().enabled = true;
        foreach (var typeName in OwnerOnlyByName)
            if (GetComponent(typeName) is Behaviour b) b.enabled = true;
        var controls = GetComponent<PlayerControls>();
        controls.enabled = true;
        caster.controls = controls;
        caster.enabled = true;

        var follow = FindFirstObjectByType<CinemachineCamera>();
        if (follow != null)
        {
            follow.Target.TrackingTarget = transform.Find("PlayerCameraRoot");
            if (follow.TryGetComponent<CameraZoom>(out var zoom)) zoom.controls = controls;
        }
    }
}
