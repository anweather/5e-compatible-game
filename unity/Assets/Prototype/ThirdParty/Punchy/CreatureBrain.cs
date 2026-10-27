// Adapted from Punchy by Zachary Franklin (https://github.com/Skeleman/Punchy),
// used with permission. From Punchy's CreatureBrainSystem.cs: one creature at
// a time instead of an Entities job, the other animals through a Herd
// snapshot (ids instead of entities), the ground through ICreatureGround, and
// "the player" generalised to the nearest threat (a wizard, or fire, which
// always scares). The behaviour is his.
// Kept close to Punchy, so our style analyzers are off here.
#pragma warning disable S3776, S3973, S2234, UNT0024, S3358, S107, S1172, S1854, S3241
using System.Collections.Generic;
using Unity.Mathematics;

namespace Punchy.Fauna
{
    /// <summary>
    /// Every creature as it was at the start of the frame, bucketed into a coarse grid, so animals can see each
    /// other (Punchy's snapshot in CreatureBrainSystem). Filled by the caller each frame.
    /// </summary>
    public sealed class Herd
    {
        public const float CellSize = 16f;

        readonly List<int> ids = new List<int>();
        readonly List<int> species = new List<int>();
        readonly List<CreatureState> states = new List<CreatureState>();
        readonly List<CreatureBody> bodies = new List<CreatureBody>();
        readonly Dictionary<int, int> index = new Dictionary<int, int>();
        readonly Dictionary<int2, List<int>> grid = new Dictionary<int2, List<int>>();
        readonly Stack<List<int>> spare = new Stack<List<int>>();

        public void Clear()
        {
            ids.Clear();
            species.Clear();
            states.Clear();
            bodies.Clear();
            index.Clear();
            foreach (var list in grid.Values)
            {
                list.Clear();
                spare.Push(list);
            }
            grid.Clear();
        }

        public void Add(int id, int speciesIndex, in CreatureState state, CreatureBody body)
        {
            int i = ids.Count;
            ids.Add(id);
            species.Add(speciesIndex);
            states.Add(state);
            bodies.Add(body);
            index[id] = i;
            var c = Cell(state.Position.xz);
            if (!grid.TryGetValue(c, out var list))
                grid[c] = list = spare.Count > 0 ? spare.Pop() : new List<int>();
            list.Add(i);
        }

        public static int2 Cell(float2 xz) => (int2)math.floor(xz / CellSize);

        public bool TryFind(int id, out int i) => index.TryGetValue(id, out i);
        public int Id(int i) => ids[i];
        public int Species(int i) => species[i];
        public CreatureState State(int i) => states[i];
        public CreatureBody Body(int i) => bodies[i];
        public IReadOnlyList<int> In(int2 cell) => grid.TryGetValue(cell, out var list) ? list : (IReadOnlyList<int>)System.Array.Empty<int>();
    }

    /// <summary>
    /// Animal behaviour. Between wanders, an animal picks its next activity by its species' tastes and what's
    /// around it: grazing, walking to the water's edge to drink, lying down to rest, sleeping (at night, or by day for
    /// night animals), a game of chase with another of its kind, or hunting prey. Anything that comes too close and
    /// might be dangerous (a wizard, fire; predators, especially hunting ones) sends it running; sleeping animals
    /// notice later. Each activity also sets a posture (lying, head down, play bow, curled up) that the animator
    /// layers over the gait.
    ///
    /// Two-animal activities agree through the Herd: a chaser targets its Partner, the partner notices and runs; when
    /// the chaser catches up the roles swap.
    ///
    /// Movement: creatures keep to dry land they can climb, turn at a limited rate and speed up and slow down
    /// smoothly, and their gait phase advances by the distance they cover, so the stride (measured from the key
    /// poses) matches the ground speed.
    /// </summary>
    public sealed class CreatureBrain
    {
        /// <summary>Fire scares an animal from at least this far, however bold it is.</summary>
        const float FireFear = 10f;

