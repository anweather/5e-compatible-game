using Chem;
using UnityEngine;

// The casting gestures, as data: each one is a short run of key poses over
// time (where each hand reaches, how far the hips dip and the spine leans and
// turns), with an anticipation, the action and a follow-through. CastPose
// plays them over the animation with LimbIK. Each has two moments that
// matter: the wind-up it holds while Cast is held to charge, and the release,
// when the spell leaves the hands (SpellCaster times the projectile, the
// meteor or the effect to it, and counts it in the cast's delay online).
//
// A spell's gesture is its look's (SpellVisuals.Entry.gesture), or, left on
// Auto, chosen from its rules: a meteor pulls down, earth shaping raises or
// presses, a push from the caster shoves, a lobbed blast is thrown, a bolt,
// line or cone is thrust out, and anything else reaches toward the target.
// A new spell gets a gesture by setting its look's gesture in
// ChemYardBuilder.BuildSpellVisuals; a new gesture is a new CastGesture and
// a row in Of.
public enum CastGesture
{
    /// <summary>Chosen from the spell's rules (CastGestures.For).</summary>
    Auto,
    /// <summary>The casting hand reaches toward the target and settles back.</summary>
    Reach,
    /// <summary>The casting arm thrust at the target and held there while a bolt or beam goes.</summary>
    Thrust,
    /// <summary>An overhand throw: wound back over the shoulder, whipped through, released at the top.</summary>
    Throw,
    /// <summary>Both arms raised overhead and gathered, then a heavy two-handed pull down, the body dipping.</summary>
    CallDown,
    /// <summary>Both hands sweep up from near the ground to overhead, the knees bent at the start.</summary>
    Raise,
    /// <summary>Both hands raised, then pressed down low, the body crouching.</summary>
    Press,
    /// <summary>Both hands drawn in to the chest, then shoved forward.</summary>
    Shove,
}

public static class CastGestures
{
    /// <summary>How a gesture moves into a key: smoothly, gathering speed (a heavy pull), or snapping then slowing (a shove).</summary>
    public enum Ease { InOut, In, Out }

    /// <summary>
    /// One moment of a gesture. Hands are in the caster's frame: x to its right, y up from its feet, z toward
    /// the target, in metres for a wizard whose shoulders stand 1.45 m up (CastPose scales them to the rig).
    /// </summary>
    public struct Key
    {
        public float time;
        public Vector3 right, left;
        /// <summary>How far each hand goes to its spot (0 leaves the animation's arm).</summary>
        public float weight, leftWeight;
        /// <summary>The hips lowered (m, the knees bending), the spine leaned forward (degrees, back if negative) and turned (degrees, the right shoulder back if positive).</summary>
        public float dip, lean, twist;
        public Ease ease;

        public Key Body(float dip, float lean, float twist = 0f)
        {
            var k = this;
            k.dip = dip;
            k.lean = lean;
            k.twist = twist;
            return k;
        }
    }

    /// <summary>A gesture: its keys in time order, the wind-up a charge holds, and when the spell leaves the hands.</summary>
    public sealed class Gesture
    {
        public readonly Key[] keys;
        /// <summary>Seconds in: the wind-up pose a held Cast keeps while it charges.</summary>
        public readonly float hold;
        /// <summary>Seconds in: when the spell leaves the hands (the throw, the pull).</summary>
        public readonly float release;
        /// <summary>Whether the hands point up or down at the target as well as toward it.</summary>
        public readonly bool aims;

        public Gesture(Key[] keys, float hold, float release, bool aims = false)
        {
            this.keys = keys;
            this.hold = hold;
            this.release = release;
            this.aims = aims;
        }

        public float Duration => keys[keys.Length - 1].time;

