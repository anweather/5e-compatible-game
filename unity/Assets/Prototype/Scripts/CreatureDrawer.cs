using System.Runtime.InteropServices;
using Punchy.Fauna;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

// Draws posed Punchy creatures (CreatureAnimator's segments) as instances of
// one unit capsule with the creature material, in a single call. Shared by
// the valley's animals (ValleyFauna) and a shapeshifted wizard's animal body
// (Shapeshift), each with its own drawer. Not a component: the owner makes
// one in Start and disposes of it in OnDestroy.
//
// A creature is capsules sunk into each other, drawn from outside only. With the
// lens in one, or so near that the near plane cuts it, that capsule goes and the
// ends of the limbs and the neck buried in it stand in the open (issue #47). So
// such a creature is drawn for its shadow alone, as CameraClearance does the
// wizard, until the lens backs out: owners call HideAtLens once it's posed.
public sealed class CreatureDrawer : System.IDisposable
{
    static readonly int SegmentsId = Shader.PropertyToID("_Segments");

    readonly Material material;
    readonly Mesh capsule;
    readonly MaterialPropertyBlock props = new();
    GraphicsBuffer buffer;

    /// <summary>A segment's Pad for "shadow only" (Creature.shader): under 0, where a crack web's is over 0.</summary>
    public const float ShadowOnly = -1f;
    // Beyond this from the lens a creature's first segment is too far to test, whatever its size.
    const float TooFar = 12f;
    // Clear of the near plane by this much, so the cut never quite shows as it nears.
    const float LensMargin = 0.05f;

    public CreatureDrawer(Material material)
    {
        this.material = material;
        capsule = CreatureCapsule.Build(12, 6, 4);
    }

    /// <summary>How near a camera's lens something must be for the near plane to cut it: out to the near plane's corners.</summary>
    public static float LensReach(Camera cam)
    {
        float tan = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
        return cam.nearClipPlane * Mathf.Sqrt(1f + tan * tan * (1f + cam.aspect * cam.aspect)) + LensMargin;
    }

    /// <summary>Whether one of a creature's segments comes within <paramref name="reach"/> of the lens.</summary>
    public static bool AtLens(SegmentInstance[] instances, int first, int count, float3 lens, float reach)
    {
        if (count <= 0 || math.distancesq(instances[first].Position, lens) > TooFar * TooFar) return false;
        for (int i = first; i < first + count; i++)
        {
            var s = instances[i];
            float around = Mathf.Max(s.RadiusStart, s.RadiusEnd) * Mathf.Max(s.Flatten, 1f) + reach;
            var along = math.mul(s.Rotation, new float3(0f, 0f, 1f));
            var to = lens - s.Position;
            if (math.distancesq(to, along * math.clamp(math.dot(to, along), 0f, s.Length)) < around * around) return true;
        }
        return false;
    }

    /// <summary>Marks one posed creature shadow only if it's at the lens; after anything else that sets a segment's Pad. Returns whether it did.</summary>
    public static bool HideAtLens(SegmentInstance[] instances, int first, int count, Camera cam)
    {
        if (cam == null) return false;
        if (!AtLens(instances, first, count, cam.transform.position, LensReach(cam))) return false;
        for (int i = first; i < first + count; i++) instances[i].Pad = ShadowOnly;
        return true;
    }

    /// <summary>Draws the first <paramref name="count"/> segments, culled as a big box round <paramref name="centre"/>.</summary>
    public void Draw(SegmentInstance[] instances, int count, Vector3 centre)
    {
        if (count <= 0 || material == null) return;
        if (buffer == null || buffer.count < count)
        {
            buffer?.Release();
            buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, Mathf.Max(count, instances.Length), Marshal.SizeOf<SegmentInstance>());
        }
        buffer.SetData(instances, 0, 0, count);
        props.SetBuffer(SegmentsId, buffer);
        var rp = new RenderParams(material)
        {
            worldBounds = new Bounds(centre, Vector3.one * 2000f),
            matProps = props,
            shadowCastingMode = ShadowCastingMode.On,
            receiveShadows = true,
        };
        Graphics.RenderMeshPrimitives(rp, capsule, 0, count);
    }

    public void Dispose()
    {
        buffer?.Release();
        buffer = null;
        if (capsule != null) Object.Destroy(capsule);
    }
}