        // This step's inputs.
        Herd herd;
        ICreatureGround terrain;
        float dt, night;

        /// <param name="self">The animal's id in the herd.</param>
        /// <param name="threat">The nearest wizard or fire (far away if none).</param>
        /// <param name="threatIsFire">Fire scares even animals that never run from people.</param>
        /// <param name="nightAmount">0 by day, 1 at night.</param>
        public void Step(ref CreatureState s, int self, int species, CreatureBody b, Herd others, ICreatureGround ground,
                         float2 threat, bool threatIsFire, float nightAmount, float deltaTime)
        {
            herd = others;
            terrain = ground;
            dt = deltaTime;
            night = nightAmount;
            var rng = new Random(s.Seed | 1u);
            s.Clock += dt;
            if (s.Mode == CreatureMode.Puppet)
            {
                Advance(ref s, b, dt);
                return;
            }
            s.ModeTime += dt;
            s.StageTime += dt;

            // Danger: a wizard or fire, or predators of this species (from further off when they're hunting it).
            // Asleep, it notices only at half the distance.
            var flee = threatIsFire ? math.max(b.FleeDistance, FireFear) : b.FleeDistance;
            var alert = s.Mode == CreatureMode.Sleep ? 0.5f : 1f;
            var away = s.Position.xz - threat;
            var playerDistance = math.length(away);
            var threatened = flee > 0f && playerDistance < flee * s.Scale * alert;
            var danger = threat;
            if (!threatened && b.Hunted && FindPredator(self, ref s, species, alert, out var predator))
            {
                threatened = true;
                danger = predator;
            }
            // Invited to play: someone has started a game of chase with this one.
            if (!threatened && b.Playfulness > 0f && Free(s.Mode) && FindPlaymate(self, ref s, out var playmate))
            {
                SetMode(ref s, CreatureMode.Play);
                s.Partner = playmate;
                s.Stage = 1;
                s.Timer = rng.NextFloat(12f, 25f);
                s.Focus = s.Leash > 0f ? s.Home : s.Position.xz;   // where the game is played
            }
            if (threatened)
            {
                if (s.Mode != CreatureMode.Flee)
                {
                    SetMode(ref s, CreatureMode.Flee);
                    s.Target = s.Position.xz + math.normalizesafe(s.Position.xz - danger, new float2(1f, 0f)) * 30f + rng.NextFloat2Direction() * 6f;
                }
                s.Focus = danger;
            }

            var targetSpeed = 0f;
            var desiredLook = 0f;
            var moving = false;
            float lie = 0f, headDown = 0f, bow = 0f, curl = 0f;
            var walk = b.WalkSpeed * s.Scale;
            var run = b.RunSpeed * s.Scale;

            // Keep an eye on a nearby wizard (busy feeding or drinking, only a close one).
            var watchPlayer = flee > 0f && playerDistance < flee * 2.5f;
            var busyWatch = flee > 0f && playerDistance < flee * 1.6f;
            var lookAtPlayer = math.clamp(WrapAngle(math.atan2(-away.x, -away.y) - s.Yaw), -1.2f, 1.2f);
            var idleLook = watchPlayer ? lookAtPlayer : math.sin(s.Clock * 0.7f + s.Seed % 7) * 0.5f;

            switch (s.Mode)
            {
                case CreatureMode.Idle:
                    s.Timer -= dt;
                    desiredLook = idleLook;
                    if (s.Timer <= 0f)
                        Decide(self, ref s, b, species, ref rng);
                    break;

                case CreatureMode.Wander:
                    targetSpeed = walk;
                    moving = true;
                    if (math.distance(s.Position.xz, s.Target) < 1f)
                        Pause(ref s, b, ref rng);
                    break;

                case CreatureMode.Flee:
                    targetSpeed = run;
                    moving = true;
                    if (threatened && math.distance(s.Position.xz, s.Target) < 2f)
                        s.Target = s.Position.xz + math.normalizesafe(s.Position.xz - s.Focus, new float2(1f, 0f)) * 30f;
                    else if (!threatened && (s.ModeTime > 3f || math.distance(s.Position.xz, s.Target) < 2f))
                        Pause(ref s, b, ref rng);
                    break;

                case CreatureMode.Graze:
                    // Head down, now and then looking up to chew and check around.
                    s.Timer -= dt;
                    var lookUp = math.frac(s.ModeTime / 7f + (s.Seed % 100) * 0.01f) > 0.78f;
                    headDown = lookUp ? 0.1f : 1f;
                    desiredLook = lookUp ? idleLook : 0f;
                    if (busyWatch)
                    {
                        headDown = 0.1f;
                        desiredLook = lookAtPlayer;
                    }
                    if (s.Timer <= 0f)
                        Pause(ref s, b, ref rng);
                    break;

                case CreatureMode.Drink:
                    if (s.Stage == 0)
                    {
                        targetSpeed = walk;
                        moving = true;
                        if (math.distance(s.Position.xz, s.Target) < 0.6f)
                        {
                            s.Stage = 1;
                            s.StageTime = 0f;
                            s.Timer = rng.NextFloat(6f, 14f);
                            s.Target = s.Focus;
                        }
                    }
                    else
                    {
                        // Face the water and lap at it.
                        moving = true;
                        s.Timer -= dt;
                        headDown = busyWatch ? 0.2f : 1f;
                        desiredLook = busyWatch ? lookAtPlayer : 0f;
                        if (s.Timer <= 0f)
                            Pause(ref s, b, ref rng);
                    }
                    break;

                case CreatureMode.Rest:
                    s.Timer -= dt;
                    // Upright bodies have no sitting pose yet: they rest on their feet.
                    lie = b.Upright ? 0f : 1f;
                    desiredLook = idleLook;
                    if (s.Timer <= 0f)
                        Pause(ref s, b, ref rng);
                    break;

                case CreatureMode.Sleep:
                    s.Timer -= dt;
                    // Animals curl up on the ground; upright bodies doze on their feet, head nodding.
                    lie = b.Upright ? 0f : 1f;
                    headDown = b.Upright ? 0.35f : 0.75f;
                    curl = b.Upright ? 0f : 1f;
                    // Wakes when the nap ends, or (drowsily) once its sleeping time is over.
                    if (s.Timer <= 0f || SleepTime(b) < 0.3f && rng.NextFloat() < dt * 0.05f)
                        Pause(ref s, b, ref rng);
                    break;

                case CreatureMode.Play:
                    Play(self, ref s, b, ref rng, ref targetSpeed, ref desiredLook, ref bow, ref moving);
                    break;

                case CreatureMode.Hunt:
                    Hunt(ref s, b, ref rng, ref targetSpeed, ref desiredLook, ref headDown, ref moving);
                    break;
            }

            // Getting up comes before going anywhere.
            targetSpeed *= math.saturate(1f - s.Lie * 1.5f);

            // Turn towards the target, then move; slow down while turning sharply.
            var turn = 0f;
            if (moving)
            {
                var to = s.Target - s.Position.xz;
                var wanted = WrapAngle(math.atan2(to.x, to.y) - s.Yaw);
                turn = math.clamp(wanted, -b.TurnRate * dt, b.TurnRate * dt);
                targetSpeed *= math.saturate(math.cos(wanted) * 1.5f);
            }
            s.Yaw = WrapAngle(s.Yaw + turn);
            s.YawRate = dt > 0f ? turn / dt : 0f;
            var urgent = s.Mode == CreatureMode.Flee || s.Mode == CreatureMode.Hunt || s.Mode == CreatureMode.Play;
            s.Speed += (targetSpeed - s.Speed) * math.min(1f, dt * (urgent ? 4f : 2.5f));

            // Stay out of water and off cliffs: stop (or, running, veer) when the way ahead is bad.
            var forward = new float2(math.sin(s.Yaw), math.cos(s.Yaw));
            var ahead = s.Position.xz + forward * math.max(1.2f, s.Speed * 0.5f) * s.Scale;
            if (s.Speed > 0.05f && !terrain.Walkable(ahead) || SlopeAhead(s.Position.xz, ahead) > 0.8f)
            {
                s.Speed *= 0.5f;
                if (s.Mode == CreatureMode.Flee || s.Mode == CreatureMode.Play && s.Stage == 1)
                    s.Target = s.Position.xz + new float2(-forward.y, forward.x) * (rng.NextBool() ? 20f : -20f);
                else if (s.Mode == CreatureMode.Wander || s.Mode == CreatureMode.Drink && s.Stage == 0)
                    Pause(ref s, b, ref rng);
            }
            Advance(ref s, b, dt);
            s.Look += (desiredLook - s.Look) * math.min(1f, dt * 3f);

            // Ease into postures: lying down takes a moment, springing up to run doesn't.
            var lieRate = lie > s.Lie ? 0.8f : urgent ? 5f : 1.5f;
            s.Lie += (lie - s.Lie) * math.min(1f, dt * lieRate);
            s.HeadDown += (headDown - s.HeadDown) * math.min(1f, dt * (urgent ? 5f : 2f));
            s.Bow += (bow - s.Bow) * math.min(1f, dt * 5f);
            s.Curl += (curl - s.Curl) * math.min(1f, dt * (curl > s.Curl ? 0.6f : 4f));
            s.Seed = rng.state;
        }

