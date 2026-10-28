using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

// The playtest bot's detectors that judge in code (playtest/DESIGN.md,
// section 3). Each is fed numbers once a frame by PlaytestBot and returns the
// candidates it sees; none touches Unity, so their rules are tested under
// dotnet test. Positions are metres, y up, as plain floats.
namespace Playtest
{
    /// <summary>The wizard: NaN, fell through the ground, left the world, airborne too long, stuck.</summary>
    public sealed class WizardWatch
    {
        public const float FellBelow = 2f, StuckMetres = 0.5f;
        public const int AirborneFrames = 300, StuckFrames = 300;

        readonly float minX, minZ, maxX, maxZ;
        int airborneSince = -1;
        int pushingSince = -1;
        float pushX, pushZ;
        bool reportedFell, reportedOut, reportedNaN, reportedAir;

        /// <summary>The world's extent on the ground (metres), past which the wizard is out of bounds.</summary>
        public WizardWatch(float minX, float minZ, float maxX, float maxZ)
        {
            this.minX = minX;
            this.minZ = minZ;
            this.maxX = maxX;
            this.maxZ = maxZ;
        }

        /// <summary>One frame of the wizard, as the bot sees it.</summary>
        public struct Sample
        {
            public float X, Y, Z;
            /// <summary>The ground's height under the wizard.</summary>
            public float Ground;
            public bool Grounded;
            /// <summary>The bot is pushing the stick.</summary>
            public bool Pushing;
            /// <summary>The bot moved the wizard itself this frame (a teleport), which resets "stuck".</summary>
            public bool BotMoved;
            /// <summary>Where it is, for the signature ("stuck near the tower").</summary>
            public string Place;
        }

        /// <summary>One frame's candidates.</summary>
        public IEnumerable<Candidate> Frame(int frame, Sample s)
        {
            float x = s.X, y = s.Y, z = s.Z, ground = s.Ground;
            string place = s.Place;
            bool grounded = s.Grounded, pushing = s.Pushing, botMoved = s.BotMoved;
            if (float.IsNaN(x) || float.IsNaN(y) || float.IsNaN(z) || float.IsInfinity(x) || float.IsInfinity(y) || float.IsInfinity(z))
            {
                if (!Once(ref reportedNaN)) yield break;
                yield return new Candidate("wizard-nan", place, Category.GameBreaking, frame, "the wizard's position is not a number");
                yield break;
            }
            if (y < ground - FellBelow && Once(ref reportedFell))
                yield return new Candidate("wizard-fell", place, Category.GameBreaking, frame,
                    string.Format(CultureInfo.InvariantCulture, "the wizard is {0:0.0} m under the ground", ground - y));
            if ((x < minX || z < minZ || x > maxX || z > maxZ) && Once(ref reportedOut))
                yield return new Candidate("wizard-out", place, Category.GameBreaking, frame,
                    string.Format(CultureInfo.InvariantCulture, "the wizard left the world at ({0:0}, {1:0})", x, z));

            if (grounded) airborneSince = -1;
            else if (airborneSince < 0) airborneSince = frame;
            if (airborneSince >= 0 && frame - airborneSince >= AirborneFrames && Once(ref reportedAir))
                yield return new Candidate("wizard-airborne", place, Category.Gameplay, frame, "the wizard has been in the air for 10 s");

            foreach (var c in Stuck(frame, x, z, pushing, botMoved, place)) yield return c;
        }

        // Pushing the stick for 10 s without getting half a metre: stuck (often the bot's own steering).
        IEnumerable<Candidate> Stuck(int frame, float x, float z, bool pushing, bool botMoved, string place)
        {
            if (!pushing || botMoved)
            {
                pushingSince = -1;
                yield break;
            }
            float dx = x - pushX, dz = z - pushZ;
            if (pushingSince < 0 || dx * dx + dz * dz > StuckMetres * StuckMetres)
            {
                pushingSince = frame;
                pushX = x;
                pushZ = z;
                yield break;
            }
            if (frame - pushingSince < StuckFrames) yield break;
            pushingSince = frame;
            yield return new Candidate("wizard-stuck", place, Category.Gameplay, frame, "the wizard pushed for 10 s without getting anywhere", lowConfidence: true);
        }

