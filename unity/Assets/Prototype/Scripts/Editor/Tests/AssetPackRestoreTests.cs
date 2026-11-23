using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

// AssetPackRestore.FromCommandLine's parts that run without importing anything: the -asset-cache
// argument, finding each pack's file by its cache path, failing, before any import, when one is missing, and laying
// in the baked tree impostors with their .meta files.
public class AssetPackRestoreTests
{
    string cache;

    [SetUp]
    public void MakeCache()
    {
        cache = Path.Combine(Path.GetTempPath(), "asset-pack-restore-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(cache, "Maker", "Kind"));
        File.WriteAllText(Path.Combine(cache, "Maker", "Kind", "Here.unitypackage"), "not a real pack");
    }

    [TearDown]
    public void RemoveCache() => Directory.Delete(cache, true);

    static AssetPackRestore.Pack Pack(string cachePath) =>
        new AssetPackRestore.Pack { id = 1, name = cachePath, folder = "Assets/NoSuchPackFolder", cache = cachePath };

    [Test]
    public void WithoutTheFlagThereIsNoCacheFolder() =>
        Assert.That(AssetPackRestore.CacheArgument(new[] { "Unity", "-batchmode", "-quit" }), Is.Null);

    [Test]
    public void TheFlagGivesAnAbsoluteFolder() =>
        Assert.That(AssetPackRestore.CacheArgument(new[] { "Unity", "-asset-cache", "packs", "-quit" }),
            Is.EqualTo(Path.GetFullPath("packs")));

    [Test]
    public void TheFlagWithNoFolderAfterItIsIgnored() =>
        Assert.That(AssetPackRestore.CacheArgument(new[] { "Unity", "-asset-cache" }), Is.Null);

    [Test]
    public void NothingMissingIsOkWhateverTheCache() =>
        Assert.That(AssetPackRestore.CheckCache(new List<AssetPackRestore.Pack>(), Path.Combine(cache, "nowhere")), Is.Null);

    [Test]
    public void ACacheFolderThatIsNotThereFails() =>
        Assert.That(AssetPackRestore.CheckCache(new List<AssetPackRestore.Pack> { Pack("Maker/Kind/Here.unitypackage") }, Path.Combine(cache, "nowhere")),
            Does.StartWith("no cache folder"));

    [Test]
    public void APackNotInTheCacheIsNamed()
    {
        var packs = new List<AssetPackRestore.Pack> { Pack("Maker/Kind/Here.unitypackage"), Pack("Maker/Kind/Gone.unitypackage") };
        string failed = AssetPackRestore.CheckCache(packs, cache);
        Assert.That(failed, Does.Contain("Maker/Kind/Gone.unitypackage"));
        Assert.That(failed, Does.Not.Contain("Here.unitypackage"), "the pack that is there is found by its cache path");
    }

    string Impostors(params string[] files)
    {
        string folder = Path.Combine(cache, AssetPackRestore.ImpostorCache);
        Directory.CreateDirectory(folder);
        foreach (var f in files) File.WriteAllText(Path.Combine(folder, f), "cache " + f);
        return Path.Combine(cache, "project", "TreeImpostors");
    }

    [Test]
    public void TheImpostorsGoWhereTheBakerLooks() =>
        Assert.That(AssetPackRestore.ImpostorFolder, Is.EqualTo(TreeImpostorBaker.Folder));

    [Test]
    public void TheBakedImpostorsAreCopiedWithTheirMetaFiles()
    {
        string to = Impostors("Oak.mat", "Oak.mat.meta", "Oak_Albedo.png", "Oak_Albedo.png.meta");
        Assert.That(AssetPackRestore.CopyImpostors(cache, to), Is.EqualTo(4));
        Assert.That(File.ReadAllText(Path.Combine(to, "Oak_Albedo.png.meta")), Is.EqualTo("cache Oak_Albedo.png.meta"),
            "the .meta keeps the GUID the material points at");
        Assert.That(Directory.GetFiles(to), Has.Length.EqualTo(4));
    }

    [Test]
    public void AnImpostorAlreadyThereIsLeftAlone()
    {
        string to = Impostors("Oak.mat", "Oak.mat.meta");
        Directory.CreateDirectory(to);
        File.WriteAllText(Path.Combine(to, "Oak.mat"), "baked here");
        Assert.That(AssetPackRestore.CopyImpostors(cache, to), Is.EqualTo(1));
        Assert.That(File.ReadAllText(Path.Combine(to, "Oak.mat")), Is.EqualTo("baked here"));
    }

    [Test]
    public void ACacheWithNoImpostorsCopiesNothing()
    {
        string to = Path.Combine(cache, "project", "TreeImpostors");
        Assert.That(AssetPackRestore.CopyImpostors(cache, to), Is.EqualTo(0));
        Assert.That(Directory.Exists(to), Is.False);
    }
}
