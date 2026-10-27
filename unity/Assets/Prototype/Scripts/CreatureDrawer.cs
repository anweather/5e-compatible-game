using System.Runtime.InteropServices;
using Punchy.Fauna;
using UnityEngine;
using UnityEngine.Rendering;

// Draws posed Punchy creatures (CreatureAnimator's segments) as instances of
// one unit capsule with the creature material, in a single call. Shared by
// the valley's animals (ValleyFauna) and a shapeshifted wizard's animal body
// (Shapeshift), each with its own drawer. Not a component: the owner makes
// one in Start and disposes of it in OnDestroy.
public sealed class CreatureDrawer : System.IDisposable
{
    static readonly int SegmentsId = Shader.PropertyToID("_Segments");

    readonly Material material;
    readonly Mesh capsule;
    readonly MaterialPropertyBlock props = new();
    GraphicsBuffer buffer;

    public CreatureDrawer(Material material)
    {
        this.material = material;
        capsule = CreatureCapsule.Build(12, 6, 4);
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