        static bool Once(ref bool reported)
        {
            if (reported) return false;
            reported = true;
            return true;
        }
    }

    /// <summary>Frame times: single spikes, and the run's 95th percentile against a budget.</summary>
    public sealed class FrameWatch
    {
        public const float SpikeMs = 100f;

        readonly List<float> ms = new List<float>();
        readonly HashSet<string> spikedAt = new HashSet<string>();

        public IReadOnlyList<float> Times => ms;

        /// <summary>Frames after a screenshot of the bot's own that a spike isn't counted on.</summary>
        public const int AfterShotFrames = 3;
        int quietUntil = -1;

        /// <summary>
        /// The bot took a screenshot on this frame: its readback, encode and write cost 40-60 ms
        /// and more, and the next few frames' spikes are the bot's, not the game's (the Mac's profile).
        /// </summary>
        public void ShotTaken(int frame) => quietUntil = frame + AfterShotFrames;

        /// <summary>One frame's real time; a spike is reported once per place.</summary>
        public IEnumerable<Candidate> Frame(int frame, float frameMs, string place)
        {
            ms.Add(frameMs);
            // The first frames load the world: no spikes counted there; nor just after a screenshot.
            if (frame > 60 && frame > quietUntil && frameMs > SpikeMs && spikedAt.Add(place ?? ""))
                yield return new Candidate("frame-spike", place, Category.Performance, frame,
                    string.Format(CultureInfo.InvariantCulture, "one frame took {0:0} ms", frameMs));
        }

        public float Percentile(double p)
        {
            if (ms.Count == 0) return 0f;
            var sorted = ms.OrderBy(v => v).ToList();
            return sorted[Math.Min(sorted.Count - 1, (int)Math.Ceiling(p * sorted.Count) - 1)];
        }
    }

    /// <summary>The player's log: errors and exceptions, keyed by their type and first stack frame.</summary>
    public static class LogWatch
    {
        /// <summary>A candidate for an error or exception log line, or null for anything else.</summary>
        public static Candidate Line(int frame, string message, string stack, bool isError)
        {
            if (!isError || string.IsNullOrEmpty(message)) return null;
            string first = (stack ?? "").Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? "";
            // "Type: details" keeps the type; the frame drops its arguments and addresses.
            string kind = message.Split(':')[0].Trim();
            int paren = first.IndexOf('(');
            string where = paren > 0 ? first.Substring(0, paren).Trim() : first;
            return new Candidate("exception", where.Length > 0 ? $"{kind}@{where}" : kind, Category.GameBreaking, frame, Short(message));
        }

        static string Short(string s) => s.Length <= 300 ? s : s.Remove(300) + "...";
    }

    /// <summary>The sound, from the tap on the listener: clipping, NaN, and silence while things should be playing.</summary>
    public sealed class AudioWatch
    {
        public const float ClipAt = 0.99f;
        public const int SilentFrames = 600;

        int silentSince = -1;
        bool clipped, nan, silent;

        /// <summary>One frame: the loudest sample since the last frame, whether one was NaN, and whether any source is playing audibly.</summary>
        public IEnumerable<Candidate> Frame(int frame, float peak, bool sawNaN, bool sourcesPlaying, string place)
        {
            if (sawNaN && !nan)
            {
                nan = true;
                yield return new Candidate("audio-nan", place, Category.Sound, frame, "the mixed sound had a NaN sample");
            }
            if (peak >= ClipAt && !clipped)
            {
                clipped = true;
                yield return new Candidate("audio-clip", place, Category.Sound, frame,
                    string.Format(CultureInfo.InvariantCulture, "the sound clipped (peak {0:0.000})", peak));
            }
            if (!sourcesPlaying || peak > 0f) silentSince = -1;
            else if (silentSince < 0) silentSince = frame;
            if (silentSince >= 0 && frame - silentSince >= SilentFrames && !silent)
            {
                silent = true;
                yield return new Candidate("audio-silent", place, Category.Sound, frame, "sounds were playing but nothing was heard for 20 s");
            }
        }
    }

