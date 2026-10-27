using System;
using System.Collections.Generic;
using UnityEngine;

// How each spell looks when cast, as data: an optional projectile that
// flies from the hand (or a meteor from the sky, a storm over the spell's
// squares, or a beam along its line), an optional effect where it lands, the
// colour of the glow in the caster's hand, and the caster's gesture
// (CastGestures). The chemistry itself stays in rules/*.json; this is only the
// show. Built by Prototype > Build Chemistry Yard. A show is spawned without
// colliders (Spawn): some pack effects carry a solid one (the Respawn mist's
// sphere), which would stand in a Misty Step's way.
[CreateAssetMenu(menuName = "Prototype/Spell Visuals")]
public class SpellVisuals : ScriptableObject
{
    [Serializable]
    public class Entry
    {
        public string spellId;
        [Tooltip("Flies from the hand to the target; the spell takes effect when it lands.")]
        public GameObject projectile;
        [Tooltip("Plays where the spell lands.")]
        public GameObject impact;
        public float impactSeconds = 2f;
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

        /// <summary>Plays the impact at a point for impactSeconds (tinted if asked), if there is one.</summary>
        public void PlayImpact(Vector3 at, Quaternion rotation)
        {
            if (impact == null) return;
            var fx = Spawn(impact, at, rotation);
            if (tinted)
                foreach (var particles in fx.GetComponentsInChildren<ParticleSystem>())
                {
                    var main = particles.main;
                    main.startColor = glow;
                }
            UnityEngine.Object.Destroy(fx, impactSeconds);
        }
    }

    public List<Entry> entries = new();

    public Entry For(string spellId) => entries.Find(e => e.spellId == spellId);

    /// <summary>
    /// A copy of a show's prefab at a spot, with every collider in it switched off and removed: a
    /// show is only to look at, and a solid one would block a blink or knock the wizard about.
    /// </summary>
    public static GameObject Spawn(GameObject prefab, Vector3 at, Quaternion turn)
    {
        var go = Instantiate(prefab, at, turn);
        NoColliders(go);
        return go;
    }

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