        /// <summary>The pose <paramref name="t"/> seconds in, between the keys either side of it.</summary>
        public Key At(float t)
        {
            if (t <= keys[0].time) return keys[0];
            for (int i = 1; i < keys.Length; i++)
            {
                var b = keys[i];
                if (t > b.time) continue;
                var a = keys[i - 1];
                return Blend(a, b, Eased(b.ease, (t - a.time) / Mathf.Max(1e-4f, b.time - a.time)));
            }
            return keys[keys.Length - 1];
        }
    
        static float Eased(Ease ease, float k)
        {
            k = Mathf.Clamp01(k);
            return ease switch
            {
                Ease.In => k * k,
                Ease.Out => 1f - (1f - k) * (1f - k),
                _ => Mathf.SmoothStep(0f, 1f, k),
            };
        }

        static Key Blend(Key a, Key b, float k) => new Key
        {
            time = Mathf.Lerp(a.time, b.time, k),
            right = Vector3.Lerp(a.right, b.right, k),
            left = Vector3.Lerp(a.left, b.left, k),
            weight = Mathf.Lerp(a.weight, b.weight, k),
            leftWeight = Mathf.Lerp(a.leftWeight, b.leftWeight, k),
            dip = Mathf.Lerp(a.dip, b.dip, k),
            lean = Mathf.Lerp(a.lean, b.lean, k),
            twist = Mathf.Lerp(a.twist, b.twist, k),
            ease = b.ease,
        };
    }

    // Where a hand hangs at rest, so a gesture starts and ends from the arm's natural place.
    static readonly Vector3 Hang = new Vector3(0.22f, 0.85f, 0.08f);

    static readonly Gesture reach = new Gesture(new[]
    {
        Rest(0f),
        One(0.12f, new Vector3(0.2f, 1.45f, 0.62f)),
        Rest(0.7f),
    }, hold: 0.12f, release: 0f, aims: true);

    static readonly Gesture thrust = new Gesture(new[]
    {
        Rest(0f),
        One(0.08f, new Vector3(0.28f, 1.4f, 0.12f)).Body(0f, -3f, 10f),
        One(0.16f, new Vector3(0.2f, 1.45f, 0.66f), Ease.Out).Body(0f, 8f, -12f),
        One(0.8f, new Vector3(0.2f, 1.45f, 0.64f)).Body(0f, 6f, -10f),
        Rest(1.05f),
    }, hold: 0.08f, release: 0.14f, aims: true);

    static readonly Gesture toss = new Gesture(new[]
    {
        Rest(0f),
        Two(0.3f, new Vector3(0.4f, 1.75f, -0.3f), new Vector3(-0.3f, 1.35f, 0.4f), 0.8f).Body(0.05f, -8f, 30f),
        Two(0.44f, new Vector3(0.22f, 1.75f, 0.5f), new Vector3(-0.28f, 1.05f, 0.15f), 0.6f, Ease.In).Body(0.08f, 12f, -10f),
        Two(0.62f, new Vector3(-0.12f, 1f, 0.45f), new Vector3(-0.3f, 0.95f, -0.05f), 0.4f).Body(0.1f, 20f, -25f),
        Rest(1f),
    }, hold: 0.3f, release: 0.42f);

    static readonly Gesture callDown = new Gesture(new[]
    {
        Rest(0f),
        Both(0.4f, new Vector3(0.12f, 2.1f, 0.12f)).Body(0f, -10f),
        Both(0.62f, new Vector3(0.1f, 2.2f, -0.02f)).Body(0f, -14f),
        Both(0.8f, new Vector3(0.24f, 1.25f, 0.55f), Ease.In).Body(0.18f, 18f),
        Both(0.95f, new Vector3(0.24f, 0.8f, 0.45f), Ease.Out).Body(0.3f, 28f),
        Both(1.3f, new Vector3(0.24f, 0.85f, 0.42f)).Body(0.24f, 22f),
        Rest(1.75f),
    }, hold: 0.4f, release: 0.78f);

