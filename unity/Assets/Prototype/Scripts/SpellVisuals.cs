using System;
using System.Collections.Generic;
using UnityEngine;

// How each spell looks when cast, as data: an optional projectile that
// flies from the hand (or a meteor from the sky, or a storm over the
// spell's squares), an optional effect where it lands, and the colour of
// the glow in the caster's hand. The chemistry itself stays in rules/*.json;
// this is only the show. Built by Prototype > Build Chemistry Yard.
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
    }

    public List<Entry> entries = new();

    public Entry For(string spellId) => entries.Find(e => e.spellId == spellId);
}
