// The lockstep wire (D19): a deterministic binary encoding every transport carries the same, and the frames that split a
// message too long for one send. Whole numbers go as varints (zigzag for signed), strings as UTF-8 with their length
// (null apart from empty, so a command comes back as it went), hashes as 8 fixed bytes. No floats: a command's payload
// is integers and strings only, so every peer reads the same values.
//
// A frame is a byte, then either the whole message (Whole) or a piece of one (Piece: the message's number from this
// sender, the piece's index and the count of pieces, then its bytes). The pieces of a message may arrive between other
// messages; one is handed on when its last piece is in.
//
// Not a port: the sandbox has no peers.
using System;
using System.Collections.Generic;
using System.Text;

namespace Chem.Lockstep
{
    /// <summary>Bytes that aren't a lockstep message: cut short, or of a kind or version this core doesn't know.</summary>
    public sealed class LockstepWireException : Exception
    {
        public LockstepWireException(string message) : base(message) { }
    }

    /// <summary>Writes a message: varints, strings and fixed 64-bit hashes, into a buffer that grows.</summary>
    public sealed class LockstepWriter
    {
        byte[] buffer = new byte[64];

        public int Length { get; private set; }

        public void Byte(byte b)
        {
            if (Length == buffer.Length) Array.Resize(ref buffer, buffer.Length * 2);
            buffer[Length++] = b;
        }

        public void Bool(bool b) => Byte(b ? (byte)1 : (byte)0);

        /// <summary>An unsigned varint: seven bits a byte, low first.</summary>
        public void Natural(uint n)
        {
            while (n >= 0x80)
            {
                Byte((byte)(n | 0x80));
                n >>= 7;
            }
            Byte((byte)n);
        }

        /// <summary>A signed varint, zigzagged so small negatives stay short.</summary>
        public void Number(int n) => Natural(unchecked((uint)((n << 1) ^ (n >> 31))));

        public void Hash(ulong n)
        {
            for (int i = 0; i < 8; i++) Byte((byte)(n >> (8 * i)));
        }

        /// <summary>A string's UTF-8 bytes after its length plus one; 0 is null.</summary>
        public void Text(string s)
        {
            if (s == null)
            {
                Natural(0);
                return;
            }
            var bytes = Encoding.UTF8.GetBytes(s);
            Natural((uint)bytes.Length + 1);
            Bytes(bytes);
        }

        public void Bytes(ReadOnlySpan<byte> bytes)
        {
            while (Length + bytes.Length > buffer.Length) Array.Resize(ref buffer, buffer.Length * 2);
            bytes.CopyTo(buffer.AsSpan(Length));
            Length += bytes.Length;
        }

        public ReadOnlySpan<byte> Span => buffer.AsSpan(0, Length);

        public byte[] ToArray() => Span.ToArray();
    }

    /// <summary>Reads what a <see cref="LockstepWriter"/> wrote, in the same order; LockstepWireException if it runs out.</summary>
    public sealed class LockstepReader
    {
        readonly byte[] bytes;
        int at;

        public LockstepReader(byte[] bytes, int from = 0)
        {
            this.bytes = bytes ?? throw new ArgumentNullException(nameof(bytes));
            at = from;
        }

        public bool AtEnd => at >= bytes.Length;

        public byte Byte()
        {
            if (at >= bytes.Length) throw new LockstepWireException("the message ends early");
            return bytes[at++];
        }

        public bool Bool() => Byte() != 0;

        public uint Natural()
        {
            uint n = 0;
            for (int shift = 0; shift < 35; shift += 7)
            {
                byte b = Byte();
                n |= (uint)(b & 0x7f) << shift;
                if (b < 0x80) return n;
            }
            throw new LockstepWireException("a number runs on past five bytes");
        }

        public int Number()
        {
            uint z = Natural();
            return unchecked((int)(z >> 1) ^ -(int)(z & 1));
        }

        public ulong Hash()
        {
            ulong n = 0;
            for (int i = 0; i < 8; i++) n |= (ulong)Byte() << (8 * i);
            return n;
        }

        public string Text()
        {
            uint n = Natural();
            if (n == 0) return null;
            int length = Count(n - 1);
            string s = Encoding.UTF8.GetString(bytes, at, length);
            at += length;
            return s;
        }

