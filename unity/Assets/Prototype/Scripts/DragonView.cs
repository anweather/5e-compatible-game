using Chem;
using UnityEngine;

// The Dragon as the player sees it (docs/BACKLOG.md 26): its model, flight, breath and
// shadow, and its horn and roar, drawn from the core's flight (World.Dragon, BreathBox)
// and nothing else, so every peer sees the same one. Nothing here decides anything: the
// flight's where and when is DragonFlight's sum on the core's state, smoothed a little
// because the sim runs at 6 ticks a second. The sky reads the same flight (SkyLight, CloudSky),
// and what the breath resets is drawn by ChemGrid.ChunkFreshened's listeners.
//
// The model is Quaternius's (ThirdParty/Quaternius, CC0), with only flying, attacks, a
// hit and a death to play: it's red by our materials, and it roars with an attack.
// How big it's drawn is sim.json's dragon.scale, looked at every frame so a mod takes hold at once.
public class DragonView : MonoBehaviour
{
    public ChemGrid grid;
    [Tooltip("The dragon's model (ThirdParty/Quaternius/Dragon.fbx); the builder sets it.")]
    public GameObject model;
    [Tooltip("Draws its shadow (Prototype/DragonShadow); the builder sets it, which puts it in the build.")]
    public Shader shadowShader;

    const float Follow = 0.1f, GroundFollow = 0.7f, Turn = 2.5f, MostBank = 35f;
    const float Level = 40f;
    const float MouthAhead = 26f, MouthBelow = 2f;
    const string Nose = "Nose_end";
    const string Flying = "DragonArmature|Dragon_Flying", Attack = "DragonArmature|Dragon_Attack";
    static readonly (string name, Color color, float smooth)[] Paints =
    {
        ("Main", new Color(0.46f, 0.045f, 0.035f), 0.3f),
        ("Belly", new Color(0.62f, 0.36f, 0.12f), 0.2f),
        ("Wings", new Color(0.22f, 0.025f, 0.025f), 0.15f),
        ("Claws", new Color(0.05f, 0.04f, 0.04f), 0.4f),
        ("Eyes", new Color(0.9f, 0.55f, 0.08f), 0.6f),
    };
    static readonly Unity.Profiling.ProfilerMarker UpdateMarker = new("DragonView.LateUpdate");

    Transform body, nose;
    Renderer[] skins;
    float unitScale = 1f, shownScale;
    Animation clips;
    DragonBreath breath;
    DragonShadow shadow;
    Vector3 shownAt, velocity;
    Vector3 way = Vector3.forward;
    float groundUnder, groundSpeed, bank, rear;
    int lastTick = -1;
    float tickAt, secondsPerTick = 1f / 6f;
    bool wasSeen;
    DragonPhase phase;
    int flightKnown = -1;
    Vector2 perch;
    bool roared;
    Vector3 cellOrigin, cellX, cellY;

    public bool Seen => wasSeen;

    public Vector3 ShownAt => shownAt;

    public bool Perched { get; private set; }

    public Vector3 Way => way;

    public DragonBreath Breath => breath;

    public float DrawnScale => body != null ? body.localScale.x / unitScale : 0f;

    public float ShadowSize { get; private set; }

    public Vector3 Mouth => nose != null ? nose.position : shownAt + way * (MouthAhead * shownScale) + Vector3.down * (MouthBelow * shownScale);

    public Bounds Extent
    {
        get
        {
            var all = skins[0].bounds;
            foreach (var r in skins) all.Encapsulate(r.bounds);
            return all;
        }
    }

    void Start()
    {
        if (grid == null) grid = ChemGrid.Current?.Grid;
        if (grid == null || model == null)
        {
            if (grid != null) Debug.LogWarning("DragonView has no model: rebuild the yard (Prototype > Build Chemistry Yard)");
            enabled = false;
            return;
        }
        cellOrigin = grid.CellCenter(0, 0);
        cellX = grid.CellCenter(1, 0) - cellOrigin;
        cellY = grid.CellCenter(0, 1) - cellOrigin;
        BuildBody();
        breath = DragonBreath.On(transform, grid);
        var shape = shadowShader != null ? shadowShader : Shader.Find("Prototype/DragonShadow");
        if (shape != null) shadow = DragonShadow.On(transform, grid, shape);
    }

