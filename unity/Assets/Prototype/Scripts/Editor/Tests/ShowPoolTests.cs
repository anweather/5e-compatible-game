using NUnit.Framework;
using UnityEngine;

// A spell's show is copied once and reused (ShowPool, #189): a copy put back is the one taken next, it starts
// from the size and speeds it was made with whatever the last cast scaled it to, and a show with a part of its
// own beyond particles and lights isn't kept at all.
[Category("Unit")]
public class ShowPoolTests
{
    GameObject prefab;

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(prefab);
        var kept = GameObject.Find("Spell shows kept");
        if (kept != null) Object.DestroyImmediate(kept);
        foreach (var show in Object.FindObjectsByType<PooledShow>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            Object.DestroyImmediate(show.gameObject);
    }

    ShowPool.Look Blast()
    {
        prefab = new GameObject("Blast");
        var ps = prefab.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.loop = true;
        main.prewarm = true;
        main.startSpeedMultiplier = 3f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        return new ShowPool.Look(prefab, default, Color.clear);
    }

    [Test]
    public void AShowPutBackIsTheOneTakenNext()
    {
        var look = Blast();
        var first = ShowPool.Take(look, Vector3.zero, Quaternion.identity, null);
        ShowPool.Back(first.GetComponent<PooledShow>());
        Assert.That(ShowPool.FreeCount(look), Is.EqualTo(1));
        Assert.That(first.activeSelf, Is.False, "put back switched off");

        var again = ShowPool.Take(look, Vector3.one, Quaternion.identity, null);
        Assert.That(again, Is.SameAs(first));
        Assert.That(again.activeSelf, Is.True);
        Assert.That(again.transform.position, Is.EqualTo(Vector3.one));
        Assert.That(ShowPool.FreeCount(look), Is.EqualTo(0));
    }

    [Test]
    public void AReusedShowStartsFromItsMadeSizeNotTheLastCasts()
    {
        var look = Blast();
        var first = ShowPool.Take(look, Vector3.zero, Quaternion.identity, null, scale: 3f);
        Assert.That(first.GetComponent<ParticleSystem>().main.startSpeedMultiplier, Is.EqualTo(9f).Within(1e-4f));
        ShowPool.Back(first.GetComponent<PooledShow>());

        var again = ShowPool.Take(look, Vector3.zero, Quaternion.identity, null);
        Assert.That(again.transform.localScale, Is.EqualTo(Vector3.one));
        Assert.That(again.GetComponent<ParticleSystem>().main.startSpeedMultiplier, Is.EqualTo(3f).Within(1e-4f));
        Assert.That(again.GetComponent<ParticleSystem>().main.scalingMode, Is.EqualTo(prefab.GetComponent<ParticleSystem>().main.scalingMode));
    }

    [Test]
    public void AShowWithAPartOfItsOwnIsNotKept()
    {
        var look = Blast();
        prefab.AddComponent<AudioSource>();
        var show = ShowPool.Take(look, Vector3.zero, Quaternion.identity, null);
        Assert.That(show.GetComponent<PooledShow>(), Is.Null);
        Object.DestroyImmediate(show);
    }
}
