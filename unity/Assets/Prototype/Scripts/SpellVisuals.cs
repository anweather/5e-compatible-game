using System;
using System.Collections.Generic;
using Chem;
using UnityEngine;

// How casts look: one entry per effect family of rules/looks.json, built from
// that file by Prototype > Build Chemistry Yard: an optional projectile that
// flies from the hand (or a meteor from the sky, a storm over the spell's
// squares, or a beam along its line), an optional effect where it lands, and
// the caster's gesture (CastGestures). A spell names its family and colour in
// spells.json ("look"), read here from the rules in force, so a mod's new spell
// has a show, and a mod's bigger one a bigger show, with no rebuild. The
// chemistry stays in rules/*.json; this is only the show. A show is spawned without
// colliders (Spawn): some pack effects carry a solid one (the Respawn mist's
// sphere), which would stand in a Misty Step's way. A look of another hue than
// its family's own turns the whole show (LookTint), at no cost to the rest. A
// spell that chose no colour follows its element's (ColourOf, J-052).
[CreateAssetMenu(menuName = "Prototype/Spell Visuals")]
public class SpellVisuals : ScriptableObject
{
    [Serializable]
    public class Entry
    {
        [Tooltip("The effect family (rules/looks.json).")]
        public string family;
        [Tooltip("Flies from the hand to the target; the spell takes effect when it lands.")]
        public GameObject projectile;
        [Tooltip("Plays where the spell lands.")]
        public GameObject impact;
        public float impactSeconds = 2f;
        [Tooltip("The glow in the hand and any tint: the spell's look's colour, set when a spell's show is worked out (For).")]
        public Color glow = Color.white;
        [Tooltip("Falls from the sky onto the target instead of flying from the hand (MeteorFall), with the projectile as its flames.")]
        public bool fromSky;
        [Tooltip("A second effect where it lands, with the impact (a meteor's dust).")]
        public GameObject afterImpact;
        [Tooltip("Pours down over the spell's squares from a low cloud for a while (SleetStorm) instead of flying from the hand; it lands when the first of it reaches the ground.")]
        public bool storm;
        [Tooltip("The caster's gesture (CastGestures); Auto picks one from the spell's rules: a meteor pulls down, earth raises or presses, a push shoves, a lobbed blast is thrown, a bolt or beam is thrust out.")]
        public CastGesture gesture;
        [Tooltip("A beam from the hand along the spell's line to where it ends (FrostBeam), instead of a projectile; it lands when the beam gets there.")]
        public bool beam;
        [Tooltip("Colours the impact's particles with the glow (Shapeshift's green take on the silver mist).")]
        public bool tinted;
        [Tooltip("The spell number the impact scales with (radius, size or length; empty for none), and that number at scale 1.")]
        public string scaleBy;
        public float scaleBase = 1f;
        public LookTint Tint { get; private set; }
        public bool ChoseColour { get; private set; }
        public bool KeepsFire { get; private set; }

        [NonSerialized] Shader particleShader;

        public float ScaleFor(SpellDef spell, int charge) =>
            EffectScale.Of(string.IsNullOrEmpty(scaleBy) ? null : scaleBy, scaleBase, spell, charge);

        public static float BurstSize(Entry look, SpellDef spell, int charge) =>
            !string.IsNullOrEmpty(look?.scaleBy) ? look.ScaleFor(spell, charge) : 1f + 0.5f * (World.ClampCharge(charge) - 1);

        public Entry Colored(Color color, Shader shader = null, float[] own = null, bool chose = false)
        {
            var e = (Entry)MemberwiseClone();
            e.glow = color;
            float paleWash = own?.Length > 3 ? own[3] : 0f;
            e.Tint = own != null ? LookTint.Between(new Color(own[0], own[1], own[2]), color, paleWash) : default;
            e.ChoseColour = chose && own != null;
            e.KeepsFire = e.ChoseColour && paleWash <= 0f;
            e.particleShader = shader;
            return e;
        }

        public void Dress(GameObject show) => Tint.Dress(show, particleShader);

        public void PlayImpact(Vector3 at, Quaternion rotation, float scale = 1f)
        {
            if (impact == null) return;
            var fx = ShowPool.Take(ShowOf(impact), at, rotation, DressShow, scale);
            LastImpact = fx;
            ShowPool.Release(fx, impactSeconds);
        }