        // ------------------------------------------------------------------------------- choosing what to do

        /// <summary>How much this is its time to sleep (0..1).</summary>
        float SleepTime(CreatureBody b) =>
            b.Active == ActiveTime.Day ? night : b.Active == ActiveTime.Night ? 1f - night : 0f;

        void Decide(int self, ref CreatureState s, CreatureBody b, int species, ref Random rng)
        {
            var sleepy = SleepTime(b);
            var awake = 1f - sleepy * 0.8f;

            // Find company first: the options depend on it.
            var playmate = 0;
            if (b.Playfulness > 0f && sleepy < 0.5f)
                FindKin(self, ref s, species, 15f, out playmate);
            var prey = 0;
            if (b.Prey.Length > 0 && b.Hunger > 0f && sleepy < 0.5f)
                FindPrey(self, ref s, b, 35f, out prey);

            var wander = 1f * awake;
            var graze = b.Grazing * 2.5f * awake;
            var rest = b.Laziness * 0.7f;
            var sleep = sleepy > 0.5f ? 6f : b.Active == ActiveTime.Always ? b.Laziness * 0.4f : 0f;
            var drink = b.Thirst * 1.2f * awake;
            var play = playmate != 0 ? b.Playfulness * 2f : 0f;
            var hunt = prey != 0 ? b.Hunger * 2.5f : 0f;
            var roll = rng.NextFloat(wander + graze + rest + sleep + drink + play + hunt);

            if ((roll -= hunt) < 0f)
            {
                SetMode(ref s, CreatureMode.Hunt);
                s.Partner = prey;
                s.Timer = b.ChaseTime * rng.NextFloat(0.7f, 1.3f);
            }
            else if ((roll -= play) < 0f)
            {
                SetMode(ref s, CreatureMode.Play);
                s.Partner = playmate;
                s.Timer = rng.NextFloat(12f, 25f);
                s.Focus = s.Leash > 0f ? s.Home : s.Position.xz;   // where the game is played
            }
            else if ((roll -= sleep) < 0f)
            {
                SetMode(ref s, CreatureMode.Sleep);
                s.Timer = sleepy > 0.5f ? rng.NextFloat(90f, 300f) : rng.NextFloat(20f, 60f);
            }
            else if ((roll -= rest) < 0f)
            {
                SetMode(ref s, CreatureMode.Rest);
                s.Timer = rng.NextFloat(12f, 40f);
            }
            else if ((roll -= graze) < 0f)
            {
                SetMode(ref s, CreatureMode.Graze);
                s.Timer = rng.NextFloat(8f, 25f);
            }
            else if ((roll -= drink) < 0f && FindWater(ref s, b, ref rng, out var shore, out var water))
            {
                SetMode(ref s, CreatureMode.Drink);
                s.Target = shore;
                s.Focus = water;
            }
            else
            {
                var candidate = s.Position.xz + rng.NextFloat2Direction() * b.WanderRadius * rng.NextFloat(0.3f, 1f);
                // Animals with a home stay near it.
                if (s.Leash > 0f && math.distance(candidate, s.Home) > s.Leash)
                    candidate = s.Home + rng.NextFloat2Direction() * s.Leash * rng.NextFloat(0.1f, 0.8f);
                if (terrain.Walkable(candidate))
                {
                    SetMode(ref s, CreatureMode.Wander);
                    s.Target = candidate;
                }
                else
                    s.Timer = 0.5f;
            }
        }

