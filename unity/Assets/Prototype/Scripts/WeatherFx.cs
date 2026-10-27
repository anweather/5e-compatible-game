using System.Collections;
using Chem;
using UnityEngine;

// What the weather looks and sounds like around the camera, from the Chem
// world's weather (Chem.Sky): rain falling (slanted by the wind) and snow
// drifting down, both in Punchy's precipitation shader (ThirdParty/Punchy),
// the hiss of rain and the wind's gusts, and for each lightning strike the
// core makes, a bolt, a flash and thunder that comes later the further away
// it struck, a new clap each time (Punchy's ThunderSynth).
// Presentation only: the strikes and what they set alight are the core's.
[RequireComponent(typeof(SkyLight), typeof(CloudSky))]
public class WeatherFx : MonoBehaviour
{
    /// <summary>The rain streaks' material (ChemYardBuilder makes it); no rain is drawn without one.</summary>
    public Material rainMaterial;
    /// <summary>The snowflakes' material; no snow is drawn without one.</summary>
    public Material snowMaterial;
    /// <summary>The lightning bolt's material, bright enough to bloom.</summary>
    public Material boltMaterial;

    const float SpeedOfSound = 343f;

    ChemGrid grid;
    SkyLight skyLight;
    CloudSky clouds;
    ParticleSystem rain, snow;
    readonly System.Random thunderRandom = new();
    AudioSource rainLoop, windLoop;
    int seenTick = -1;
    SpellVisuals.Entry meteorLook;

    // The Meteor spell's look (its flames, blast and dust), from a wizard's spell visuals; a bare one without.
    static SpellVisuals.Entry MeteorLook()
    {
        var caster = FindFirstObjectByType<SpellCaster>();
        var look = caster != null && caster.visuals != null ? caster.visuals.For(SpellIds.Meteor) : null;
        return look ?? new SpellVisuals.Entry { spellId = SpellIds.Meteor, fromSky = true };
    }
    float rainShown, snowShown, windShown;

    void Start()
    {
        grid = FindFirstObjectByType<ChemGrid>();
        skyLight = GetComponent<SkyLight>();
        clouds = GetComponent<CloudSky>();
        if (rainMaterial != null) rain = MakeRain();
        if (snowMaterial != null) snow = MakeSnow();
        rainLoop = MakeLoop("Rain", ProceduralSounds.RainLoop);
        windLoop = MakeLoop("Wind", ProceduralSounds.WindLoop);
    }

    void LateUpdate()
    {
        var world = grid != null ? grid.World : null;
        var cam = Camera.main;
        if (world == null || cam == null) return;
        var now = world.Weather;
        rainShown = Mathf.MoveTowards(rainShown, now.Rain / 100f, Time.deltaTime * 0.25f);
        snowShown = Mathf.MoveTowards(snowShown, now.Snow / 100f, Time.deltaTime * 0.2f);
        windShown = Mathf.MoveTowards(windShown, world.WindDir >= 0 ? 1f : 0f, Time.deltaTime * 0.3f);
        if (rain != null) Rain(cam.transform.position, world.WindDir);
        if (snow != null) Snow(cam.transform.position, world.WindDir);
        rainLoop.volume = 0.22f * rainShown * GameVolume.Ambience;
        windLoop.volume = 0.15f * windShown * GameVolume.Ambience;
        Play(rainLoop, rainShown > 0.01f);
        Play(windLoop, windShown > 0.01f);
        if (world.Tick == seenTick) return;
        seenTick = world.Tick;
        foreach (var (x, y) in world.Strikes) StartCoroutine(Strike(Ground(grid.CellCenter(x, y)), cam.transform.position));
        // A meteor shower's meteors: each falls for the ticks until it lands, from beyond it as seen from here.
        foreach (var (x, y, lands) in world.NewMeteors)
        {
            meteorLook ??= MeteorLook();
            float seconds = (lands - world.Tick) / (float)world.Rules.Sim.TicksPerSecond;
            MeteorFall.Launch(Ground(grid.CellCenter(x, y)), cam.transform.position, meteorLook, null, null, seconds);
        }
    }

    static void Play(AudioSource source, bool on)
    {
        if (on && !source.isPlaying) source.Play();
        else if (!on && source.isPlaying) source.Stop();
    }

    // --- rain -----------------------------------------------------------------