        public ShowPool.Look ShowOf(GameObject prefab) => new(prefab, Tint, tinted ? glow : Color.clear);

        public ShowPool.Look BlastOf(GameObject prefab) => new(prefab, Tint, Color.clear);

        public void Ready()
        {
            if (!fromSky)
            {
                ShowPool.Ready(ShowOf(impact), DressShow);
                return;
            }
            ShowPool.Ready(BlastOf(impact), Dress);
            ShowPool.Ready(BlastOf(afterImpact), Dress);
        }

        public void DressShow(GameObject fx)
        {
            Dress(fx);
            if (!tinted) return;
            foreach (var particles in fx.GetComponentsInChildren<ParticleSystem>())
            {
                var main = particles.main;
                main.startColor = glow;
            }
        }
    }

    public List<Entry> families = new();

    [Tooltip("Draws a pack effect in a look of another hue (Prototype/LookParticle), kept here so a player build has it.")]
    public Shader lookParticle;

    public static GameObject LastImpact { get; set; }

    [NonSerialized] readonly Dictionary<string, Entry> shows = new();
    [NonSerialized] Rules showsFor, showsFrom;
    [NonSerialized] LookFamilies looks;
    [NonSerialized] Rules readiedFor;

    public Entry For(Rules rules, string spellId, Rules from = null)
    {
        if (rules == null) return null;
        from ??= rules;
        if (!ReferenceEquals(rules, showsFor) || !ReferenceEquals(from, showsFrom))
        {
            shows.Clear();
            showsFor = rules;
            showsFrom = from;
        }
        if (shows.TryGetValue(spellId, out var show)) return show;
        var spell = rules.Spell(spellId);
        var look = spell?.Look;
        var family = look != null ? families.Find(f => f.family == look.Family) : null;
        if (family == null) return shows[spellId] = null;
        looks ??= LookFamilies.Load(ChemGrid.Folders.Rules);
        var color = ColourOf(spell, rules, from, out bool chose);
        return shows[spellId] = family.Colored(color, lookParticle, looks.Find("spells", look.Family)?.Own, chose);
    }

    static Color ColourOf(SpellDef spell, Rules now, Rules from, out bool chose)
    {
        var shipped = from.Spell(spell.Id)?.Look;
        var own = new Color(spell.Look.R, spell.Look.G, spell.Look.B);
        Color.RGBToHSV(own, out _, out float saturation, out _);
        chose = saturation >= LookTint.Pale && !LookDef.SameColor(shipped, spell.Look);
        if (chose) return own;
        var element = now.ElementLook(spell.Element);
        bool follows = element != null && (shipped == null || !LookDef.SameColor(from.ElementLook(spell.Element), element));
        return follows ? new Color(element.R, element.G, element.B) : own;
    }

    public void ReadyShows(Rules rules, Rules from = null)
    {
        if (rules == null || ReferenceEquals(rules, readiedFor)) return;
        readiedFor = rules;
        foreach (var spell in rules.Spells) For(rules, spell.Id, from)?.Ready();
    }

    public static void Scale(GameObject show, float scale)
    {
        if (Mathf.Approximately(scale, 1f)) return;
        show.transform.localScale *= scale;
        foreach (var particles in show.GetComponentsInChildren<ParticleSystem>())
        {
            var main = particles.main;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            if (main.simulationSpace == ParticleSystemSimulationSpace.World) main.startSpeedMultiplier *= scale;
        }
        foreach (var light in show.GetComponentsInChildren<Light>()) light.range *= scale;
    }

    public static GameObject Spawn(GameObject prefab, Vector3 at, Quaternion turn)
    {
        using var spawning = SpawnMarker.Auto();
        var go = Instantiate(prefab, at, turn);
        NoColliders(go);
        return go;
    }

    static readonly Unity.Profiling.ProfilerMarker SpawnMarker = new("SpellVisuals.Spawn");

    public static void NoColliders(GameObject show)
    {
        foreach (var c in show.GetComponentsInChildren<Collider>(true))
        {
            c.enabled = false;
            Destroy(c);
        }
    }
}
