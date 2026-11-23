using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

// AssetPackRestore.FromCommandLine's parts that run without importing anything: the -asset-cache
// argument, finding each pack's file by its cache path, and failing before any import when one is missing.
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
        Assert.That(AssetPackRestore.RestoreFromCache(new List<AssetPackRestore.Pack>(), Path.Combine(cache, "nowhere")), Is.Null);

    [Test]
    public void ACacheFolderThatIsNotThereFails() =>
        Assert.That(AssetPackRestore.RestoreFromCache(new List<AssetPackRestore.Pack> { Pack("Maker/Kind/Here.unitypackage") }, Path.Combine(cache, "nowhere")),
            Does.StartWith("no cache folder"));

    [Test]
    public void APackNotInTheCacheFailsBeforeAnyImport()
    {
        var packs = new List<AssetPackRestore.Pack> { Pack("Maker/Kind/Here.unitypackage"), Pack("Maker/Kind/Gone.unitypackage") };
        string failed = AssetPackRestore.RestoreFromCache(packs, cache);
        Assert.That(failed, Does.Contain("Maker/Kind/Gone.unitypackage"));
        Assert.That(failed, Does.Not.Contain("Here.unitypackage"), "the pack that is there is found by its cache path");
        Assert.That(Directory.Exists("Assets/NoSuchPackFolder"), Is.False, "nothing was imported");
    }
}
