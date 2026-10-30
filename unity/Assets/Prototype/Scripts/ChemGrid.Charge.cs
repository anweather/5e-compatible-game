using System.Collections.Generic;
using Chem;
using UnityEngine;

// A charge running through what conducts, and a storm cloud's strikes, drawn with
// arcs (ArcFx). The core charges a whole pond in one tick; here the cells it
// charged are sampled and joined up from where it struck, each arc a moment after
// the one before, so the charge is seen to run out through the water and on to a
// coil. A strike is one bolt from under its cloud, sparks and a crack of thunder.
public partial class ChemGrid
{
    // Arcs for one charge, how fast it runs (m/s), and how far over the cell it runs.
    const int MostChargeArcs = 22;
    const float ChargeSpeed = 16f;
    const float ChargeLift = 0.18f;

    readonly List<Vector3> charging = new();
    readonly List<Vector3> joined = new();
    readonly List<Vector3> coilsCharging = new();
    Vector3? chargeFrom;
    int shedSeen = -1;
    readonly HashSet<(int x, int y, int tick)> strikesShown = new();

    /// <summary>Where the next charge starts from: where a bolt just struck.</summary>
    public void ChargeStartsAt(Vector3 at) => chargeFrom = at;

    // A cell the charge has just reached (RenderCharge).
    void NoteCharged(Cell c, Vector3 at)
    {
        at.y += ChargeLift;
        charging.Add(at);
        if (c.T == TerrainIds.Coil) coilsCharging.Add(at);
    }

    // The cells charged this frame, joined up from the strike: each sample to the nearest one before it.
    void RenderChargeRun()
    {
        if (charging.Count == 0) return;
        var from = chargeFrom ?? charging[0];
        chargeFrom = null;
        charging.Sort((a, b) => (a - from).sqrMagnitude.CompareTo((b - from).sqrMagnitude));
        joined.Clear();
        joined.Add(from);
        int every = Mathf.Max(1, charging.Count / MostChargeArcs);
        for (int i = 0; i < charging.Count; i++)
        {
            if (i % every != 0 && !coilsCharging.Contains(charging[i])) continue;
            var at = charging[i];
            var near = from;
            foreach (var p in joined)
                if ((p - at).sqrMagnitude < (near - at).sqrMagnitude) near = p;
            joined.Add(at);
            ArcFx.Play(near, at, 0.05f, Vector3.Distance(from, near) / ChargeSpeed, 0.4f);
        }
        foreach (var coil in coilsCharging)
        {
            float delay = Vector3.Distance(from, coil) / ChargeSpeed;
            for (int i = 0; i < 4; i++)
                ArcFx.Play(coil + Vector3.up * 1.5f, coil + new Vector3(Random.Range(-0.9f, 0.9f), 0.1f, Random.Range(-0.9f, 0.9f)), 0.05f, delay + i * 0.05f, 0.5f);
        }
        charging.Clear();
        coilsCharging.Clear();
    }

    // A storm cloud strikes: a bolt from under it to the ground, sparks and thunder.
    void RenderShedStrikes()
    {
        // From the tick before too, as a flash (RenderFlashes).
        foreach (var (x, y, tick) in World.ShedStrikesSince(shedSeen - 1))
        {
            if (!strikesShown.Add((x, y, tick))) continue;
            var c = World.At(x, y);
            var at = CellCenter(x, y);
            at.y = GroundTop(x, y, c);
            float top = FogBank.CloudHeight * cellSize;
            ArcFx.Play(at + new Vector3(Random.Range(-0.8f, 0.8f), top, Random.Range(-0.8f, 0.8f)), at, 0.16f, 0f, 0.3f);
            Spark(at + Vector3.up * 0.1f);
            GameSounds.Play(SoundIds.Zap, at);
        }
        shedSeen = World.Tick;
        strikesShown.RemoveWhere(s => s.tick < shedSeen - 1);
    }
}
