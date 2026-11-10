using Punchy.Fauna;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// What a camera can see of the valley's animals (ValleyFauna): whether one, or the shadow it casts, may be in the view,
// so only those are posed and drawn. Generous on purpose: an animal is a sphere well round its body, and its shadow the
// ground from it to where the sun or moon throws its top, out to the shadows' distance. Near the lens every animal counts
// (CreatureDrawer.HideAtLens needs it posed). Not a component: ValleyFauna keeps one and calls From each frame.
public sealed class CreatureView
{
    // Round an animal's body, beyond its length and height: a raised head, a tail, a step taken this frame.
    const float Margin = 1.5f;
    // Within this of the lens every animal is posed (CreatureDrawer's TooFar is 12 m).
    const float NearLens = 15f;
    // The longest shadow looked for: a low sun's runs on, but past this it's a faint smear at most.
    const float LongestShadow = 50f;
    // URP's own shadow distance when the asset can't be read.
    const float FallbackShadowDistance = 150f;

    readonly Plane[] planes = new Plane[6];
    // Where an animal's top's shadow falls, per metre of its height, for each light casting shadows (the sun, the moon).
    readonly Vector3[] throws = new Vector3[2];
    int lights;
    Vector3 lens;
    float shadowDistance;

    /// <summary>Every animal posed, seen or not: for a still to hold against the culled one (the probe, a script eval).</summary>
    public static bool PoseAll { get; set; }

    // Each Play mode starts culling, with the domain reload off too.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => PoseAll = false;

    /// <summary>Takes the camera's view and the lights' angles for this frame.</summary>
    public void From(Camera cam, SkyLight sky)
    {
        GeometryUtility.CalculateFrustumPlanes(cam, planes);
        lens = cam.transform.position;
        shadowDistance = GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp ? urp.shadowDistance : FallbackShadowDistance;
        lights = 0;
        if (sky == null) return;
        Add(sky.sun);
        Add(sky.moon);
    }

    void Add(Light light)
    {
        if (light == null || !light.isActiveAndEnabled || light.shadows == LightShadows.None || light.intensity <= 0f) return;
        var d = light.transform.forward;
        if (d.y >= -0.01f) return; // from below the ground: its shadows fall nowhere
        throws[lights++] = new Vector3(d.x, 0f, d.z) / -d.y;
    }

    /// <summary>Whether an animal of this body and scale at this point may show, or its shadow may.</summary>
    public bool Shows(float3 position, CreatureBody body, float scale)
    {
        float height = (body.StandHeight * 2f + body.BodyLength) * scale + Margin;
        var middle = new Vector3(position.x, position.y + body.StandHeight * scale, position.z);
        float away = Vector3.Distance(middle, lens);
        if (PoseAll || away <= NearLens || InView(middle, height)) return true;
        for (int i = 0; i < lights; i++)
        {
            var shadow = Vector3.ClampMagnitude(throws[i] * height, LongestShadow);
            float length = shadow.magnitude;
            if (away - length - height > shadowDistance) continue; // drawn beyond the shadows' distance
            if (InView(middle + shadow * 0.5f, height + length * 0.5f)) return true;
        }
        return false;
    }

    bool InView(Vector3 centre, float radius)
    {
        for (int i = 0; i < planes.Length; i++)
            if (planes[i].GetDistanceToPoint(centre) < -radius) return false;
        return true;
    }
}
