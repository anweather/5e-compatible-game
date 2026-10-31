using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Unity.Profiling;
using Unity.Profiling.LowLevel;
using Unity.Profiling.LowLevel.Unsafe;

// The main Profiler markers read every frame for PerfScenario, through
// ProfilerRecorder: Unity's own (the player loop, script updates, camera
// rendering, garbage collection) and ours (ChemGrid, ValleyGround, ValleyFauna,
// RobeCloth, the playtest bot). A recorder only finds a marker in a development
// build; in any other it reads nothing, and Missing says which those are.
// Sample keeps each marker's sum and peak for the run; Breakdown writes one
// frame's slow markers, for a line about a slow frame. A wide reader (-perf-wide)
// also watches every timed marker of the main thread, for a hitch nothing else
// names: costlier, and its markers overlap (a parent and its children all show).
public sealed class PerfMarkers : IDisposable
{
    /// <summary>The bot's own screenshot: its frames aren't the game's.</summary>
    const string BotShot = "PlaytestBot.Shot";
    const string Collect = "GC.Collect";
    const double NsPerMs = 1e6;
    /// <summary>A marker under this in a frame stays out of its breakdown.</summary>
    const double BreakdownFloorMs = 0.5, WideFloorMs = 2.0;

    static readonly (ProfilerCategory category, string[] names)[] Timed =
    {
        (ProfilerCategory.Internal, new[]
        {
            "PlayerLoop", "BehaviourUpdate", "LateBehaviourUpdate", "FixedBehaviourUpdate", "CoroutinesDelayedCalls",
            "Terrain.Details.Render", "Physics.TerrainCollider.ForceRebuildCollisions",
            "FixedUpdate.PhysicsFixedUpdate", "Physics.Simulate", "Physics.SyncTransforms",
            "Instantiate", "Loading.ReadObject", "Loading.AwakeFromLoad", "Mesh.UploadMeshData",
            "Shader.CreateGPUProgram", "CreateGraphicsPipelineAsync", "Semaphore.WaitForSignal",
            "Terrain.UpdateHeightmapTexture", "Terrain.RecomputeInvalidPatches",
            "Update.DirectorUpdate", "PostLateUpdate.UpdateAllRenderers", "PostLateUpdate.UpdateAllSkinnedMeshes", "PostLateUpdate.UpdateAudio",
            "ParticleSystem.Update", "Gfx.PresentFrame", "Gfx.WaitForPresentOnGfxThread", "Gfx.WaitForGfxCommandsFromMainThread",
            // Camera.Render stays 0 under URP: FinishFrameRendering holds the camera's work.
            "PostLateUpdate.FinishFrameRendering", "Camera.Render", "Inl_RenderCameraStack", "CullScriptable",
            Collect,
        }),
        (ProfilerCategory.Scripts, new[]
        {
            "ChemGrid.Step", "ChemGrid.Render", "ChemGrid.ChangeGround", "ChemGrid.RenderWater", "ChemGrid.RenderFire",
            "ChemGrid.RenderGas", "ChemGrid.FinishRender", "ChemGrid.WaterMesh", "ChemGrid.SlickMesh", "ChemGrid.Shaping", "ChemGrid.RecycleFx",
            "ValleyGround.Flush", "ValleyGround.Paint", "ValleyGround.ClearGrass", "ValleyGround.UploadPaint",
            "ValleyFauna.Update", "ValleyFauna.Think", "ValleyFauna.Draw",
            "RobeCloth.Step", "PlaytestBot.LateUpdate", BotShot,
            "ChemGrid.ChunkPlan", "ChemGrid.ChunkApply", "ChemGrid.ChunkEvent", "ChemGrid.DetailChunks",
            "ThingSpawner.SpawnChunk", "ThingSpawner.UnloadChunk", "ValleyGround.DrawDetails",
            "ValleyGround.HeightUpdate", "ValleyGround.HeightEase", "ValleyGround.HeightWrite",
            "Water.Recompute", "Water.ComputeTops", "Water.FindBanks", "Water.DrawTile",
            "Contour.Tile.Outline", "Contour.Tile.Points", "Contour.Tile.Upload",
            "Contour.Film.Outline", "Contour.Film.Points", "Contour.Film.Upload",
            "FogBank.LateUpdate", "FogBank.MakePuffs", "FlameBank.Write", "FlameBank.LateUpdate",
            "SpellCaster.ShowAim", "AimMarker.Redraw",
        }),
    };

