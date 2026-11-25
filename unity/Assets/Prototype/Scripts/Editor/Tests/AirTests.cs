using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

// The one air (docs/design/hills.md "One air"): Air.cs and Air.hlsl are the same sums (the shaders' AirSeen and AirToward,
// read back from a compute shader, against Air.Seen and Air.Toward); URP's fog for the Lit things is matched to the air
// where it says; a hills map's air leaves the massif to read at 12 km; and the air has one owner: no script but
// AirInPlay writes the scene's fog, and no shader of the land, its water or the far ring reads URP's fog.
public class AirTests
{
    const int Asked = 256;

    static Air Hills(float rain = 0f, float fog = 0f) =>
        Air.Exponential(FarRing.HillsReach, Air.GaussianMetres(FarRing.HillsClear, FarRing.HillsRainy, rain, fog), FarRing.HillsClear,
            FarRing.HillsFarFrom, FarRing.HillsFarTo, new Color(0.70f, 0.80f, 0.93f), new Vector3(0.03f, 0.025f, 0.02f), new Vector3(0.6f, 0.5f, 0.62f).normalized);

    [Test, Category("Unit")]
    public void URPsFogIsMatchedToTheAirWhereItSays()
    {
        foreach (var air in new[] { Hills(), Hills(rain: 0.6f), Hills(fog: 1f) })
        {
            float urp = Mathf.Exp(-Mathf.Pow(air.Density * Air.LitMatchedAt, 2f));
            Assert.AreEqual(air.Seen(Air.LitMatchedAt), urp, 1e-4f);
        }
    }

    [Test, Category("Unit")]
    public void AHillsMapsClearAirReadsTheMassifFarOff()
    {
        var air = Hills();
        Assert.That(air.Seen(672f), Is.GreaterThan(0.9f), "the known ring's edge hardly hazed: no pale band there");
        Assert.That(air.Seen(12000f), Is.InRange(0.1f, 0.3f), "the massif's far side still reads at 12 km");
        Assert.That(air.Seen(FarRing.HillsFarTo), Is.EqualTo(0f).Within(1e-6f), "nothing left where the far land ends");
        Assert.That(Hills(rain: 1f).Seen(2000f), Is.LessThan(0.2f), "rain closes it in");
        Assert.That(Hills(fog: 1f).Seen(150f), Is.LessThan(0.01f), "fog closes it right in");
        for (float d = 0f; d < FarRing.HillsFarTo; d += 50f) Assert.That(air.Seen(d + 50f), Is.LessThanOrEqualTo(air.Seen(d)), "never clearer farther off");
    }

