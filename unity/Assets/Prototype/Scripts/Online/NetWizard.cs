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
// sent to the owner, who applies it (Knockback). A dodge roll's tumble is laid
// over the bones, outside the synced animation, so the owner announces each
// roll and every other copy plays it (RollPose).
//
// The animal shape a wizard has (Shapeshift: its species and coat, packed)
// is a variable its owner writes; every other machine draws that animal in
// place of the wizard, a late joiner too. Its look (WizardOutfit: a seed for
// the hat, robe and beard) is another owner-written variable, so
// every peer dresses each wizard the same.
[RequireComponent(typeof(SpellCaster), typeof(SurfaceEffects), typeof(PlayerControls))]
public class NetWizard : NetworkBehaviour
{
    static readonly Registry<NetWizard> registry = new();

    public static NetWizard[] All => registry.All();

    void Awake() => registry.Join(this);

    public override void OnDestroy()
    {
        registry.Leave(this);
        base.OnDestroy();
    }

    readonly NetworkVariable<int> form = new(Shapeshift.None, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    const int NoLook = int.MinValue;
    readonly NetworkVariable<int> look = new(NoLook, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    public override void OnNetworkSpawn()
    {
        var grid = ChemGrid.Current?.Grid;
        var caster = GetComponent<SpellCaster>();
        caster.grid = grid;
        form.OnValueChanged += FormChanged;
        look.OnValueChanged += LookChanged;
        KeepAnimating(gameObject);

        if (!IsOwner)
        {
            name = $"Wizard (player {OwnerClientId + 1})";
            WizardRig.PrepareForAnimationEvents(gameObject);
            FormChanged(Shapeshift.None, form.Value);
            LookChanged(NoLook, look.Value);
            return;
        }
        SetLook(WizardOutfit.SavedSeed);

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
        Local = this;
        startGrid = grid;
        if (grid != null) grid.WorldReset += BackToStart;
    }

    public static void KeepAnimating(GameObject wizard)
    {
        if (wizard.TryGetComponent<Animator>(out var animator)) animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
    }

    public string CastPlayer => Peers.PlayerOf(checked((int)OwnerClientId));

    public static NetWizard Local { get; private set; }

    ChemGrid startGrid;

    public override void OnNetworkDespawn()
    {
        if (Local == this) Local = null;
        if (startGrid != null) startGrid.WorldReset -= BackToStart;
        form.OnValueChanged -= FormChanged;
        look.OnValueChanged -= LookChanged;
        if (TryGetComponent<ItemHolder>(out var hands)) hands.LetGoOnLeaving();
    }

    public void SetForm(int value)
    {
        if (IsOwner) form.Value = value;
    }

    public void SetLook(int seed)
    {
        if (!IsOwner) return;
        look.Value = seed;
        if (TryGetComponent<WizardOutfit>(out var outfit)) outfit.Wear(seed);
    }

    void LookChanged(int was, int now)
    {
        if (!IsOwner && now != NoLook && TryGetComponent<WizardOutfit>(out var outfit)) outfit.Wear(now);
    }

    void FormChanged(int was, int now)
    {
        if (!IsOwner && TryGetComponent<Shapeshift>(out var shape)) shape.Show(now);
    }

    public void PushOwner(Vector3 impulse) => PushRpc(impulse);

    public void Teleport(Vector3 to)
    {
        if (TryGetComponent<Unity.Netcode.Components.NetworkTransform>(out var net) && net.CanCommitToTransform)
            net.Teleport(to, transform.rotation, transform.localScale);
        else transform.position = to;
    }

    public void Rolled(float seconds) => RollRpc(seconds);

    [Rpc(SendTo.NotOwner)]
    void RollRpc(float seconds)
    {
        if (TryGetComponent<RollPose>(out var pose)) pose.Play(seconds);
    }

    public void Downed(bool lying) => DownedRpc(lying);

    [Rpc(SendTo.NotOwner)]
    void DownedRpc(bool lying)
    {
        if (TryGetComponent<DownedPose>(out var pose)) pose.Lying = lying;
    }

    public void AskForReset() => AskForResetRpc();

    [Rpc(SendTo.Server)]
    void AskForResetRpc() =>
        EventLog.Add($"Player {OwnerClientId + 1} asks to reset the world: <b>R</b> calls the dragon, holding it rebuilds the valley at once");

    void BackToStart()
    {
        if (!OnlineReset.Happened(Peers.Of(startGrid))) return;
        if (TryGetComponent<Sitter>(out var sitter)) sitter.Stand();
        if (TryGetComponent<Shapeshift>(out var shape)) shape.ChangeBack("You're yourself again", force: true);
        if (TryGetComponent<ItemHolder>(out var hands)) hands.LetGoOnLeaving();
        if (TryGetComponent<Vitals>(out var vitals)) vitals.Refill();
        JumpToStart();
    }

    public void JumpToStart() => JumpTo(SpawnPoint(startGrid));

    public void JumpTo(Vector3 at)
    {
        bool hasBody = TryGetComponent<CharacterController>(out var body) && body.enabled;
        if (hasBody) body.enabled = false;
        Teleport(at);
        Physics.SyncTransforms();
        if (hasBody) body.enabled = true;
    }

    [Rpc(SendTo.Owner)]
    void PushRpc(Vector3 impulse)
    {
        if (TryGetComponent<Knockback>(out var knockback)) knockback.Push(impulse);
    }

    Vector3 SpawnPoint(ChemGrid grid)
    {
        float angle = OwnerClientId * 90f * Mathf.Deg2Rad;
        var start = grid.CellCenter(grid.PlayerStart.x, grid.PlayerStart.y);
        start.y = grid.GroundAt(start);
        return start + new Vector3(Mathf.Cos(angle), 0.1f, Mathf.Sin(angle)) * grid.cellSize;
    }

    CinemachineCamera TakeCamera(PlayerControls controls)
    {
        var follow = FindAnyObjectByType<CinemachineCamera>();
        if (follow == null) return null;
        follow.Target.TrackingTarget = transform.Find(WizardRig.CameraRoot);
        if (follow.TryGetComponent<CameraZoom>(out var zoom)) zoom.controls = controls;
        return follow;
    }
}