        /// <summary>A count read before that many things: refused if more bytes than are left would be needed.</summary>
        public int Count() => Count(Natural());

        int Count(uint n)
        {
            if (n > (uint)(bytes.Length - at)) throw new LockstepWireException("a length runs past the message's end");
            return (int)n;
        }

        /// <summary>The bytes from here to the end.</summary>
        public byte[] Rest()
        {
            var rest = new byte[bytes.Length - at];
            Array.Copy(bytes, at, rest, 0, rest.Length);
            at = bytes.Length;
            return rest;
        }
    }

    /// <summary>Splits a message into frames that fit a send, and puts a sender's pieces back together.</summary>
    public sealed class LockstepFrames
    {
        const byte Whole = 0, Piece = 1;
        /// <summary>A piece's frame before its bytes, at most: the kind, then three varints of up to five bytes.</summary>
        public const int PieceHeader = 16;
        /// <summary>The shortest send a transport may offer.</summary>
        public const int MinMessageBytes = 32;

        // The sender's: its next message's number.
        int nextMessage;
        // The receiver's: each sender's messages part in, by number.
        readonly Dictionary<(PeerId from, int message), byte[][]> partIn = new Dictionary<(PeerId, int), byte[][]>();

        /// <summary>The frames to send for <paramref name="message"/>, each at most <paramref name="maxBytes"/> long.</summary>
        public List<byte[]> Split(ReadOnlySpan<byte> message, int maxBytes)
        {
            if (maxBytes < MinMessageBytes) throw new ArgumentOutOfRangeException(nameof(maxBytes), $"a transport must take {MinMessageBytes} bytes a send at the least");
            var frames = new List<byte[]>();
            if (message.Length + 1 <= maxBytes)
            {
                var whole = new byte[message.Length + 1];
                whole[0] = Whole;
                message.CopyTo(whole.AsSpan(1));
                frames.Add(whole);
                return frames;
            }
            int each = maxBytes - PieceHeader, count = (message.Length + each - 1) / each, number = nextMessage++;
            for (int i = 0; i < count; i++)
            {
                var w = new LockstepWriter();
                w.Byte(Piece);
                w.Number(number);
                w.Number(i);
                w.Number(count);
                w.Bytes(message.Slice(i * each, Math.Min(each, message.Length - i * each)));
                frames.Add(w.ToArray());
            }
            return frames;
        }

        /// <summary>A frame from <paramref name="from"/>: true with the whole message it completes, false while pieces are still to come.</summary>
        public bool TryJoin(PeerId from, byte[] frame, out byte[] message)
        {
            message = null;
            if (frame == null || frame.Length == 0) throw new LockstepWireException("an empty frame");
            if (frame[0] == Whole)
            {
                message = frame.AsSpan(1).ToArray();
                return true;
            }
            if (frame[0] != Piece) throw new LockstepWireException($"a frame of kind {frame[0]}");
            var r = new LockstepReader(frame, 1);
            int number = r.Number(), index = r.Number(), count = r.Number();
            if (count <= 0 || index < 0 || index >= count) throw new LockstepWireException($"piece {index} of {count}");
            var key = (from, number);
            if (!partIn.TryGetValue(key, out var pieces)) partIn[key] = pieces = new byte[count][];
            if (pieces.Length != count) throw new LockstepWireException($"piece {index} of {count}, after pieces of {pieces.Length}");
            pieces[index] = r.Rest();
            if (Array.IndexOf(pieces, null) >= 0) return false;
            partIn.Remove(key);
            var whole = new LockstepWriter();
            foreach (var p in pieces) whole.Bytes(p);
            message = whole.ToArray();
            return true;
        }

        /// <summary>A peer went: its part-sent messages never finish.</summary>
        public void Forget(PeerId from)
        {
            var gone = new List<(PeerId, int)>();
            foreach (var key in partIn.Keys)
                if (key.from == from) gone.Add(key);
            foreach (var key in gone) partIn.Remove(key);
        }
    }

    /// <summary>64-bit FNV-1a over a string's UTF-16 code units: a hash window's parts.</summary>
    public static class Fnv1a64
    {
        public static ulong Hash(string s)
        {
            unchecked
            {
                ulong h = 0xcbf29ce484222325;
                foreach (char c in s ?? "") h = (h ^ c) * 0x100000001b3;
                return h;
            }
        }
    }
}
