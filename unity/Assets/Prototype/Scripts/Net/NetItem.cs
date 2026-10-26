using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

// A thing to carry in multiplayer (next to its NetProp, which makes the host
// simulate it while it lies about or flies).
//
// - Who holds it is one NetworkVariable, the holder wizard's object id. A
//   player asks the host to pick it up; the host checks it is free and near,
//   and sets it. Every machine then puts it in that wizard's hand
//   (ItemHolder), so it follows the hand on every screen; on clients the
//   NetworkTransform is paused meanwhile, so it doesn't fight the hand.
// - Drop and throw: the holder sends where the hand is and the velocity; the
//   host lets go there and its physics flies it, and everyone follows.
// - Its light being covered or not is a NetworkVariable too, so a lantern's
//   beam and a glowing ball look the same to everyone.
// - The Dancing Lights mote: the holder sends where it is, a few times a
//   second, and everyone else eases it there.
// - Sending stones: the message goes to the machine of whoever holds the
//   other stone, and only that player sees it.
// The spyglass needs nothing: zooming is the holder's own view.
[RequireComponent(typeof(Carryable))]
public class NetItem : NetworkBehaviour
{
    /// <summary>No one holds it.</summary>
    public const ulong Nobody = ulong.MaxValue;
    // How far (m) from the item a wizard may be when the host grants a pick-up (the hand's reach plus lag).
    const float GrantReach = 4f;
    // The most a spell may ask for (Mage Hand: 30 ft, 6 squares of 1.5 m, plus lag), so a client
    // can't claim any reach it likes.
    const float MaxGrantReach = 12f;
    // How often the holder sends the mote's position, per second.
    const float MoteRate = 15f;

    readonly NetworkVariable<ulong> holder = new(Nobody);
    readonly NetworkVariable<bool> lit = new(true);

    Carryable item;
    NetworkTransform follow;
    float nextMote;

    public bool Online => IsSpawned;
    /// <summary>Whether its light is uncovered.</summary>
    public bool Lit => lit.Value;

    void Awake()
    {
        item = GetComponent<Carryable>();
        TryGetComponent(out follow);
    }

    public override void OnNetworkSpawn()
    {
        holder.OnValueChanged += HolderChanged;
        lit.OnValueChanged += LitChanged;
        Follow(holder.Value);
        if (TryGetComponent<ItemLight>(out var light)) light.SetOn(lit.Value);
    }

    public override void OnNetworkDespawn()
    {
        holder.OnValueChanged -= HolderChanged;
        lit.OnValueChanged -= LitChanged;
    }

    void HolderChanged(ulong was, ulong now) => Follow(now);

    void LitChanged(bool was, bool now)
    {
        if (TryGetComponent<ItemLight>(out var light)) light.SetOn(now);
    }

    void Update()
    {
        // The holder left the game: the host lets the item drop where it is.
        if (!IsServer || !IsSpawned || holder.Value == Nobody) return;
        if (NetworkManager.SpawnManager.SpawnedObjects.ContainsKey(holder.Value)) return;
        holder.Value = Nobody;
        Follow(Nobody);
    }

    // Puts the item in the hand of the wizard with that object id (or lets it go), on this machine.
    void Follow(ulong id)
    {
        ItemHolder next = null;
        if (id != Nobody && NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(id, out var wizard)) wizard.TryGetComponent(out next);
        if (follow != null) follow.enabled = IsServer || next == null;
        if (item.Holder == next) return;
        if (item.Holder != null)
        {
            item.Holder.Forget(item);
            item.Release(Vector3.zero);
        }
        if (next != null) next.Grant(item);
    }

    // --- picking up and letting go --------------------------------------------------

    /// <summary>Asks the host to put this item in the local player's hand.</summary>
    public void RequestPickUp() => PickUpRpc(GrantReach);

    /// <summary>Asks the host for this item from up to <paramref name="reach"/> metres away (Mage Hand), capped at MaxGrantReach.</summary>
    public void RequestPickUp(float reach) => PickUpRpc(reach);

    [Rpc(SendTo.Server)]
    void PickUpRpc(float reach, RpcParams rpc = default)
    {
        if (holder.Value != Nobody) return;
        var wizard = NetworkManager.SpawnManager.GetPlayerNetworkObject(rpc.Receive.SenderClientId);
        if (wizard == null || !wizard.TryGetComponent<ItemHolder>(out var hands) || hands.Held != null) return;
        float allowed = Mathf.Clamp(reach, GrantReach, MaxGrantReach);
        if ((wizard.transform.position - transform.position).sqrMagnitude > allowed * allowed) return;
        holder.Value = wizard.NetworkObjectId;
        Follow(holder.Value);
    }

    /// <summary>Asks the host to let go of this item at <paramref name="from"/>, moving at <paramref name="velocity"/>.</summary>
    public void RequestRelease(Vector3 from, Vector3 velocity) => ReleaseRpc(from, velocity);

    [Rpc(SendTo.Server)]
    void ReleaseRpc(Vector3 from, Vector3 velocity, RpcParams rpc = default)
    {
        var wizard = NetworkManager.SpawnManager.GetPlayerNetworkObject(rpc.Receive.SenderClientId);
        if (holder.Value == Nobody || wizard == null || wizard.NetworkObjectId != holder.Value) return;
        if (item.Holder != null) item.Holder.Forget(item);
        item.ReleaseAt(from, velocity);
        holder.Value = Nobody;
    }

    // --- its light --------------------------------------------------------------------

    /// <summary>Asks the host to cover or uncover its light, for everyone.</summary>
    public void RequestLit(bool on) => LitRpc(on);

    [Rpc(SendTo.Server)]
    void LitRpc(bool on) => lit.Value = on;

    // --- the Dancing Lights mote ------------------------------------------------------------

    /// <summary>Holder: where the mote is (sent a few times a second, and at once when it goes out or comes back).</summary>
    public void ShareMote(bool isOut, Vector3 at, bool now)
    {
        if (!now && Time.time < nextMote) return;
        nextMote = Time.time + 1f / MoteRate;
        MoteRpc(isOut, at);
    }

    [Rpc(SendTo.NotMe, Delivery = RpcDelivery.Unreliable)]
    void MoteRpc(bool isOut, Vector3 at)
    {
        if (TryGetComponent<DancingLights>(out var lights)) lights.Follow(isOut, at);
    }

    // --- sending stones ---------------------------------------------------------------------

    /// <summary>Sends a message to the player holding this stone: only their screen shows it, with a ping at <paramref name="from"/>.</summary>
    public void Whisper(ulong client, Vector3 from, string message, string sender) =>
        HeardRpc(from, new FixedString128Bytes(message), new FixedString64Bytes(sender), RpcTarget.Single(client, RpcTargetUse.Temp));

    [Rpc(SendTo.SpecifiedInParams)]
    void HeardRpc(Vector3 from, FixedString128Bytes message, FixedString64Bytes sender, RpcParams rpc = default)
    {
        if (TryGetComponent<SendingStone>(out _)) SendingStone.Heard(from, message.ToString(), sender.ToString());
    }
}
