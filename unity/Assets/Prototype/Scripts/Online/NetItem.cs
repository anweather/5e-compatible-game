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
// - The first find of a mod's thing: the host stamps it into the next tick as it grants.
// - Sending stones: the message goes to the machine of whoever holds the
//   other stone, and only that player sees it.
// - A pick: the holder asks the host to swing at a cell near them; the host
//   stamps the dig for every machine (Pick, ChemEffects).
// The spyglass needs nothing: zooming is the holder's own view.
[RequireComponent(typeof(Carryable))]
public class NetItem : NetworkBehaviour
{
    public const ulong Nobody = ulong.MaxValue;
    const float GrantReach = 4f;
    const float MaxGrantReach = 12f;
    const float MoteRate = 15f;
    const int SwingReachCells = 4;

    readonly NetworkVariable<ulong> holder = new(Nobody);
    readonly NetworkVariable<bool> lit = new(true);

    Carryable item;
    NetworkTransform follow;
    float nextMote;

    public bool Online => IsSpawned;
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
        item.Lost();
    }

    void HolderChanged(ulong was, ulong now) => Follow(now);

    void LitChanged(bool was, bool now)
    {
        if (TryGetComponent<ItemLight>(out var light)) light.SetOn(now);
    }

    void Update()
    {
        if (!IsServer || !IsSpawned || holder.Value == Nobody) return;
        if (NetworkManager.SpawnManager.SpawnedObjects.ContainsKey(holder.Value)) return;
        holder.Value = Nobody;
        Follow(Nobody);
    }

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

    public void RequestPickUp() => PickUpRpc(GrantReach);

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
        if (item.Prop.FromMod) Peers.Of(item.Grid).StampFound(Peers.NameOf(checked((int)rpc.Receive.SenderClientId)), item.Prop.Thing);
    }

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

    public void RequestLit(bool on) => LitRpc(on);

    [Rpc(SendTo.Server)]
    void LitRpc(bool on) => lit.Value = on;

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

    public void RequestDig(Vector2Int cell) => DigRpc(cell.x, cell.y);

    [Rpc(SendTo.Server)]
    void DigRpc(int x, int y, RpcParams rpc = default)
    {
        var wizard = NetworkManager.SpawnManager.GetPlayerNetworkObject(rpc.Receive.SenderClientId);
        if (wizard == null || wizard.NetworkObjectId != holder.Value || !TryGetComponent<Pick>(out var pick)) return;
        var cell = new Vector2Int(x, y);
        if ((item.Grid.CellAt(wizard.transform.position) - cell).sqrMagnitude > SwingReachCells * SwingReachCells) return;
        pick.Swing(cell);
    }

    public void Whisper(ulong client, Vector3 from, string message, string sender) =>
        HeardRpc(from, new FixedString128Bytes(message), new FixedString64Bytes(sender), RpcTarget.Single(client, RpcTargetUse.Temp));

    [Rpc(SendTo.SpecifiedInParams)]
    void HeardRpc(Vector3 from, FixedString128Bytes message, FixedString64Bytes sender, RpcParams rpc = default)
    {
        if (TryGetComponent<SendingStone>(out _)) SendingStone.Heard(from, message.ToString(), sender.ToString());
    }
}