    ParticleSystem MakeRain()
    {
        var go = new GameObject("Rain");
        go.transform.SetParent(transform, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = 1.3f;
        main.startSpeed = 0f;
        main.startSize = 0.022f;
        main.maxParticles = 8000;
        main.playOnAwake = false;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(60f, 1f, 60f);
        var velocity = ps.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.World;
        var emission = ps.emission;
        emission.rateOverTime = 0f;
        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Stretch;
        renderer.velocityScale = 0.045f;
        renderer.lengthScale = 1f;
        // Drops passing right by the lens would fill the screen; keep them streaks.
        renderer.maxParticleSize = 0.006f;
        renderer.sharedMaterial = rainMaterial;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        ps.Play();
        return ps;
    }

    // Falls over the camera, slanting downwind; dimmer at night.
    void Rain(Vector3 camera, int windDir)
    {
        rain.transform.position = camera + Vector3.up * 14f;
        var emission = rain.emission;
        emission.rateOverTime = 5000f * rainShown;
        var velocity = rain.velocityOverLifetime;
        var slant = windDir >= 0 ? Downwind(windDir) * 5f : Vector3.zero;
        velocity.x = slant.x;
        velocity.y = -20f;
        velocity.z = slant.z;
        var main = rain.main;
        float light = skyLight != null ? Mathf.Lerp(0.25f, 1f, skyLight.Daylight) : 1f;
        main.startColor = new Color(0.75f * light, 0.8f * light, 0.9f * light, 0.28f);
    }

    // --- snow ---------------------------------------------------------------------

    ParticleSystem MakeSnow()
    {
        var go = new GameObject("Snow");
        go.transform.SetParent(transform, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = 8f;
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.07f);
        main.startColor = new Color(1f, 1f, 1f, 0.9f);
        main.maxParticles = 12000;
        main.playOnAwake = false;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(45f, 1f, 45f);
        var velocity = ps.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.World;
        // Flakes flutter as they fall.
        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = 0.6f;
        noise.frequency = 0.4f;
        var emission = ps.emission;
        emission.rateOverTime = 0f;
        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sharedMaterial = snowMaterial;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        ps.Play();
        return ps;
    }

    // Drifts down over the camera, carried downwind.
    void Snow(Vector3 camera, int windDir)
    {
        snow.transform.position = camera + Vector3.up * 10f;
        var emission = snow.emission;
        emission.rateOverTime = 1500f * snowShown;
        var velocity = snow.velocityOverLifetime;
        var drift = windDir >= 0 ? Downwind(windDir) * 1.5f : Vector3.zero;
        velocity.x = drift.x;
        velocity.y = -1.4f;
        velocity.z = drift.z;
    }

    /// <summary>The way a world direction (0..3, N E S W; north is -y, so -z in the scene) points.</summary>
    public static Vector3 Downwind(int dir) => dir switch
    {
        0 => Vector3.forward,
        1 => Vector3.right,
        2 => Vector3.back,
        _ => Vector3.left,
    };

    // --- lightning --------------------------------------------------------------

    static Vector3 Ground(Vector3 p) =>
        Physics.Raycast(new Vector3(p.x, 400f, p.z), Vector3.down, out var hit, 800f, ~0, QueryTriggerInteraction.Ignore)
            ? hit.point : p;

    IEnumerator Strike(Vector3 at, Vector3 listener)
    {
        var bolt = boltMaterial != null ? Bolt(at) : null;
        var flash = new GameObject("Lightning flash").AddComponent<Light>();
        flash.type = LightType.Point;
        flash.transform.position = at + Vector3.up * 25f;
        flash.range = 160f;
        flash.color = new Color(0.8f, 0.85f, 1f);
        float distance = Vector3.Distance(at, listener);
        if (skyLight != null) skyLight.Flash(Mathf.Lerp(1f, 0.3f, distance / 700f));
        clouds.Strike(at, 1f);
        StartCoroutine(Thunder(distance));
        for (float t = 0f; t < 0.35f; t += Time.deltaTime)
        {
            // Two quick flickers, then fading.
            float k = 1f - t / 0.35f;
            float flicker = t < 0.06f || (t > 0.12f && t < 0.18f) ? 1f : 0.35f;
            flash.intensity = 40f * k * flicker;
            if (bolt != null) bolt.widthMultiplier = 0.3f * k * flicker;
            yield return null;
        }
        Destroy(flash.gameObject);
        if (bolt != null) Destroy(bolt.gameObject);
    }

    // A jagged line from the clouds down to the strike.
    LineRenderer Bolt(Vector3 at)
    {
        var line = new GameObject("Lightning bolt").AddComponent<LineRenderer>();
        const int points = 16;
        line.positionCount = points;
        line.sharedMaterial = boltMaterial;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        var top = at + new Vector3(Random.Range(-15f, 15f), 120f, Random.Range(-15f, 15f));
        for (int i = 0; i < points; i++)
        {
            float k = i / (points - 1f);
            var p = Vector3.Lerp(top, at, k);
            if (i > 0 && i < points - 1) p += new Vector3(Random.Range(-7f, 7f), Random.Range(-2f, 2f), Random.Range(-7f, 7f));
            line.SetPosition(i, p);
        }
        return line;
    }

    // Thunder arrives at the speed of sound: a new clap for every strike (ThunderSynth), made on a
    // worker thread while the sound travels, a sharp crack close by and a low roll from far off.
    IEnumerator Thunder(float distance)
    {
        int seed = thunderRandom.Next();
        var made = System.Threading.Tasks.Task.Run(() => Punchy.Weather.ThunderSynth.Generate(distance, seed));
        yield return new WaitForSeconds(Mathf.Min(distance / SpeedOfSound, 8f));
        while (!made.IsCompleted) yield return null;
        if (made.IsFaulted) yield break;
        var clip = Punchy.Weather.ThunderSynth.ToClip(made.Result);
        var go = new GameObject("Thunder");
        go.transform.SetParent(transform, false);
        var source = go.AddComponent<AudioSource>();
        source.clip = clip;
        source.spatialBlend = 0f;
        source.volume = Mathf.Lerp(0.7f, 0.2f, distance / 700f) * GameVolume.Ambience;
        source.Play();
        Destroy(go, clip.length + 0.2f);
        Destroy(clip, clip.length + 1f);
    }

    // --- loops ------------------------------------------------------------------

    AudioSource MakeLoop(string name, AudioClip clip)
    {
        var go = new GameObject(name + " loop");
        go.transform.SetParent(transform, false);
        var source = go.AddComponent<AudioSource>();
        source.clip = clip;
        source.loop = true;
        source.spatialBlend = 0f;
        source.volume = 0f;
        source.playOnAwake = false;
        return source;
    }
}