    [Test]
    public void TheShadersReadTheAirAsAirDoes()
    {
        if (!SystemInfo.supportsComputeShaders) Assert.Ignore("no compute shaders here");
        var asset = AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/Prototype/Scripts/Editor/Tests/AirRead.compute");
        Assert.IsNotNull(asset);
        var compute = Object.Instantiate(asset);
        var target = new RenderTexture(Asked, 2, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear) { enableRandomWrite = true };
        target.Create();
        var read = new Texture2D(Asked, 2, TextureFormat.RGBAFloat, false, true);
        try
        {
            foreach (var air in new[] { Hills(), Hills(rain: 0.5f), Air.Faded(250f, 16000f, new Color(0.6f, 0.7f, 0.8f), Vector3.zero, Vector3.up) })
            {
                int kernel = compute.FindKernel("Read");
                compute.SetVector("_Air", air.ForShaders);
                compute.SetVector("_AirFar", air.FarForShaders);
                var h = air.Horizon.linear;
                compute.SetVector("_AirHorizon", new Vector4(h.r, h.g, h.b, 0f));
                compute.SetVector("_AirGlow", air.Glow);
                compute.SetVector("_AirSun", air.Sun);
                compute.SetVector("_WorldSpaceCameraPos", Vector4.zero);
                float metres = 16000f / Asked;
                compute.SetVector("_Asked", new Vector4(Asked, metres, 0.05f, 0f));
                compute.SetTexture(kernel, "_Out", target);
                compute.Dispatch(kernel, Asked / 64, 1, 1);
                var was = RenderTexture.active;
                RenderTexture.active = target;
                read.ReadPixels(new Rect(0, 0, Asked, 2), 0, 0, false);
                RenderTexture.active = was;
                float seenApart = 0f, colourApart = 0f;
                for (int x = 0; x < Asked; x++)
                {
                    seenApart = Mathf.Max(seenApart, Mathf.Abs(read.GetPixel(x, 0).r - air.Seen(x * metres)));
                    float a = (x + 0.37f) / Asked * 2f * Mathf.PI;
                    var c = air.Toward(new Vector3(Mathf.Cos(a), 0.05f, Mathf.Sin(a)));
                    var s = read.GetPixel(x, 1);
                    colourApart = Mathf.Max(colourApart, Mathf.Max(Mathf.Abs(s.r - c.x), Mathf.Max(Mathf.Abs(s.g - c.y), Mathf.Abs(s.b - c.z))));
                }
                TestContext.Out?.WriteLine($"shader and C#: seen at most {seenApart:0.000000} apart, colour {colourApart:0.000000}");
                Assert.That(seenApart, Is.LessThan(1f / 1024f));
                Assert.That(colourApart, Is.LessThan(1f / 1024f));
            }
        }
        finally
        {
            target.Release();
            Object.DestroyImmediate(target);
            Object.DestroyImmediate(read);
            Object.DestroyImmediate(compute);
        }
    }

    const string Prototype = "Assets/Prototype";

    [Test, Category("Unit")]
    public void OnlyAirInPlayWritesTheScenesFog()
    {
        var writes = new Regex(@"RenderSettings\.fog(Color|Density|Mode|StartDistance|EndDistance)\s*=[^=]");
        var writers = Directory.GetFiles(Path.Combine(Prototype, "Scripts"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Replace('\\', '/').Contains("/Editor/") && Path.GetFileName(f) != "AirInPlay.cs" && writes.IsMatch(File.ReadAllText(f)))
            .Select(Path.GetFileName).ToList();
        Assert.IsEmpty(writers, "the fog is AirInPlay's to write (the scene's builders set where it starts)");
    }

    [Test, Category("Unit")]
    public void TheLandAndTheFarRingDrawByTheOneAir()
    {
        var urpFog = new Regex(@"\b(MixFog|MixFogColor|ComputeFogFactor|unity_FogColor|unity_FogParams)\b");
        string[] ours = { "FarLand.shader", "Ocean.shader", "CellWater.shader", "TreeImpostor.shader", "TreeImpostor.hlsl", "TerrainRockPasses.hlsl" };
        foreach (var name in ours)
        {
            string text = File.ReadAllText(Path.Combine(Prototype, "Shaders", name));
            Assert.IsFalse(urpFog.IsMatch(text), $"{name} reads URP's fog: draw by Air.hlsl's Aired instead");
            if (!name.EndsWith(".hlsl", System.StringComparison.Ordinal) || name == "TerrainRockPasses.hlsl")
                Assert.That(text, Does.Contain("Air.hlsl"), $"{name} doesn't draw by the one air");
        }
        var ownHaze = new Regex(@"\b(LandmarkHaze|_FarHazeDensity|_FarSkyHorizon|_FarSkyZenith)\b");
        var stale = Directory.GetFiles(Prototype, "*.*", SearchOption.AllDirectories)
            .Where(f => (f.EndsWith(".shader", System.StringComparison.Ordinal) || f.EndsWith(".hlsl", System.StringComparison.Ordinal) || f.EndsWith(".cs", System.StringComparison.Ordinal))
                && !f.EndsWith("AirTests.cs", System.StringComparison.Ordinal) && ownHaze.IsMatch(File.ReadAllText(f)))
            .Select(Path.GetFileName).ToList();
        Assert.IsEmpty(stale, "a haze of its own, beside the one air");
    }
}