    void BuildBody()
    {
        body = new GameObject("Dragon").transform;
        body.SetParent(transform, false);
        var go = Instantiate(model, body);
        go.transform.localRotation = Quaternion.Euler(Level, 0f, 0f) * go.transform.localRotation;
        foreach (var r in go.GetComponentsInChildren<Renderer>())
        {
            var paints = r.sharedMaterials;
            for (int i = 0; i < paints.Length; i++) paints[i] = PaintFor(paints[i]);
            r.sharedMaterials = paints;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            if (r is SkinnedMeshRenderer skin) skin.updateWhenOffscreen = true;
        }
        var bounds = new Bounds(body.position, Vector3.zero);
        foreach (var r in go.GetComponentsInChildren<Renderer>()) bounds.Encapsulate(r.bounds);
        unitScale = DragonFlight.SpanAtOne / Mathf.Max(0.01f, Mathf.Max(bounds.size.x, bounds.size.z));
        skins = go.GetComponentsInChildren<Renderer>();
        nose = System.Array.Find(go.GetComponentsInChildren<Transform>(), t => t.name == Nose);
        clips = go.GetComponentInChildren<Animation>();
        if (clips != null)
        {
            clips.cullingType = AnimationCullingType.AlwaysAnimate;
            foreach (AnimationState s in clips) s.wrapMode = WrapMode.Loop;
            if (clips[Attack] != null) clips[Attack].wrapMode = WrapMode.ClampForever;
            if (clips[Flying] != null) clips.Play(Flying);
        }
        body.gameObject.SetActive(false);
    }

    static Material PaintFor(Material was)
    {
        foreach (var (name, color, smooth) in Paints)
            if (was != null && was.name.StartsWith(name, System.StringComparison.Ordinal)) return UrpMaterials.NewLit("Dragon " + name, color, smooth);
        return UrpMaterials.NewLit("Dragon", Paints[0].color, Paints[0].smooth);
    }

    void LateUpdate()
    {
        using var marked = UpdateMarker.Auto();
        var world = grid.World;
        var flight = world?.Dragon;
        if (flight == null)
        {
            Vanish();
            return;
        }
        Cues(world, flight);
        var pose = DragonFlight.At(world, TickFraction(world), PerchFor(world));
        if (!pose.Seen)
        {
            Vanish();
            return;
        }
        var p = world.Rules.Sim.Dragon;
        Resize((float)p.Scale);
        Fly(pose);
        Breathe(world, pose);
        LayShadow(p, pose);
    }

    void Resize(float scale)
    {
        if (Mathf.Approximately(scale, shownScale)) return;
        shownScale = scale;
        body.localScale = Vector3.one * (unitScale * scale);
    }

    float TickFraction(World world)
    {
        if (world.Tick != lastTick)
        {
            if (lastTick >= 0 && world.Tick == lastTick + 1) secondsPerTick = Mathf.Lerp(secondsPerTick, Mathf.Max(0.02f, Time.time - tickAt), 0.1f);
            lastTick = world.Tick;
            tickAt = Time.time;
        }
        return Mathf.Clamp01((Time.time - tickAt) / secondsPerTick);
    }

    void Vanish()
    {
        if (body != null && body.gameObject.activeSelf) body.gameObject.SetActive(false);
        wasSeen = false;
        if (breath != null) breath.Stop();
        if (shadow != null) shadow.Hide();
    }

    Vector2 PerchFor(World world)
    {
        if (flightKnown == world.DragonFlights) return perch;
        flightKnown = world.DragonFlights;
        perch = new Vector2(world.W / 2f, world.H / 2f);
        if (grid.valley == null) return perch;
        float best = float.NegativeInfinity;
        const int Step = 8;
        for (int y = Step; y < world.H - Step; y += Step)
            for (int x = Step; x < world.W - Step; x += Step)
            {
                float h = grid.valley.BaseHeight(x, y);
                if (h <= best) continue;
                best = h;
                perch = new Vector2(x, y);
            }
        return perch;
    }

    Vector3 Scene(Vector2 cell) => cellOrigin + cellX * cell.x + cellY * cell.y;

