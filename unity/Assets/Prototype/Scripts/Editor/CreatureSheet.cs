using System.Collections.Generic;
using System.IO;
using Punchy.Fauna;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

// A dozen species side by side on flat ground, posed mid-stride, drawn in an
// empty scene and written to a PNG: nothing moves, so two sheets can be compared
// pixel by pixel (the before and after of a change to how bodies are built).
// Asks before leaving an unsaved scene.
public static class CreatureSheet
{
    public static readonly string[] Dozen =
    {
        "Deer", "BlackBear", "ArcticFox", "Wolf", "Rabbit", "Turkey",
        "Heron", "GarterSnake", "GiantSpider", "Human", "Ogre", "ToxicFrog",
    };

    const int Columns = 4, Width = 960, Height = 720;
    const float Spacing = 3.2f;

    sealed class Flat : ICreatureGround
    {
        public float Height(float2 xz) => 0f;
        public bool Walkable(float2 xz) => true;
        public bool Water(float2 xz) => false;
    }

    // How big a body is, to fit it to its place: its torso, or its longest chain of segments.
    static float Extent(CreatureBody b)
    {
        float longest = math.max(b.BodyLength, b.StandHeight * 2f);
        foreach (var part in b.Parts)
        {
            float chain = 0f;
            for (int s = 0; s < part.SegmentCount; s++) chain += b.Segments[part.FirstSegment + s].Length;
            longest = math.max(longest, chain);
        }
        return longest;
    }

    /// <summary>Draws the bodies, in order, to a PNG at <paramref name="path"/>.</summary>
    public static void Write(IReadOnlyList<CreatureBody> bodies, string path)
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        var was = EditorSceneManager.GetActiveScene().path;
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var sun = new GameObject("Sun").AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        sun.shadows = LightShadows.Soft;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.45f, 0.45f, 0.5f);

        var cam = new GameObject("Sheet camera").AddComponent<Camera>();
        cam.gameObject.AddComponent<UniversalAdditionalCameraData>();
        cam.transform.SetPositionAndRotation(new Vector3(Spacing * (Columns - 1) / 2f, 8f, -6.5f), Quaternion.Euler(38f, 0f, 0f));
        cam.fieldOfView = 45f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.62f, 0.68f, 0.6f);
        var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
        cam.targetTexture = rt;

        var material = AssetDatabase.LoadAssetAtPath<Material>(PropBuilder.Generated + "/Creature.mat");
        var drawer = new CreatureDrawer(material);
        var animator = new CreatureAnimator();
        var ground = new Flat();
        int total = 0;
        foreach (var b in bodies) total += b.Segments.Length;
        var instances = new SegmentInstance[total];
        int offset = 0;
        for (int i = 0; i < bodies.Count; i++)
        {
            var b = bodies[i];
            var state = new CreatureState
            {
                Position = new float3(i % Columns * Spacing, 0f, (2 - i / Columns) * Spacing),
                Yaw = math.radians(115f),
                Speed = b.WalkSpeed,
                Phase = 0.3f,
                Scale = 1.9f / Extent(b),
                Mode = CreatureMode.Wander,
                Seed = 1u,
            };
            offset += animator.Evaluate(b, state, 0, ground, instances, offset);
        }
        drawer.Draw(instances, offset, cam.transform.position);
        cam.Render();

        RenderTexture.active = rt;
        var shot = new Texture2D(Width, Height, TextureFormat.RGB24, false);
        shot.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
        shot.Apply();
        RenderTexture.active = null;
        File.WriteAllBytes(path, shot.EncodeToPNG());

        cam.targetTexture = null;
        Object.DestroyImmediate(shot);
        Object.DestroyImmediate(rt);
        if (!string.IsNullOrEmpty(was)) EditorSceneManager.OpenScene(was);
    }
}