    static readonly Gesture raise = new Gesture(new[]
    {
        Rest(0f),
        Both(0.28f, new Vector3(0.25f, 0.6f, 0.45f)).Body(0.28f, 28f),
        Both(0.4f, new Vector3(0.27f, 0.75f, 0.42f)).Body(0.24f, 22f),
        Both(0.62f, new Vector3(0.3f, 2f, 0.32f)).Body(0f, -8f),
        Both(0.9f, new Vector3(0.3f, 2f, 0.3f)).Body(0f, -6f),
        Rest(1.25f),
    }, hold: 0.28f, release: 0.5f);

    static readonly Gesture press = new Gesture(new[]
    {
        Rest(0f),
        Both(0.28f, new Vector3(0.25f, 1.75f, 0.3f)).Body(0f, -6f),
        Both(0.5f, new Vector3(0.27f, 0.6f, 0.5f), Ease.In).Body(0.28f, 28f),
        Both(0.8f, new Vector3(0.27f, 0.62f, 0.48f)).Body(0.26f, 26f),
        Rest(1.15f),
    }, hold: 0.28f, release: 0.45f);

    static readonly Gesture shove = new Gesture(new[]
    {
        Rest(0f),
        Both(0.2f, new Vector3(0.2f, 1.35f, 0.12f)).Body(0.06f, -8f),
        Both(0.32f, new Vector3(0.2f, 1.42f, 0.64f), Ease.Out).Body(0.12f, 12f),
        Both(0.6f, new Vector3(0.2f, 1.42f, 0.62f)).Body(0.1f, 10f),
        Rest(0.9f),
    }, hold: 0.2f, release: 0.3f);

    /// <summary>A gesture's keys and timing (Auto is Reach).</summary>
    public static Gesture Of(CastGesture gesture) => gesture switch
    {
        CastGesture.Thrust => thrust,
        CastGesture.Throw => toss,
        CastGesture.CallDown => callDown,
        CastGesture.Raise => raise,
        CastGesture.Press => press,
        CastGesture.Shove => shove,
        _ => reach,
    };

    /// <summary>The gesture a spell is cast with: its look's, or one chosen from its rules if that is Auto.</summary>
    public static CastGesture For(SpellDef spell, SpellVisuals.Entry look)
    {
        if (look != null && look.gesture != CastGesture.Auto) return look.gesture;
        if (look != null && look.fromSky) return CastGesture.CallDown;
        return FromRules(spell, look != null && look.projectile != null);
    }

    static CastGesture FromRules(SpellDef spell, bool flies)
    {
        if (spell == null) return CastGesture.Reach;
        if (spell.Effect == SpellShapes.TerraformEffect) return spell.Dig > 0 ? CastGesture.Press : CastGesture.Raise;
        if (spell.Origin == "self" && (spell.Push > 0 || spell.Wind > 0)) return CastGesture.Shove;
        if (flies && spell.Shape == SpellShapes.Burst) return CastGesture.Throw;
        bool pointed = spell.Shape == SpellShapes.Line || spell.Shape == SpellShapes.Cone || spell.Shape == SpellShapes.Point;
        return pointed && spell.Element != null ? CastGesture.Thrust : CastGesture.Reach;
    }

    static Key Rest(float time) => new Key { time = time, right = Hang, left = Mirror(Hang) };

    static Key One(float time, Vector3 right, Ease ease = Ease.InOut) =>
        new Key { time = time, right = right, left = Mirror(Hang), weight = 1f, ease = ease };

    static Key Two(float time, Vector3 right, Vector3 left, float leftWeight, Ease ease = Ease.InOut) =>
        new Key { time = time, right = right, left = left, weight = 1f, leftWeight = leftWeight, ease = ease };

    static Key Both(float time, Vector3 right, Ease ease = Ease.InOut) =>
        new Key { time = time, right = right, left = Mirror(right), weight = 1f, leftWeight = 1f, ease = ease };

    static Vector3 Mirror(Vector3 v) => new Vector3(-v.x, v.y, v.z);

}