    void Fly(DragonFlight.Pose pose)
    {
        var at = Scene(pose.Cell);
        float ground = GroundUnderAll(at, pose);
        if (!wasSeen)
        {
            groundUnder = ground;
            shownAt = at + Vector3.up * (ground + pose.Lift);
            velocity = Vector3.zero;
            groundSpeed = 0f;
            body.gameObject.SetActive(true);
            wasSeen = true;
        }
        groundUnder = ground > groundUnder ? Mathf.SmoothDamp(groundUnder, ground, ref groundSpeed, GroundFollow * 0.5f) : Mathf.SmoothDamp(groundUnder, ground, ref groundSpeed, GroundFollow * 2f);
        var target = at + Vector3.up * (groundUnder + pose.Lift);
        var was = shownAt;
        shownAt = Vector3.SmoothDamp(shownAt, target, ref velocity, Follow);
        var moved = shownAt - was;
        if (moved.sqrMagnitude > 0.01f && !pose.Perched) way = Vector3.Slerp(way, moved.normalized, 1f - Mathf.Exp(-Time.deltaTime * Turn));
        float flat = Vector3.SignedAngle(Vector3.ProjectOnPlane(way, Vector3.up), Vector3.ProjectOnPlane(moved, Vector3.up), Vector3.up);
        bank = Mathf.Lerp(bank, Mathf.Clamp(-flat * 4f, -MostBank, MostBank), 1f - Mathf.Exp(-Time.deltaTime * Turn));
        rear = Mathf.Lerp(rear, pose.Perched ? Level : 0f, 1f - Mathf.Exp(-Time.deltaTime * Turn));
        body.SetPositionAndRotation(shownAt, Quaternion.LookRotation(way, Vector3.up) * Quaternion.Euler(-rear, 0f, bank + pose.Roll));
        Perched = pose.Perched;
    }

    float GroundUnderAll(Vector3 at, DragonFlight.Pose pose)
    {
        float span = DragonFlight.SpanAtOne * shownScale;
        var ahead = Vector3.ProjectOnPlane(way, Vector3.up).normalized * (span * Mathf.Clamp01(pose.Lift / (DragonFlight.CruiseAtOne * shownScale)));
        var tip = Vector3.Cross(Vector3.up, ahead) * 0.5f;
        float ground = Mathf.Max(Ground(at), Mathf.Max(Ground(at + tip), Ground(at - tip)));
        return Mathf.Max(ground, Mathf.Max(Ground(at + ahead), Mathf.Max(Ground(at + ahead + tip), Ground(at + ahead - tip))));
    }

    float Ground(Vector3 at)
    {
        float ground = grid.GroundAt(at);
        return grid.valley != null ? Mathf.Max(ground, grid.valley.LakeLevel) : ground;
    }

    void Breathe(World world, DragonFlight.Pose pose)
    {
        if (breath == null) return;
        var flight = world.Dragon;
        if (!world.BreathBox(out int x0, out int y0, out int x1, out int y1))
        {
            breath.Stop();
            return;
        }
        var (a, b) = flight.AlongX
            ? (new Vector2(pose.Lead, y0), new Vector2(pose.Lead, y1))
            : (new Vector2(x0, pose.Lead), new Vector2(x1, pose.Lead));
        var flatWay = Vector3.ProjectOnPlane(way, Vector3.up).normalized;
        breath.Burn(Scene(a), Scene(b), -flatWay, Mouth, shownScale);
    }

    void LayShadow(DragonParams p, DragonFlight.Pose pose)
    {
        if (shadow == null) return;
        if (pose.Lift >= DragonFlight.Clouds * 0.8f)
        {
            shadow.Hide();
            return;
        }
        float high = Mathf.InverseLerp(0f, DragonFlight.High, pose.Lift);
        ShadowSize = DragonFlight.Wingspan(p) * Mathf.Lerp(1.2f, 2.6f, high);
        shadow.Lay(new Vector3(shownAt.x, 0f, shownAt.z), way, ShadowSize, Mathf.Lerp(0.7f, 0.5f, high));
    }

    void Cues(World world, DragonState flight)
    {
        var eye = Camera.main;
        var near = eye != null ? eye.transform.position : shownAt;
        if (phase != flight.Phase)
        {
            if (flight.Phase == DragonPhase.Warning) GameSounds.Play(SoundIds.Horn, near, 1f, 0f);
            if (flight.Phase == DragonPhase.Passes) GameSounds.Play(SoundIds.Roar, near, 1f, 0.05f);
            phase = flight.Phase;
            roared = false;
        }
        if (flight.Phase != DragonPhase.Leaving || roared) return;
        if (!DragonFlight.At(world, 0f, perch).Perched) return;
        roared = true;
        GameSounds.Play(SoundIds.Roar, near, 1f, 0.05f);
        if (clips != null && clips[Attack] != null)
        {
            clips.CrossFade(Attack, 0.3f);
            clips.CrossFadeQueued(Flying, 0.5f, QueueMode.CompleteOthers);
        }
    }
}