        static bool Free(CreatureMode mode) =>
            mode == CreatureMode.Idle || mode == CreatureMode.Wander || mode == CreatureMode.Graze || mode == CreatureMode.Rest;

        static void SetMode(ref CreatureState s, CreatureMode mode)
        {
            s.Mode = mode;
            s.ModeTime = 0f;
            s.Stage = 0;
            s.StageTime = 0f;
            s.Partner = 0;
        }

        static void Pause(ref CreatureState s, CreatureBody b, ref Random rng)
        {
            SetMode(ref s, CreatureMode.Idle);
            s.Timer = rng.NextFloat(b.IdleTime.x, math.max(b.IdleTime.x, b.IdleTime.y));
        }

        // ------------------------------------------------------------------------------- two-animal activities

        /// <summary>
        /// Chase in fun. The chaser play-bows, then runs at its partner; catching up swaps the roles (the runner
        /// that has been running longest turns chaser, so both agree). Ends when the time is up or the partner
        /// stops playing.
        /// </summary>
        void Play(int self, ref CreatureState s, CreatureBody b, ref Random rng,
                  ref float targetSpeed, ref float desiredLook, ref float bow, ref bool moving)
        {
            s.Timer -= dt;
            if (!herd.TryFind(s.Partner, out var p) || s.Timer <= 0f ||
                s.ModeTime > 1.5f && (herd.State(p).Mode != CreatureMode.Play || herd.State(p).Partner != self))
            {
                if (rng.NextFloat() < 0.4f)
                {
                    SetMode(ref s, CreatureMode.Rest);
                    s.Timer = rng.NextFloat(10f, 25f);
                }
                else
                    Pause(ref s, b, ref rng);
                return;
            }

            var other = herd.State(p);
            var to = other.Position.xz - s.Position.xz;
            var distance = math.length(to);
            desiredLook = math.clamp(WrapAngle(math.atan2(to.x, to.y) - s.Yaw), -1.2f, 1.2f);
            moving = true;
            var walk = b.WalkSpeed * s.Scale;
            var run = b.RunSpeed * s.Scale;

            if (s.Stage == 0)
            {
                s.Target = other.Position.xz;
                if (s.StageTime < 1f)
                    bow = 1f;   // play bow, then off
                else
                {
                    targetSpeed = math.lerp(walk, run, 0.75f);
                    var reach = (b.BodyLength * s.Scale + herd.Body(p).BodyLength * other.Scale) * 0.6f + 0.3f;
                    if (distance < reach)
                    {
                        s.Stage = 1;
                        s.StageTime = 0f;
                    }
                }
            }
            else
            {
                // Both running: the one that's run longest turns round to chase.
                if (other.Stage == 1 && other.Partner == self && s.StageTime > other.StageTime && s.StageTime > 0.3f)
                {
                    s.Stage = 0;
                    s.StageTime = 0f;
                    return;
                }
                var away = math.normalizesafe(-to, new float2(1f, 0f));
                var side = new float2(-away.y, away.x) * math.sin(s.Clock * 1.3f + s.Seed % 11) * 0.6f;
                // Don't run off: past a dozen metres from the chaser, circle back round; and keep the game near
                // where it started (home, for animals that have one), turning back towards it further out.
                var dir = distance > 12f ? new float2(-away.y, away.x) : away + side;
                var toCentre = s.Focus - s.Position.xz;
                var wander = math.length(toCentre);
                var range = s.Leash > 0f ? s.Leash * 0.6f : 12f;
                if (wander > range)
                    dir = math.lerp(math.normalizesafe(dir), toCentre / wander, math.saturate((wander - range) / 6f));
                s.Target = s.Position.xz + math.normalizesafe(dir, away) * 8f;
                targetSpeed = math.lerp(walk, run, distance > 12f ? 0.3f : 0.6f);
            }
        }

