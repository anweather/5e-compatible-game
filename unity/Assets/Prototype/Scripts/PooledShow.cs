using UnityEngine;

// One copy of a spell show that ShowPool keeps: the look it was made as, the size, particle speeds
// and light ranges it had before anything scaled it (so each take starts from those), and when it
// goes back to the pool. Presentation only.
public class PooledShow : MonoBehaviour
{
    public ShowPool.Look Look { get; private set; }

    ParticleSystem[] systems;
    float[] speeds;
    ParticleSystemScalingMode[] modes;
    Light[] lights;
    float[] ranges;
    Vector3 size;
    float backAt = float.MaxValue;

    public void Keep(ShowPool.Look look)
    {
        Look = look;
        size = transform.localScale;
        systems = GetComponentsInChildren<ParticleSystem>(true);
        speeds = new float[systems.Length];
        modes = new ParticleSystemScalingMode[systems.Length];
        for (int i = 0; i < systems.Length; i++)
        {
            var main = systems[i].main;
            speeds[i] = main.startSpeedMultiplier;
            modes[i] = main.scalingMode;
        }
        lights = GetComponentsInChildren<Light>(true);
        ranges = new float[lights.Length];
        for (int i = 0; i < lights.Length; i++) ranges[i] = lights[i].range;
    }

    public void Unscale()
    {
        transform.localScale = size;
        for (int i = 0; i < systems.Length; i++)
        {
            var main = systems[i].main;
            main.startSpeedMultiplier = speeds[i];
            main.scalingMode = modes[i];
        }
        for (int i = 0; i < lights.Length; i++) lights[i].range = ranges[i];
    }

    public void Play()
    {
        backAt = float.MaxValue;
        foreach (var ps in systems)
            if (ps.main.playOnAwake && !ps.isPlaying) ps.Play(false);
    }

    public void StopAndClear()
    {
        backAt = float.MaxValue;
        foreach (var ps in systems) ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    public void BackIn(float seconds) => backAt = Time.time + seconds;

    void Update()
    {
        if (Time.time < backAt) return;
        backAt = float.MaxValue;
        ShowPool.Back(this);
    }
}
