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
        /// <summary>How far this show is turned from its family's own colour to its spell's look.</summary>
        public LookTint Tint { get; private set; }
        /// <summary>The spell chose a colour of its own and its show is drawn in it, whatever its element's is.</summary>
        public bool ChoseColour { get; private set; }
        /// <summary>The fire it leaves burns in that colour too (ChemGrid.LookFire), not in the element's.</summary>
        public bool KeepsFire { get; private set; }

        [NonSerialized] Shader particleShader;

        /// <summary>How big this show is for a spell as the rules stand and for how long it was held.</summary>
        public float ScaleFor(SpellDef spell, int charge) =>
            EffectScale.Of(string.IsNullOrEmpty(scaleBy) ? null : scaleBy, scaleBase, spell, charge);

        /// <summary>How big a spell's burst (BurstFx) is: as this show where it scales with the spell's numbers, else by the charge alone.</summary>
        public static float BurstSize(Entry look, SpellDef spell, int charge) =>
            !string.IsNullOrEmpty(look?.scaleBy) ? look.ScaleFor(spell, charge) : 1f + 0.5f * (World.ClampCharge(charge) - 1);

        /// <summary>This family's show in a spell's colour.</summary>
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

        /// <summary>Turns a copy of one of this show's effects to the spell's look; as authored if the look is its own colour.</summary>
        public void Dress(GameObject show) => Tint.Dress(show, particleShader);

        /// <summary>Plays the impact at a point for impactSeconds (tinted if asked), if there is one, at <paramref name="scale"/>.</summary>
        public void PlayImpact(Vector3 at, Quaternion rotation, float scale = 1f)
        {
            if (impact == null) return;
            var fx = ShowPool.Take(ShowOf(impact), at, rotation, DressShow, scale);
            LastImpact = fx;
            ShowPool.Release(fx, impactSeconds);
        }

        /// <summary>A copy of one of this show's effects kept for its look (ShowPool), dressed in it.</summary>
        public ShowPool.Look ShowOf(GameObject prefab) => new(prefab, Tint, tinted ? glow : Color.clear);

        /// <summary>A meteor's blast or dust kept for its look (MeteorFall): dressed, never tinted, so kept apart from a tinted impact.</summary>
        public ShowPool.Look BlastOf(GameObject prefab) => new(prefab, Tint, Color.clear);

        /// <summary>Has this show's effects copied ahead of its first cast (ShowPool.Ready).</summary>
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

        /// <summary>Turns a new copy of one of this show's effects to its look, and colours it with the glow if it's tinted.</summary>
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

    /// <summary>One show per effect family.</summary>
    public List<Entry> families = new();

    [Tooltip("Draws a pack effect in a look of another hue (Prototype/LookParticle), kept here so a player build has it.")]
    public Shader lookParticle;

    /// <summary>The last impact played, for the probe.</summary>
    public static GameObject LastImpact { get; set; }

    [NonSerialized] readonly Dictionary<string, Entry> shows = new();
    [NonSerialized] Rules showsFor, showsFrom;
    [NonSerialized] LookFamilies looks;
    [NonSerialized] Rules readiedFor;

    /// <summary>
    /// A spell's show from the rules in force: its family's, in its colour; null with no rules or
    /// no look. Kept for a rule set (the view asks every frame), so a switch gives a new one.
    /// <paramref name="from"/> is the rules before any mod, which say what's moved (ColourOf); none, nothing has.
    /// </summary>
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
        // Read from the file, not this asset, so a family's own colour needs no yard build.
        looks ??= LookFamilies.Load(ChemGrid.Folders.Rules);
        var color = ColourOf(spell, rules, from, out bool chose);
        return shows[spellId] = family.Colored(color, lookParticle, looks.Find("spells", look.Family)?.Own, chose);
    }

    // The colour a spell's show is drawn in (J-052). Every spell ships with a colour, so as the world's colours
    // do (ChemGrid.WorldLook.cs) this goes by what's moved from the base rules: a spell chose a colour if a mod
    // gave it one, and a pale one asks for none. One that didn't takes its element's, the one its "element" names,
    // whichever orbs made it, once that has moved (at once for a new spell, which has no colour to fall back on).
    // A spell with no element keeps what it has, so the rules as they ship change nothing.
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

    /// <summary>Has every spell's show in these rules copied ahead of its first cast (ShowPool.Ready), once for each rule set.</summary>
    public void ReadyShows(Rules rules, Rules from = null)
    {
        if (rules == null || ReferenceEquals(rules, readiedFor)) return;
        readiedFor = rules;
        foreach (var spell in rules.Spells) For(rules, spell.Id, from)?.Ready();
    }

    /// <summary>
    /// Scales a show as a whole: its particle systems follow the transform (the packs' are set to
    /// ignore it), and their speeds and lights go with it. At 1 the show stays exactly as authored.
    /// </summary>
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

    /// <summary>
    /// A copy of a show's prefab at a spot, with every collider in it switched off and removed: a
    /// show is only to look at, and a solid one would block a blink or knock the wizard about.
    /// </summary>
    public static GameObject Spawn(GameObject prefab, Vector3 at, Quaternion turn)
    {
        using var spawning = SpawnMarker.Auto();
        var go = Instantiate(prefab, at, turn);
        NoColliders(go);
        return go;
    }

    static readonly Unity.Profiling.ProfilerMarker SpawnMarker = new("SpellVisuals.Spawn");

    /// <summary>Switches off and removes every collider in a show (off at once: Destroy waits for the frame's end).</summary>
    public static void NoColliders(GameObject show)
    {
        foreach (var c in show.GetComponentsInChildren<Collider>(true))
        {
            c.enabled = false;
            Destroy(c);
        }
    }
}