        /// <summary>Stalks low towards its prey, then sprints; gives up when it's caught up or run out of puff.</summary>
        void Hunt(ref CreatureState s, CreatureBody b, ref Random rng,
                  ref float targetSpeed, ref float desiredLook, ref float headDown, ref bool moving)
        {
            s.Timer -= dt;
            if (!herd.TryFind(s.Partner, out var p) || s.Timer <= 0f)
            {
                Tired(ref s, ref rng);
                return;
            }
            var prey = herd.State(p);
            var to = prey.Position.xz - s.Position.xz;
            var distance = math.length(to);
            if (distance > 60f || distance < b.BodyLength * s.Scale + 0.5f)
            {
                Tired(ref s, ref rng);
                return;
            }
            s.Target = prey.Position.xz;
            desiredLook = math.clamp(WrapAngle(math.atan2(to.x, to.y) - s.Yaw), -1f, 1f);
            moving = true;
            if (distance > 14f && s.Stage == 0)
            {
                targetSpeed = b.WalkSpeed * s.Scale * 0.8f;
                headDown = 0.35f;
            }
            else
            {
                s.Stage = 1;
                targetSpeed = b.RunSpeed * s.Scale;
            }
        }

        static void Tired(ref CreatureState s, ref Random rng)
        {
            SetMode(ref s, CreatureMode.Rest);
            s.Timer = rng.NextFloat(15f, 40f);
        }

