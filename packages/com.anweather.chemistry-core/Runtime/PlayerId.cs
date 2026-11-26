// A player's stable id (docs/design/player-saves.md, step 2; J-038): who a player is from session to session and host to
// host, kept in their player save. A type only, for now: the game still names players by their session ("p" and the
// client's number, NetChem.PlayerName), and what an id stands for (one per machine, or a profile each) waits on Andrew
// (the note's question 4). An id is never made from anything a session gives (a client number, a peer name): it's made
// once from random bytes the caller brings, so the core stays free of randomness, and is kept as written.
using System;
using System.Globalization;
using System.Linq;

namespace Chem
{
    public readonly struct PlayerId : IEquatable<PlayerId>, IComparable<PlayerId>
    {
        public const int MostLength = 64;
        public const int Bytes = 16;

        readonly string value;

        PlayerId(string value) => this.value = value;

        public string Value => value;

        public bool IsNone => value == null;

        public static PlayerId New(byte[] random)
        {
            if (random == null || random.Length != Bytes) throw new ArgumentException($"an id is made from {Bytes} random bytes", nameof(random));
            if (random.All(b => b == 0)) throw new ArgumentException("those bytes aren't random", nameof(random));
            return new PlayerId(string.Concat(random.Select(b => b.ToString("x2", CultureInfo.InvariantCulture))));
        }

        public static bool TryParse(string text, out PlayerId id)
        {
            id = default;
            if (Problem(text) != null) return false;
            id = new PlayerId(text);
            return true;
        }

        public static PlayerId Parse(string text) => TryParse(text, out var id) ? id : throw new FormatException(Problem(text));

        public static string Problem(string text)
        {
            if (string.IsNullOrEmpty(text)) return "a player id isn't empty";
            if (text.Length > MostLength) return $"a player id is at most {MostLength} characters";
            if (!text.All(c => (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-' || c == '_'))
                return "a player id is a-z, 0-9, '-' and '_'";
            if (IsSessionName(text)) return $"{text} is a session's name for a player, not a player id";
            return null;
        }

        static bool IsSessionName(string text) =>
            text == TriggerDef.SoloPlayer || (text.Length > 1 && text[0] == 'p' && text.Skip(1).All(c => c >= '0' && c <= '9'));

        public bool Equals(PlayerId other) => string.Equals(value, other.value, StringComparison.Ordinal);

        public override bool Equals(object obj) => obj is PlayerId other && Equals(other);

        public override int GetHashCode() => value == null ? 0 : StringComparer.Ordinal.GetHashCode(value);

        public int CompareTo(PlayerId other) => string.CompareOrdinal(value, other.value);

        public override string ToString() => value ?? "";

        public static bool operator ==(PlayerId a, PlayerId b) => a.Equals(b);

        public static bool operator !=(PlayerId a, PlayerId b) => !a.Equals(b);

        public static bool operator <(PlayerId a, PlayerId b) => a.CompareTo(b) < 0;

        public static bool operator <=(PlayerId a, PlayerId b) => a.CompareTo(b) <= 0;

        public static bool operator >(PlayerId a, PlayerId b) => a.CompareTo(b) > 0;

        public static bool operator >=(PlayerId a, PlayerId b) => a.CompareTo(b) >= 0;
    }
}
