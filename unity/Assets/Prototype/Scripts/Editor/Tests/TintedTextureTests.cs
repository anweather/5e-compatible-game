using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

// ValleyBuilder.Tinted, the terrain's colour-multiplied textures: it has to read a pack texture
// (compressed, not readable) with no GPU, or a -nographics yard gets flat-coloured terrain.
// Only -nographics shows the failure this guards: run it in a -nographics batch too.
public class TintedTextureTests
{
    const string Source = "Assets/Prototype/Generated/TintTestSource.png";
    const string Output = "Assets/Prototype/Generated/TintTestSource_Tinted.png";

    [TearDown]
    public void Remove()
    {
        AssetDatabase.DeleteAsset(Source);
        AssetDatabase.DeleteAsset(Output);
    }

    [Test]
    public void MultipliesTheColoursOfAnUnreadableTexture()
    {
        var solid = new Texture2D(8, 8, TextureFormat.RGBA32, false);
        solid.SetPixels32(Enumerable.Repeat(new Color32(200, 100, 50, 255), 64).ToArray());
        Directory.CreateDirectory(Path.GetDirectoryName(Source));
        File.WriteAllBytes(Source, solid.EncodeToPNG());
        AssetDatabase.ImportAsset(Source);

        ValleyBuilder.Tinted(AssetDatabase.LoadAssetAtPath<Texture2D>(Source), new Color(0.5f, 1f, 1f), 4);

        var tinted = new Texture2D(2, 2);
        tinted.LoadImage(File.ReadAllBytes(Output));
        Assert.That(tinted.width, Is.EqualTo(4));
        Color32 pixel = tinted.GetPixel(2, 2);
        Assert.That(pixel.r, Is.InRange(98, 102));
        Assert.That(pixel.g, Is.InRange(98, 102));
        Assert.That(pixel.b, Is.InRange(48, 52));
    }
}