        // ------------------------------------------------------------------------------- seeing each other

        /// <summary>A predator of this species close by (further off if it's hunting this animal).</summary>
        bool FindPredator(int self, ref CreatureState s, int species, float alert, out float2 at)
        {
            at = default;
            var best = float.MaxValue;
            var c = Herd.Cell(s.Position.xz);
            for (int z = -2; z <= 2; z++)
            for (int x = -2; x <= 2; x++)
                foreach (var j in herd.In(c + new int2(x, z)))
                {
                    if (herd.Id(j) == self || !Hunts(herd.Body(j), species))
                        continue;
                    var other = herd.State(j);
                    var d = math.distance(other.Position.xz, s.Position.xz);
                    var range = (other.Mode == CreatureMode.Hunt && other.Partner == self ? 30f : 8f) * alert;
                    if (d < range && d < best)
                    {
                        best = d;
                        at = other.Position.xz;
                    }
                }
            return best < float.MaxValue;
        }

        static bool Hunts(CreatureBody b, int species)
        {
            for (int k = 0; k < b.Prey.Length; k++)
                if (b.Prey[k] == species)
                    return true;
            return false;
        }

        /// <summary>Someone close by who has started a game of chase with this animal.</summary>
        bool FindPlaymate(int self, ref CreatureState s, out int playmate)
        {
            playmate = 0;
            var c = Herd.Cell(s.Position.xz);
            for (int z = -1; z <= 1; z++)
            for (int x = -1; x <= 1; x++)
                foreach (var j in herd.In(c + new int2(x, z)))
                {
                    var other = herd.State(j);
                    if (other.Mode == CreatureMode.Play && other.Partner == self && other.Stage == 0)
                    {
                        playmate = herd.Id(j);
                        return true;
                    }
                }
            return false;
        }