    readonly List<string> names = new();
    readonly List<ProfilerRecorder> recorders = new();
    readonly double[] sum, peak, last;
    readonly int shotAt;
    readonly double floorMs;
    ProfilerRecorder allocated = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
    ProfilerRecorder systemUsed = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "System Used Memory");
    ProfilerRecorder gfxUsed = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "Gfx Used Memory");
    long allocatedSum, allocatedPeak;
    int frames, collections;

    public PerfMarkers(bool wide = false)
    {
        floorMs = wide ? WideFloorMs : BreakdownFloorMs;
        foreach (var (category, group) in Timed)
            foreach (string name in group)
            {
                names.Add(name);
                recorders.Add(ProfilerRecorder.StartNew(category, name));
            }
        if (wide) WatchEverything();
        sum = new double[names.Count];
        peak = new double[names.Count];
        last = new double[names.Count];
        shotAt = names.IndexOf(BotShot);
    }

    // Every timed marker not read already, on this thread alone (a recorder sums all threads otherwise).
    void WatchEverything()
    {
        var handles = new List<ProfilerRecorderHandle>();
        ProfilerRecorderHandle.GetAvailable(handles);
        var known = new HashSet<string>(names);
        foreach (var handle in handles)
        {
            var description = ProfilerRecorderHandle.GetDescription(handle);
            if (description.UnitType != ProfilerMarkerDataUnit.TimeNanoseconds || !known.Add(description.Name)) continue;
            names.Add(description.Name);
            recorders.Add(new ProfilerRecorder(handle, 1, ProfilerRecorderOptions.Default | ProfilerRecorderOptions.CollectOnlyOnCurrentThread));
        }
    }

    /// <summary>The last frame's garbage, in bytes (0 outside a development build).</summary>
    public long AllocatedLastFrame => allocated.Valid ? allocated.LastValue : 0;

    /// <summary>Whether the last frame held the bot's own screenshot.</summary>
    public bool BotShotLastFrame => last[shotAt] > 0;

    /// <summary>The process's memory as Unity's counters see it, in bytes (0 outside a development build).</summary>
    public long SystemUsedBytes => systemUsed.Valid ? systemUsed.LastValue : 0;
    public long GfxUsedBytes => gfxUsed.Valid ? gfxUsed.LastValue : 0;

    /// <summary>The markers no recorder found.</summary>
    public IEnumerable<string> Missing()
    {
        for (int i = 0; i < names.Count; i++)
            if (!recorders[i].Valid) yield return names[i];
    }

    /// <summary>Reads the frame that just ended. Call once a frame.</summary>
    public void Sample()
    {
        frames++;
        long garbage = AllocatedLastFrame;
        allocatedSum += garbage;
        allocatedPeak = Math.Max(allocatedPeak, garbage);
        for (int i = 0; i < recorders.Count; i++)
        {
            double ms = recorders[i].Valid ? recorders[i].LastValue / NsPerMs : 0;
            last[i] = ms;
            sum[i] += ms;
            peak[i] = Math.Max(peak[i], ms);
            if (ms > 0 && names[i] == Collect) collections++;
        }
    }

    /// <summary>The last frame's markers of half a millisecond or more, as name=ms pairs, then its garbage.</summary>
    public string Breakdown()
    {
        var line = new StringBuilder();
        for (int i = 0; i < names.Count; i++)
            if (last[i] >= floorMs) line.Append(' ').Append(names[i]).Append('=').Append(last[i].ToString("0.0", CultureInfo.InvariantCulture));
        line.Append(" gc_alloc_kb=").Append((AllocatedLastFrame / 1024).ToString(CultureInfo.InvariantCulture));
        return line.ToString();
    }

    /// <summary>Each marker's mean and peak over the run, as name=mean/peak pairs in ms, then the garbage.</summary>
    public string Summary()
    {
        var line = new StringBuilder();
        var inv = CultureInfo.InvariantCulture;
        for (int i = 0; i < names.Count; i++)
            if (peak[i] > 0) line.Append(' ').Append(names[i]).Append('=').Append((sum[i] / frames).ToString("0.00", inv)).Append('/').Append(peak[i].ToString("0.0", inv));
        line.Append(" gc_alloc_kb_mean=").Append((allocatedSum / 1024.0 / Math.Max(1, frames)).ToString("0.0", inv));
        line.Append(" gc_alloc_kb_peak=").Append((allocatedPeak / 1024).ToString(inv));
        line.Append(" gc_collect_frames=").Append(collections.ToString(inv));
        return line.ToString();
    }

    public void Dispose()
    {
        foreach (var recorder in recorders) recorder.Dispose();
        allocated.Dispose();
        systemUsed.Dispose();
        gfxUsed.Dispose();
    }
}
