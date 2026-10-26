using System.Collections.Generic;
using Chem;
using UnityEngine;

// A fixed pool of flickering point lights spread over the biggest fires.
// Hundreds of cells can burn at once and every real-time light costs a lot,
// so the lights are made once and moved, never created per fire. Each light
// also carries the crackle of the fire it sits on, so the sound of a fire
// comes from the same few places as its light.
public sealed class FireLightPool
{
    readonly ChemGrid grid;
    readonly List<Light> lights = new();
    readonly List<AudioSource> crackles = new();
    readonly List<(int x, int y, int fire)> burning = new();

    public FireLightPool(ChemGrid grid, int size)
    {
        this.grid = grid;
        for (int i = 0; i < size; i++)
        {
            var l = new GameObject("Fire light").AddComponent<Light>();
            l.transform.SetParent(grid.transform, false);
            l.type = LightType.Point;
            l.color = new Color(1f, 0.55f, 0.22f);
            l.range = 7f;
            l.shadows = LightShadows.None;
            l.enabled = false;
            lights.Add(l);
            crackles.Add(GameSounds.Loop(SoundIds.FireLoop, l.transform, 0f));
        }
    }

    /// <summary>How many cells burned at the last Place (the simulated chunks').</summary>
    public int BurningCount => burning.Count;

    /// <summary>Moves the lights to the biggest fires, at least two cells apart; call after the world steps.</summary>
    public void Place()
    {
        CollectBurning();
        int used = 0;
        foreach (var b in burning)
        {
            if (used >= lights.Count) break;
            if (NearLitFire(b.x, b.y, used)) continue;
            var l = lights[used];
            l.transform.position = grid.CellCenter(b.x, b.y) + Vector3.up * (grid.SurfaceY(b.x, b.y) + 1f);
            l.intensity = 1.5f + b.fire;
            l.enabled = true;
            SetCrackle(used, 0.55f + 0.15f * b.fire);
            used++;
        }
        for (int i = used; i < lights.Count; i++)
        {
            lights[i].enabled = false;
            SetCrackle(i, 0f);
        }
    }

    // A fire's crackle: playing (at this volume) while its light is lit, stopped otherwise.
    void SetCrackle(int i, float volume)
    {
        var s = crackles[i];
        s.volume = volume * GameVolume.Effects;
        if (volume > 0f && !s.isPlaying) s.Play();
        else if (volume <= 0f && s.isPlaying) s.Stop();
    }

    /// <summary>Wavers the lit lights' range; call every frame.</summary>
    public void Flicker()
    {
        for (int i = 0; i < lights.Count; i++)
        {
            var l = lights[i];
            if (!l.enabled) continue;
            l.range = 6f + 1.5f * Mathf.PerlinNoise(Time.time * 6f, i * 7.3f);
        }
    }

    // Every burning cell of the simulated chunks (a sleeping chunk's fire is paused), biggest fire first.
    void CollectBurning()
    {
        var world = grid.World;
        var cells = world.Cells;
        burning.Clear();
        foreach (var (cx, cy) in world.ActiveChunks)
        {
            int x0 = cx << Chunks.Shift, y0 = cy << Chunks.Shift;
            int x1 = Mathf.Min(world.W, x0 + Chunks.Size), y1 = Mathf.Min(world.H, y0 + Chunks.Size);
            for (int y = y0; y < y1; y++)
            {
                for (int x = x0; x < x1; x++)
                {
                    int f = cells[y * world.W + x].Fire;
                    if (f > 0) burning.Add((x, y, f));
                }
            }
        }
        burning.Sort((a, b) => b.fire.CompareTo(a.fire));
    }

    // Whether one of the first `used` lights is already within two cells.
    bool NearLitFire(int x, int y, int used)
    {
        for (int i = 0; i < used; i++)
        {
            var p = grid.CellAt(lights[i].transform.position);
            if (Mathf.Abs(p.x - x) <= 2 && Mathf.Abs(p.y - y) <= 2) return true;
        }
        return false;
    }
}
