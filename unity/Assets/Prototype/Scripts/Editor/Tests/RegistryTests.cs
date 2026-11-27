using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

// The registries that stand in for scene searches (Registry.cs): a member joins in Awake and leaves in OnDestroy,
// one on an inactive GameObject (or destroyed) is never handed out, as Find* never found it, a disabled one still
// is, and every registry is empty after Play ends (OnDestroy) and again as Play starts (Registries.ForgetAll).
// Edit mode runs no Awake or OnDestroy, so the tests call them by name, as ChemGridCurrentTests does. Each type
// with a registry has a case in Types; the chunk index (PhysicsProp.Near) has its own tests.
public class RegistryTests
{
    readonly List<GameObject> made = new();

    [SetUp]
    public void SetUp() => Registries.ForgetAll();

    [TearDown]
    public void TearDown()
    {
        foreach (var go in made)
            if (go != null) UnityEngine.Object.DestroyImmediate(go);
        made.Clear();
        Registries.ForgetAll();
    }

    public sealed class Kind
    {
        public string Name;
        public Func<GameObject, Component> Make;
        public Func<Component[]> All;
        public bool WakesInEditMode;

        public override string ToString() => Name;
    }

    static Kind Single<T>(Func<T> inPlay, Func<GameObject, T> make = null) where T : Component => new()
    {
        Name = typeof(T).Name,
        Make = go => make != null ? make(go) : go.AddComponent<T>(),
        All = () => inPlay() is { } one ? new Component[] { one } : Array.Empty<Component>(),
    };

    static Kind Many<T>(Func<T[]> all, Func<GameObject, T> make = null) where T : Component => new()
    {
        Name = typeof(T).Name,
        Make = go => make != null ? make(go) : go.AddComponent<T>(),
        All = () => all().Cast<Component>().ToArray(),
    };

    static PhysicsProp NewProp(GameObject go)
    {
        go.AddComponent<BoxCollider>();
        go.AddComponent<MeshRenderer>();
        go.AddComponent<Rigidbody>();
        return go.AddComponent<PhysicsProp>();
    }

    public static IEnumerable<Kind> Types()
    {
        yield return Single(() => ValleyFauna.InPlay);
        yield return Single(() => ValleyChemistry.InPlay);
        yield return Single(() => ThingSpawner.InPlay);
        yield return Many(() => PhysicsProp.All, NewProp);
        yield return Many(() => SpellCaster.All);
        yield return Many<Caster>(() => Caster.All, go => go.AddComponent<SpellCaster>());
        yield return Many(() => Carryable.All, go => NewProp(go).gameObject.AddComponent<Carryable>());
        yield return Single(() => SkyLight.InPlay);
        var village = Many(() => Village.All);
        village.WakesInEditMode = true;
        yield return village;
        yield return Many(() => NetWizard.All);
        yield return Many(() => SpellProjectile.All);
        yield return Single(() => DefenseField.InPlay);
        yield return Single(() => WeatherFx.InPlay);
        yield return Many(() => FrostBeam.All);
        yield return Many(() => MeteorFall.All);
        yield return Single(() => NetSession.InPlay);
    }

    Component New(Kind kind, bool wake = true)
    {
        var go = new GameObject($"{kind.Name} under test");
        made.Add(go);
        var c = kind.Make(go);
        if (wake) Call(c, "Awake");
        return c;
    }

    static void Call(Component c, string name)
    {
        var method = c.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        Assert.That(method, Is.Not.Null, $"{c.GetType().Name}.{name}");
        method.Invoke(c, null);
    }

    [TestCaseSource(nameof(Types))]
    public void OneThatWokeJoinsAndOneThatDidNotIsLeftOut(Kind kind)
    {
        var unwoken = New(kind, wake: false);
        if (kind.WakesInEditMode) Call(unwoken, "OnDestroy");
        Assert.That(kind.All(), Is.Empty, "edit mode ran no Awake: not joined");
        var woke = New(kind);
        Assert.That(kind.All(), Is.EqualTo(new[] { woke }), "joined once, however often Awake runs");
    }

    [TestCaseSource(nameof(Types))]
    public void OneThatGoesLeaves(Kind kind)
    {
        var c = New(kind);
        Call(c, "OnDestroy");
        Assert.That(kind.All(), Is.Empty);
    }

    [TestCaseSource(nameof(Types))]
    public void OneOnAnInactiveObjectIsLeftOutAndOneDisabledIsNot(Kind kind)
    {
        var c = New(kind);
        c.gameObject.SetActive(false);
        Assert.That(kind.All(), Is.Empty, "Find* skips inactive GameObjects");
        c.gameObject.SetActive(true);
        Assert.That(kind.All(), Is.EqualTo(new[] { c }), "back when active again");
        ((Behaviour)c).enabled = false;
        Assert.That(kind.All(), Is.EqualTo(new[] { c }), "Find* finds a disabled component on an active GameObject");
    }

