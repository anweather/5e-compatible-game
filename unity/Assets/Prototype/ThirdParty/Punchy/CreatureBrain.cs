// Adapted from Punchy by Zachary Franklin (https://github.com/Skeleman/Punchy),
// used with permission. From Punchy's CreatureBrainSystem.cs: one creature at
// a time instead of an Entities job, the ground through ICreatureGround, and
// "the player" generalised to the nearest threat (a wizard, or fire, which
// always scares). The behaviour is his.
// Kept close to Punchy, so our style analyzers are off here.
#pragma warning disable S3776, S3973, S2234, UNT0024
using Unity.Mathematics;

namespace Punchy.Fauna
{
    /// <summary>
    /// Simple animal behaviour: stand around for a while (glancing at the player if they're near), wander to a
    /// random spot nearby, repeat; bolt away if the player comes too close. Creatures keep to dry land they can
    /// climb, turn at a limited rate and speed up and slow down smoothly, and their gait phase advances by the
    /// distance they cover, so the stride (measured from the key poses) matches the ground speed.
    /// </summary>
    public static class CreatureBrain
    {
        /// <summary>Fire scares an animal from at least this far, however bold it is.</summary>
        const float FireFear = 10f;

        /// <param name="threat">The nearest thing to be wary of (far away if none).</param>
        /// <param name="threatIsFire">Fire scares even animals that never run from people.</param>
        public static void Step(ref CreatureState s, CreatureBody b, ICreatureGround terrain, float2 threat, bool threatIsFire, float dt)
        {
            var rng = new Random(s.Seed | 1u);
            if (s.Mode == CreatureMode.Puppet)
            {
                Advance(ref s, b, terrain, dt);
                return;
            }

            var flee = threatIsFire ? math.max(b.FleeDistance, FireFear) : b.FleeDistance;
            var away = s.Position.xz - threat;
            var playerDistance = math.length(away);
            var scared = flee > 0f && playerDistance < flee * s.Scale;
            if (scared && s.Mode != CreatureMode.Flee)
            {
                s.Mode = CreatureMode.Flee;
                s.Target = s.Position.xz + math.normalizesafe(away, new float2(1f, 0f)) * 30f + rng.NextFloat2Direction() * 6f;
            }

            var targetSpeed = 0f;
            var desiredLook = 0f;
            switch (s.Mode)
            {
                case CreatureMode.Idle:
                    s.Timer -= dt;
                    // Keep an eye on a nearby player.
                    if (flee > 0f && playerDistance < flee * 2.5f)
                    {
                        var toPlayer = math.atan2(-away.x, -away.y);
                        desiredLook = math.clamp(WrapAngle(toPlayer - s.Yaw), -1.2f, 1.2f);
                    }
                    else
                        desiredLook = math.sin(s.Timer * 0.7f + s.Seed % 7) * 0.5f;
                    if (s.Timer <= 0f)
                    {
                        var candidate = s.Position.xz + rng.NextFloat2Direction() * b.WanderRadius * rng.NextFloat(0.3f, 1f);
                        if (terrain.Walkable(candidate))
                        {
                            s.Target = candidate;
                            s.Mode = CreatureMode.Wander;
                        }
                        else
                            s.Timer = 0.5f;
                    }
                    break;
                case CreatureMode.Wander:
                    targetSpeed = b.WalkSpeed * s.Scale;
                    if (math.distance(s.Position.xz, s.Target) < 1f)
                        Rest(ref s, b, ref rng);
                    break;
                case CreatureMode.Flee:
                    targetSpeed = b.RunSpeed * s.Scale;
                    if (!scared && playerDistance > flee * 2.2f || math.distance(s.Position.xz, s.Target) < 2f)
                    {
                        if (scared)
                            s.Target = s.Position.xz + math.normalizesafe(away, new float2(1f, 0f)) * 30f;
                        else
                            Rest(ref s, b, ref rng);
                    }
                    break;
            }

            // Turn towards the target, then move; slow down while turning sharply.
            var turn = 0f;
            if (s.Mode != CreatureMode.Idle)
            {
                var to = s.Target - s.Position.xz;
                var wanted = WrapAngle(math.atan2(to.x, to.y) - s.Yaw);
                turn = math.clamp(wanted, -b.TurnRate * dt, b.TurnRate * dt);
                targetSpeed *= math.saturate(math.cos(wanted) * 1.5f);
            }
            s.Yaw = WrapAngle(s.Yaw + turn);
            s.YawRate = dt > 0f ? turn / dt : 0f;
            s.Speed += (targetSpeed - s.Speed) * math.min(1f, dt * (s.Mode == CreatureMode.Flee ? 4f : 2.5f));

            // Stay out of water and off cliffs: stop (or, fleeing, veer) when the way ahead is bad.
            var forward = new float2(math.sin(s.Yaw), math.cos(s.Yaw));
            var ahead = s.Position.xz + forward * math.max(1.2f, s.Speed * 0.5f) * s.Scale;
            if (s.Speed > 0.05f && !terrain.Walkable(ahead) || SlopeAhead(terrain, s.Position.xz, ahead) > 0.8f)
            {
                s.Speed *= 0.5f;
                if (s.Mode == CreatureMode.Flee)
                    s.Target = s.Position.xz + new float2(-forward.y, forward.x) * (rng.NextBool() ? 20f : -20f);
                else if (s.Mode == CreatureMode.Wander)
                    Rest(ref s, b, ref rng);
            }
            Advance(ref s, b, terrain, dt);
            s.Look += (desiredLook - s.Look) * math.min(1f, dt * 3f);
            s.Seed = rng.state;
        }

        /// <summary>Moves along the heading and advances the gait by the distance covered, so feet don't slide.</summary>
        static void Advance(ref CreatureState s, CreatureBody b, ICreatureGround terrain, float dt)
        {
            var forward = new float2(math.sin(s.Yaw), math.cos(s.Yaw));
            s.Position.xz += forward * s.Speed * dt;
            s.Position.y = terrain.Height(s.Position.xz);
            var run = math.smoothstep(b.WalkSpeed * 1.1f, math.max(b.WalkSpeed * 1.2f, b.RunSpeed * 0.7f), s.Speed / s.Scale);
            var stride = math.lerp(b.WalkStride, b.RunStride, run) * s.Scale;
            s.Phase = math.frac(s.Phase + s.Speed * dt / stride);
            s.Sway += (-s.YawRate * 0.35f - s.Sway) * math.min(1f, dt * 4f);
        }

        static void Rest(ref CreatureState s, CreatureBody b, ref Random rng)
        {
            s.Mode = CreatureMode.Idle;
            s.Timer = rng.NextFloat(b.IdleTime.x, math.max(b.IdleTime.x, b.IdleTime.y));
        }

        static float SlopeAhead(ICreatureGround terrain, float2 from, float2 to)
        {
            var rise = terrain.Height(to) - terrain.Height(from);
            return rise / math.max(math.distance(from, to), 0.1f);
        }

        static float WrapAngle(float a) => a - 2f * math.PI * math.floor((a + math.PI) / (2f * math.PI));
    }
}
