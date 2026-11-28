// ReloadKeep: a generated prop or house disabled by a domain reload keeps its meshes and takes them back when it's enabled
// again with the same plan; with a changed plan it makes new ones and the kept ones are destroyed; whatever isn't taken
// back is destroyed after the reload; and outside a reload nothing is kept. The reload is stood in for by its flag.
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public class ReloadKeepTests
{
    static readonly FieldInfo Reloading = typeof(ReloadKeep).GetField("reloading", BindingFlags.NonPublic | BindingFlags.Static);
    static readonly FieldInfo PropMesh = typeof(GeneratedProp).GetField("mesh", BindingFlags.NonPublic | BindingFlags.Instance);
    static readonly FieldInfo HouseBody = typeof(VillageHouse).GetField("body", BindingFlags.NonPublic | BindingFlags.Instance);

    GameObject made;

    [SetUp]
    public void Clean() => ReloadKeepStash.instance.DropUnclaimed();

    [TearDown]
    public void Restore()
    {
        Reloading.SetValue(null, false);
        ReloadKeepStash.instance.DropUnclaimed();
        if (made != null) Object.DestroyImmediate(made);
    }

    static Mesh MeshOf(GeneratedProp prop) => (Mesh)PropMesh.GetValue(prop);

    GeneratedProp Palm(int seed)
    {
        made = new GameObject("Palm under test");
        made.SetActive(false);
        var prop = made.AddComponent<GeneratedProp>();
        prop.kind = GeneratedProp.Kind.Palm;
        prop.seed = seed;
        made.SetActive(true);
        return prop;
    }

    [Test]
    public void APropDisabledByAReloadTakesItsMeshBack()
    {
        var prop = Palm(7);
        var mesh = MeshOf(prop);
        Reloading.SetValue(null, true);
        prop.enabled = false;
        Assert.That(mesh != null, "kept, not destroyed, while the reload disables it");
        prop.enabled = true;
        Assert.AreSame(mesh, MeshOf(prop));
        Assert.AreEqual(1, prop.transform.childCount);
    }

    [Test]
    public void AChangedPlanMakesItAgainAndDropsTheKeptOne()
    {
        var prop = Palm(7);
        var mesh = MeshOf(prop);
        Reloading.SetValue(null, true);
        prop.enabled = false;
        prop.seed = 8;
        prop.enabled = true;
        Assert.AreNotSame(mesh, MeshOf(prop));
        Assert.That(mesh == null, "the kept mesh is destroyed");
        Assert.AreEqual(1, prop.transform.childCount);
    }

    [Test]
    public void WhatIsntTakenBackIsDestroyedAfterTheReload()
    {
        var prop = Palm(7);
        var mesh = MeshOf(prop);
        Reloading.SetValue(null, true);
        prop.enabled = false;
        ReloadKeepStash.instance.DropUnclaimed();
        Assert.That(mesh == null);
        Assert.AreEqual(0, prop.transform.childCount);
    }

    [Test]
    public void OutsideAReloadNothingIsKept()
    {
        var prop = Palm(7);
        var mesh = MeshOf(prop);
        prop.enabled = false;
        Assert.That(mesh == null);
        prop.enabled = true;
        Assert.That(MeshOf(prop) != null);
    }

    [Test]
    public void AHouseDisabledByAReloadKeepsItsPartsAndColliders()
    {
        made = new GameObject("House under test");
        made.SetActive(false);
        var house = made.AddComponent<VillageHouse>();
        made.SetActive(true);
        var body = (Mesh)HouseBody.GetValue(house);
        int children = made.transform.childCount;
        Reloading.SetValue(null, true);
        house.enabled = false;
        house.enabled = true;
        Assert.AreSame(body, HouseBody.GetValue(house));
        Assert.AreEqual(children, made.transform.childCount);
        Reloading.SetValue(null, false);
        house.enabled = false;
        Assert.That(body == null, "an ordinary disable still clears it");
    }
}