    [TestCaseSource(nameof(Types))]
    public void ADestroyedOneIsNeverHandedOut(Kind kind)
    {
        var c = New(kind);
        UnityEngine.Object.DestroyImmediate(c.gameObject);
        Assert.That(kind.All(), Is.Empty, "gone even though edit mode ran no OnDestroy");
    }

    [TestCaseSource(nameof(Types))]
    public void EmptyAfterPlayEndsAndAsPlayStarts(Kind kind)
    {
        var a = New(kind);
        New(kind);
        Call(a, "OnDestroy");
        Assert.That(kind.All(), Has.No.Member(a), "Play ending destroys each member: it leaves");
        Registries.ForgetAll();
        Assert.That(kind.All(), Is.Empty, "and Play starting forgets any left (no domain reload, J-023)");
    }

    [Test]
    public void ByInstanceIdIsLowestFirst()
    {
        var kind = Types().First(k => k.Name == nameof(SpellCaster));
        var made3 = new[] { New(kind), New(kind), New(kind) };
        var byId = made3.OrderBy(c => c.GetInstanceID()).ToArray();
        Assert.That(SpellCaster.AllByInstanceId, Is.EqualTo(byId));
    }

    [Test]
    public void TheManyKeepJoinOrder()
    {
        var kind = Types().First(k => k.Name == nameof(PhysicsProp));
        var a = New(kind);
        var b = New(kind);
        var c = New(kind);
        Call(b, "OnDestroy");
        Assert.That(kind.All(), Is.EqualTo(new[] { a, c }));
    }

    [Test]
    public void NearReadsTheChunksAskedForTheRingAroundAndTheLooseOnes()
    {
        var registry = new Registry<BoxCollider>();
        BoxCollider Member(string name)
        {
            var go = new GameObject(name);
            made.Add(go);
            var m = go.AddComponent<BoxCollider>();
            registry.Join(m);
            return m;
        }
        var here = Member("here");
        var ring = Member("ring");
        var far = Member("far");
        var loose = Member("loose");
        registry.Place(here, new Vector2Int(5, 5));
        registry.Place(ring, new Vector2Int(7, 4));
        registry.Place(far, new Vector2Int(9, 5));
        Assert.That(registry.Near(new Vector2Int(6, 5), new Vector2Int(5, 5)), Is.EqualTo(new[] { ring, here, loose }),
            "chunk by chunk (either corner first), then the loose");
        registry.Place(far, new Vector2Int(5, 6));
        Assert.That(registry.Near(new Vector2Int(5, 5), new Vector2Int(5, 5)), Is.EquivalentTo(new[] { here, far, loose }), "moved in");
        registry.Unplace(here);
        Assert.That(registry.InChunk(new Vector2Int(5, 5)), Is.Empty);
        Assert.That(registry.Near(new Vector2Int(20, 20), new Vector2Int(20, 20)), Is.EquivalentTo(new[] { loose, here }),
            "unplaced: in every answer");
        loose.gameObject.SetActive(false);
        Assert.That(registry.Near(new Vector2Int(20, 20), new Vector2Int(20, 20)), Is.EqualTo(new[] { here }), "inactive: left out");
        registry.Leave(here);
        registry.Place(here, new Vector2Int(20, 20));
        Assert.That(registry.Near(new Vector2Int(20, 20), new Vector2Int(20, 20)), Is.Empty, "one that left can't be placed");
        Assert.That(registry.All(), Is.EqualTo(new[] { ring, far }));
    }

    [Test]
    public void ManyJoinAndLeaveInLinearTime()
    {
        const int n = 20000;
        var registry = new Registry<BoxCollider>();
        var members = new BoxCollider[n];
        for (int i = 0; i < n; i++)
        {
            var go = new GameObject("many");
            made.Add(go);
            members[i] = go.AddComponent<BoxCollider>();
        }
        var clock = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < n; i++) registry.Join(members[i]);
        for (int i = 0; i < n; i++) registry.Place(members[i], new Vector2Int(i % 7, 0));
        for (int i = 0; i < n; i += 2) registry.Unplace(members[i]);
        for (int i = 0; i < n; i++) registry.Leave(members[i]);
        clock.Stop();
        Assert.That(registry.All(), Is.Empty);
        Assert.That(registry.Near(new Vector2Int(3, 0), new Vector2Int(3, 0)), Is.Empty);
        Assert.That(clock.ElapsedMilliseconds, Is.LessThan(500), $"{n} joined, placed and left");
    }

    [Test]
    public void APropNotPlacedYetIsNearEverywhere()
    {
        var prop = (PhysicsProp)New(Types().First(k => k.Name == nameof(PhysicsProp)));
        Assert.That(PhysicsProp.Near(new Vector2Int(100, 100), new Vector2Int(100, 100)), Is.EqualTo(new[] { prop }),
            "no grid yet (Start places it): it's in every answer, so no caller misses it");
        Call(prop, "OnDestroy");
        Assert.That(PhysicsProp.Near(new Vector2Int(100, 100), new Vector2Int(100, 100)), Is.Empty);
    }
}