        /// <summary>The nearest free animal of the same species within range.</summary>
        bool FindKin(int self, ref CreatureState s, int species, float range, out int found)
        {
            found = 0;
            var best = range;
            var c = Herd.Cell(s.Position.xz);
            var r = (int)math.ceil(range / Herd.CellSize);
            for (int z = -r; z <= r; z++)
            for (int x = -r; x <= r; x++)
                foreach (var j in herd.In(c + new int2(x, z)))
                {
                    if (herd.Id(j) == self || herd.Species(j) != species || !Free(herd.State(j).Mode))
                        continue;
                    var d = math.distance(herd.State(j).Position.xz, s.Position.xz);
                    if (d < best)
                    {
                        best = d;
                        found = herd.Id(j);
                    }
                }
            return found != 0;
        }

        /// <summary>The nearest animal it hunts within range.</summary>
        bool FindPrey(int self, ref CreatureState s, CreatureBody b, float range, out int found)
        {
            found = 0;
            var best = range;
            var c = Herd.Cell(s.Position.xz);
            var r = (int)math.ceil(range / Herd.CellSize);
            for (int z = -r; z <= r; z++)
            for (int x = -r; x <= r; x++)
                foreach (var j in herd.In(c + new int2(x, z)))
                {
                    if (herd.Id(j) == self || !Hunts(b, herd.Species(j)))
                        continue;
                    var d = math.distance(herd.State(j).Position.xz, s.Position.xz);
                    if (d < best)
                    {
                        best = d;
                        found = herd.Id(j);
                    }
                }
            return found != 0;
        }

        // ------------------------------------------------------------------------------- ground

        /// <summary>
        /// Looks around for water within 50 m: the spot to stand (on dry land, front end at the edge) and the
        /// water it faces.
        /// </summary>
        bool FindWater(ref CreatureState s, CreatureBody b, ref Random rng, out float2 shore, out float2 water)
        {
            shore = water = default;
            var start = rng.NextFloat(math.PI * 2f);
            for (int k = 0; k < 8; k++)
            {
                var a = start + k * math.PI * 0.25f;
                var dir = new float2(math.sin(a), math.cos(a));
                for (var d = 6f; d <= 50f; d += 8f)
                {
                    var p = s.Position.xz + dir * d;
                    if (!terrain.Water(p))
                        continue;
                    // Back up from the water to dry land.
                    var q = p;
                    for (int step = 0; step < 30 && !terrain.Walkable(q); step++)
                        q -= dir;
                    if (!terrain.Walkable(q))
                        break;
                    water = p;
                    shore = q + dir * 0.6f - dir * (b.BodyLength * 0.4f * s.Scale);
                    return true;
                }
            }
            return false;
        }

        /// <summary>Moves along the heading and advances the gait by the distance covered, so feet don't slide.</summary>
        void Advance(ref CreatureState s, CreatureBody b, float deltaTime)
        {
            var forward = new float2(math.sin(s.Yaw), math.cos(s.Yaw));
            s.Position.xz += forward * s.Speed * deltaTime;
            s.Position.y = terrain.Height(s.Position.xz);
            var run = math.smoothstep(b.WalkSpeed * 1.1f, math.max(b.WalkSpeed * 1.2f, b.RunSpeed * 0.7f), s.Speed / s.Scale);
            var stride = math.lerp(b.WalkStride, b.RunStride, run) * s.Scale;
            s.Phase = math.frac(s.Phase + s.Speed * deltaTime / stride);
            s.Sway += (-s.YawRate * 0.35f - s.Sway) * math.min(1f, deltaTime * 4f);
        }

        float SlopeAhead(float2 from, float2 to)
        {
            var rise = terrain.Height(to) - terrain.Height(from);
            return rise / math.max(math.distance(from, to), 0.1f);
        }

        static float WrapAngle(float a) => a - 2f * math.PI * math.floor((a + math.PI) / (2f * math.PI));
    }
}