    /// <summary>A screenshot's pixels: missing shaders (magenta), a black or blown-out frame.</summary>
    public static class ImageCheck
    {
        public const double MagentaShare = 0.01, Dark = 8, Bright = 247;

        /// <summary>Candidates for one screenshot, given its pixels as r, g, b bytes in a row. At night (darkExpected) a black picture is fine.</summary>
        public static IEnumerable<Candidate> Pixels(int frame, byte[] rgb, string place, bool darkExpected = false)
        {
            if (rgb == null || rgb.Length < 3) yield break;
            int n = rgb.Length / 3, magenta = 0;
            double sum = 0;
            for (int i = 0; i < n; i++)
            {
                byte r = rgb[3 * i], g = rgb[3 * i + 1], b = rgb[3 * i + 2];
                sum += (r + g + b) / 3.0;
                if (r > 200 && b > 200 && g < 60) magenta++;
            }
            double mean = sum / n;
            if (magenta > n * MagentaShare)
                yield return new Candidate("image-magenta", place, Category.Visual, frame,
                    string.Format(CultureInfo.InvariantCulture, "{0:0.0}% of the picture is magenta (a missing shader?)", 100.0 * magenta / n));
            if (mean < Dark && !darkExpected)
                yield return new Candidate("image-black", place, Category.Visual, frame, "the picture is black");
            else if (mean > Bright)
                yield return new Candidate("image-blown", place, Category.Visual, frame, "the picture is white");
        }
    }

    /// <summary>Physics props: NaN, flung, lost under the ground or far away, awake in an inactive chunk.</summary>
    public sealed class PropWatch
    {
        public const float FlungSpeed = 50f, LostMetres = 200f;

        readonly HashSet<string> reported = new HashSet<string>();

        /// <summary>One prop on one frame.</summary>
        public struct Sample
        {
            /// <summary>ThingId's id, and the kind of thing (the signature's key).</summary>
            public string Id, Kind;
            public float X, Y, Z, Speed;
            /// <summary>The ground's height under it.</summary>
            public float Ground;
            /// <summary>Where it spawned, on the ground.</summary>
            public float SpawnX, SpawnZ;
            public bool Asleep, ChunkActive;
        }

        /// <summary>One prop's candidates on one frame; each problem is reported once per prop.</summary>
        public IEnumerable<Candidate> Frame(int frame, Sample p)
        {
            string id = p.Id, kind = p.Kind;
            float x = p.X, y = p.Y, z = p.Z, speed = p.Speed, ground = p.Ground;
            if (float.IsNaN(x) || float.IsNaN(y) || float.IsNaN(z))
            {
                if (reported.Add(id + "nan"))
                    yield return new Candidate("prop-nan", kind, Category.Physics, frame, $"prop {id}'s position is not a number");
                yield break;
            }
            if (speed > FlungSpeed && reported.Add(id + "flung"))
                yield return new Candidate("prop-flung", kind, Category.Physics, frame,
                    string.Format(CultureInfo.InvariantCulture, "prop {0} flew off at {1:0} m/s", id, speed));
            float dx = x - p.SpawnX, dz = z - p.SpawnZ;
            if ((y < ground - WizardWatch.FellBelow || dx * dx + dz * dz > LostMetres * LostMetres) && reported.Add(id + "lost"))
                yield return new Candidate("prop-lost", kind, Category.Physics, frame, $"prop {id} is under the ground or far from where it started");
            if (!p.Asleep && !p.ChunkActive && reported.Add(id + "awake"))
                yield return new Candidate("prop-awake-inactive", kind, Category.Physics, frame, $"prop {id} is awake in an inactive chunk");
        }
    }
}
