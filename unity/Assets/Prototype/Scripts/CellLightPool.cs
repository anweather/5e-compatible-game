using System.Collections.Generic;
using Chem;
using UnityEngine;

// The Light spell's glow (Cell.Lit): a fixed pool of warm point lights, each
// with a small bright mote over its cell, made once and moved. Lit cells can
// outnumber the pool; the first ones lit keep theirs. Only the show: the cell's
// timer is the core's.
public sealed class CellLightPool
{
    const float Range = 12f, Intensity = 3.5f, MoteSize = 0.2f, MoteHeight = 0.8f;
    static readonly Color Warm = new(1f, 0.92f, 0.76f);

    readonly ChemGrid grid;
    readonly List<(Light light, GameObject mote)> pool = new();
    readonly Dictionary<int, float> lit = new();
    readonly Material material;
    bool dirty;

    public CellLightPool(ChemGrid grid, int size)
    {
        this.grid = grid;
        material = UrpMaterials.NewLit("Light mote", Warm, 0f, Warm * 5f);
        for (int i = 0; i < size; i++)
        {
            var l = new GameObject("Spell light").AddComponent<Light>();
            l.transform.SetParent(grid.transform, false);
            l.type = LightType.Point;
            l.color = Warm;
            l.range = Range;
            l.intensity = Intensity;
            l.shadows = LightShadows.None;
            l.enabled = false;
            var mote = Primitives.Visual(PrimitiveType.Sphere, "Spell light mote");
            mote.transform.SetParent(l.transform, false);
            mote.transform.localScale = Vector3.one * MoteSize;
            var r = mote.GetComponent<Renderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mote.SetActive(false);
            pool.Add((l, mote));
        }
    }

    /// <summary>How many lights are on, for the probe.</summary>
    public int Shown { get; private set; }

    /// <summary>A cell was redrawn, with its ground at <paramref name="top"/>.</summary>
    public void Changed(int x, int y, Cell c, float top)
    {
        int key = grid.World.Idx(x, y);
        if (c.Lit > 0)
        {
            dirty |= !lit.TryGetValue(key, out float was) || !Mathf.Approximately(was, top);
            lit[key] = top;
        }
        else dirty |= lit.Remove(key);
    }

    /// <summary>Moves the lights if a lit cell came or went.</summary>
    public void Place()
    {
        if (!dirty) return;
        dirty = false;
        Shown = 0;
        foreach (var kv in lit)
        {
            if (Shown == pool.Count) break;
            var at = grid.CellCenter(kv.Key % grid.World.W, kv.Key / grid.World.W);
            var (light, mote) = pool[Shown++];
            light.transform.position = new Vector3(at.x, kv.Value + MoteHeight, at.z);
            light.enabled = true;
            mote.SetActive(true);
        }
        for (int i = Shown; i < pool.Count; i++)
        {
            pool[i].light.enabled = false;
            pool[i].mote.SetActive(false);
        }
    }

    public void Destroy()
    {
        foreach (var (light, _) in pool)
            if (light != null) Object.Destroy(light.gameObject);
        Object.Destroy(material);
    }
}
