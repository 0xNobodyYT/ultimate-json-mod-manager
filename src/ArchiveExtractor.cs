using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using System.Xml;
using System.Xml.Linq;
using System.Xml.XPath;

namespace CdJsonModManager
{
    internal static class ArchiveExtractor
    {
        // ============================================================
        // LZ4 block encoder.
        //
        // Crimson Desert accepts normal LZ4 block payloads for generated
        // overlays. A literal-only block is technically valid, but encrypted
        // overlay files have proven less tolerant in the game loader, so this
        // emits real match sequences compatible with common LZ4 libraries.
        // ============================================================
        public static byte[] Lz4BlockCompress(byte[] data)
        {
            if (data == null) data = new byte[0];
            int len = data.Length;
            if (len == 0) return new byte[] { 0x00 };

            var output = new List<byte>(Math.Max(16, len / 2));
            var hash = new int[1 << 16];
            for (int n = 0; n < hash.Length; n++) hash[n] = -1;

            int anchor = 0;
            int i = 0;
            int matchLimit = len - 12;
            while (i <= matchLimit)
            {
                uint sequence = ReadU32(data, i);
                int h = (int)(((sequence * 2654435761u) >> 16) & 0xFFFF);
                int match = hash[h];
                hash[h] = i;

                if (match >= 0 && i - match <= 0xFFFF && ReadU32(data, match) == sequence)
                {
                    int tokenIndex = output.Count;
                    output.Add(0);

                    int literalLength = i - anchor;
                    int token = Math.Min(literalLength, 15) << 4;
                    if (literalLength >= 15) WriteLength(output, literalLength - 15);
                    for (int p = anchor; p < i; p++) output.Add(data[p]);

                    int offset = i - match;
                    output.Add((byte)(offset & 0xFF));
                    output.Add((byte)((offset >> 8) & 0xFF));

                    i += 4;
                    match += 4;
                    int matchLength = 4;
                    while (i < len && data[i] == data[match])
                    {
                        i++;
                        match++;
                        matchLength++;
                    }

                    int encodedMatchLength = matchLength - 4;
                    token |= Math.Min(encodedMatchLength, 15);
                    output[tokenIndex] = (byte)token;
                    if (encodedMatchLength >= 15) WriteLength(output, encodedMatchLength - 15);

                    anchor = i;
                    continue;
                }

                i++;
            }

            WriteLastLiterals(output, data, anchor, len - anchor);
            return output.ToArray();
        }

        private static uint ReadU32(byte[] data, int offset)
        {
            return (uint)(data[offset]
                | (data[offset + 1] << 8)
                | (data[offset + 2] << 16)
                | (data[offset + 3] << 24));
        }

        private static void WriteLength(List<byte> output, int length)
        {
            while (length >= 255)
            {
                output.Add(255);
                length -= 255;
            }
            output.Add((byte)length);
        }

        private static void WriteLastLiterals(List<byte> output, byte[] data, int start, int length)
        {
            int token = Math.Min(length, 15) << 4;
            output.Add((byte)token);
            if (length >= 15) WriteLength(output, length - 15);
            for (int p = start; p < start + length; p++) output.Add(data[p]);
        }

        public static byte[] Lz4BlockDecompressPublic(byte[] data, uint originalSize)
        {
            return Lz4BlockDecompress(data, originalSize);
        }

        private const uint ChachaHashInit = 0x000C5EDEu;
        private const uint ChachaIvXor = 0x60616263u;
        private static readonly uint[] ChachaKeyDeltas = new uint[]
        {
            0x00000000u, 0x0A0A0A0Au, 0x0C0C0C0Cu, 0x06060606u,
            0x0E0E0E0Eu, 0x0A0A0A0Au, 0x06060606u, 0x02020202u
        };

        public static byte[] CryptChaCha20ByFileName(byte[] data, string fileName)
        {
            if (data == null) data = new byte[0];
            var basename = (Path.GetFileName(fileName) ?? fileName ?? "").ToLowerInvariant();
            var seed = HashLittle(Encoding.UTF8.GetBytes(basename), ChachaHashInit);
            var keyBase = seed ^ ChachaIvXor;
            var key = ChachaKeyDeltas.Select(delta => keyBase ^ delta).ToArray();
            var nonce = new[] { seed, seed, seed, seed };
            return ChaCha20Xor(data, key, nonce);
        }

        private static byte[] ChaCha20Xor(byte[] input, uint[] key, uint[] nonce)
        {
            var output = new byte[input.Length];
            var constants = new uint[] { 0x61707865u, 0x3320646Eu, 0x79622D32u, 0x6B206574u };
            var state = new uint[16];
            Array.Copy(constants, 0, state, 0, 4);
            Array.Copy(key, 0, state, 4, 8);
            Array.Copy(nonce, 0, state, 12, 4);

            var offset = 0;
            while (offset < input.Length)
            {
                var block = ChaCha20Block(state);
                var take = Math.Min(64, input.Length - offset);
                for (int i = 0; i < take; i++) output[offset + i] = (byte)(input[offset + i] ^ block[i]);
                offset += take;
                unchecked
                {
                    state[12]++;
                    if (state[12] == 0) state[13]++;
                }
            }
            return output;
        }

        private static byte[] ChaCha20Block(uint[] state)
        {
            var x = new uint[16];
            Array.Copy(state, x, 16);
            for (int i = 0; i < 10; i++)
            {
                QuarterRound(x, 0, 4, 8, 12);
                QuarterRound(x, 1, 5, 9, 13);
                QuarterRound(x, 2, 6, 10, 14);
                QuarterRound(x, 3, 7, 11, 15);
                QuarterRound(x, 0, 5, 10, 15);
                QuarterRound(x, 1, 6, 11, 12);
                QuarterRound(x, 2, 7, 8, 13);
                QuarterRound(x, 3, 4, 9, 14);
            }

            var bytes = new byte[64];
            for (int i = 0; i < 16; i++)
            {
                var value = unchecked(x[i] + state[i]);
                bytes[i * 4] = (byte)value;
                bytes[i * 4 + 1] = (byte)(value >> 8);
                bytes[i * 4 + 2] = (byte)(value >> 16);
                bytes[i * 4 + 3] = (byte)(value >> 24);
            }
            return bytes;
        }

        private static void QuarterRound(uint[] x, int a, int b, int c, int d)
        {
            unchecked
            {
                x[a] += x[b]; x[d] = Rotl(x[d] ^ x[a], 16);
                x[c] += x[d]; x[b] = Rotl(x[b] ^ x[c], 12);
                x[a] += x[b]; x[d] = Rotl(x[d] ^ x[a], 8);
                x[c] += x[d]; x[b] = Rotl(x[b] ^ x[c], 7);
            }
        }

        private static uint Rotl(uint value, int bits)
        {
            return (value << bits) | (value >> (32 - bits));
        }

        private static uint HashLittle(byte[] data, uint initval)
        {
            unchecked
            {
                var length = data != null ? data.Length : 0;
                var remaining = length;
                uint a = 0xDEADBEEFu + (uint)length + initval;
                uint b = a;
                uint c = a;
                var offset = 0;
                while (remaining > 12)
                {
                    a += BitConverter.ToUInt32(data, offset);
                    b += BitConverter.ToUInt32(data, offset + 4);
                    c += BitConverter.ToUInt32(data, offset + 8);
                    Mix(ref a, ref b, ref c);
                    offset += 12;
                    remaining -= 12;
                }

                var tail = new byte[12];
                if (remaining > 0) Array.Copy(data, offset, tail, 0, remaining);
                if (remaining >= 12) c += BitConverter.ToUInt32(tail, 8);
                else if (remaining >= 9) c += BitConverter.ToUInt32(tail, 8) & (0xFFFFFFFFu >> (8 * (12 - remaining)));
                if (remaining >= 8) b += BitConverter.ToUInt32(tail, 4);
                else if (remaining >= 5) b += BitConverter.ToUInt32(tail, 4) & (0xFFFFFFFFu >> (8 * (8 - remaining)));
                if (remaining >= 4) a += BitConverter.ToUInt32(tail, 0);
                else if (remaining >= 1) a += BitConverter.ToUInt32(tail, 0) & (0xFFFFFFFFu >> (8 * (4 - remaining)));
                else return c;
                Final(ref a, ref b, ref c);
                return c;
            }
        }

        private static void Mix(ref uint a, ref uint b, ref uint c)
        {
            unchecked
            {
                a -= c; a ^= Rotl(c, 4); c += b;
                b -= a; b ^= Rotl(a, 6); a += c;
                c -= b; c ^= Rotl(b, 8); b += a;
                a -= c; a ^= Rotl(c, 16); c += b;
                b -= a; b ^= Rotl(a, 19); a += c;
                c -= b; c ^= Rotl(b, 4); b += a;
            }
        }

        private static void Final(ref uint a, ref uint b, ref uint c)
        {
            unchecked
            {
                c ^= b; c -= Rotl(b, 14);
                a ^= c; a -= Rotl(c, 11);
                b ^= a; b -= Rotl(a, 25);
                c ^= b; c -= Rotl(b, 16);
                a ^= c; a -= Rotl(c, 4);
                b ^= a; b -= Rotl(a, 14);
                c ^= b; c -= Rotl(b, 24);
            }
        }

        // ============================================================
        // PAMT parse + entry-section byte offset (so callers can patch
        // entries in place by computing entrySectionStart + idx*20).
        // Otherwise identical to ParsePamt.
        // ============================================================
        public static PamtParseResult ParsePamtFull(string pamtPath, string pazDir)
        {
            var data = File.ReadAllBytes(pamtPath);
            var pamtStem = Path.GetFileNameWithoutExtension(pamtPath);
            var off = 4;
            var pazCount = ReadU32(data, ref off);
            off += 8;
            for (var i = 0; i < pazCount; i++)
            {
                off += 8;
                if (i < pazCount - 1) off += 4;
            }

            var folderSize = ReadU32(data, ref off);
            var folderEnd = off + (int)folderSize;
            var folderPrefix = "";
            while (off < folderEnd)
            {
                var parent = ReadU32(data, ref off);
                var slen = data[off++];
                var name = Encoding.UTF8.GetString(data, off, slen);
                off += slen;
                if (parent == 0xFFFFFFFF) folderPrefix = name;
            }

            var nodeSize = ReadU32(data, ref off);
            var nodeStart = off;
            var nodes = new Dictionary<uint, Tuple<uint, string>>();
            while (off < nodeStart + nodeSize)
            {
                var rel = (uint)(off - nodeStart);
                var parent = ReadU32(data, ref off);
                var slen = data[off++];
                var name = Encoding.UTF8.GetString(data, off, slen);
                off += slen;
                nodes[rel] = Tuple.Create(parent, name);
            }

            Func<uint, string> buildPath = nodeRef =>
            {
                var parts = new List<string>();
                var cur = nodeRef;
                var guard = 0;
                while (cur != 0xFFFFFFFF && guard++ < 64 && nodes.ContainsKey(cur))
                {
                    var node = nodes[cur];
                    parts.Add(node.Item2);
                    cur = node.Item1;
                }
                parts.Reverse();
                return string.Concat(parts);
            };

            var folderCount = ReadU32(data, ref off);
            off += 4 + (int)folderCount * 16;

            var entrySectionStart = off;
            var entries = new List<PazEntry>();
            while (off + 20 <= data.Length)
            {
                var nodeRef = ReadU32(data, ref off);
                var pazOffset = ReadU32(data, ref off);
                var compSize = ReadU32(data, ref off);
                var origSize = ReadU32(data, ref off);
                var flags = ReadU32(data, ref off);
                var pazIndex = (int)(flags & 0xFF);
                var nodePath = buildPath(nodeRef);
                var fullPath = string.IsNullOrEmpty(folderPrefix) ? nodePath : folderPrefix + "/" + nodePath;
                var pazNum = int.Parse(pamtStem) + pazIndex;
                entries.Add(new PazEntry
                {
                    Path = fullPath,
                    PazFile = Path.Combine(pazDir, pazNum + ".paz"),
                    Offset = pazOffset,
                    CompSize = compSize,
                    OrigSize = origSize,
                    Flags = flags
                });
            }

            return new PamtParseResult { Entries = entries, EntrySectionStart = entrySectionStart, PamtBytes = data };
        }

        public static void WriteU32LE(byte[] data, int offset, uint value)
        {
            data[offset] = (byte)(value & 0xFF);
            data[offset + 1] = (byte)((value >> 8) & 0xFF);
            data[offset + 2] = (byte)((value >> 16) & 0xFF);
            data[offset + 3] = (byte)((value >> 24) & 0xFF);
        }

        // ============================================================
        // 32-bit hash candidates - diagnostic harness.
        // We don't yet know which algorithm Crimson Desert uses for the
        // 4-byte field at PAMT offset 16 + i*12 + 0. The diagnostic
        // computes all common 32-bit hashes over the paz file (full + a
        // few windows) and prints them so we can identify the match.
        // Once the algorithm is known these can be trimmed down.
        // ============================================================

        private static uint[] _crc32IeeeTable;
        private static uint[] _crc32CTable;

        public static uint[] PublicCrcTable(uint poly) { return BuildCrcTable(poly); }

        // ============================================================
        // Bob Jenkins' lookup3 / hashlittle - used by Pearl Abyss engine.
        // Reference: http://burtleburtle.net/bob/c/lookup3.c
        // Returns just the primary 32-bit hash 'c'.
        // ============================================================
        public static uint HashLittle(byte[] data, int offset, int length, uint initval)
        {
            uint a, b, c;
            a = b = c = 0xdeadbeefu + (uint)length + initval;

            int i = offset;
            int remaining = length;
            while (remaining > 12)
            {
                a += (uint)data[i] | ((uint)data[i + 1] << 8) | ((uint)data[i + 2] << 16) | ((uint)data[i + 3] << 24);
                b += (uint)data[i + 4] | ((uint)data[i + 5] << 8) | ((uint)data[i + 6] << 16) | ((uint)data[i + 7] << 24);
                c += (uint)data[i + 8] | ((uint)data[i + 9] << 8) | ((uint)data[i + 10] << 16) | ((uint)data[i + 11] << 24);
                // mix(a,b,c)
                a -= c; a ^= Rotl(c, 4); c += b;
                b -= a; b ^= Rotl(a, 6); a += c;
                c -= b; c ^= Rotl(b, 8); b += a;
                a -= c; a ^= Rotl(c, 16); c += b;
                b -= a; b ^= Rotl(a, 19); a += c;
                c -= b; c ^= Rotl(b, 4); b += a;
                i += 12; remaining -= 12;
            }
            // Tail (1..12 bytes)
            switch (remaining)
            {
                case 12: c += (uint)data[i + 11] << 24; goto case 11;
                case 11: c += (uint)data[i + 10] << 16; goto case 10;
                case 10: c += (uint)data[i + 9] << 8; goto case 9;
                case 9: c += (uint)data[i + 8]; goto case 8;
                case 8: b += (uint)data[i + 7] << 24; goto case 7;
                case 7: b += (uint)data[i + 6] << 16; goto case 6;
                case 6: b += (uint)data[i + 5] << 8; goto case 5;
                case 5: b += (uint)data[i + 4]; goto case 4;
                case 4: a += (uint)data[i + 3] << 24; goto case 3;
                case 3: a += (uint)data[i + 2] << 16; goto case 2;
                case 2: a += (uint)data[i + 1] << 8; goto case 1;
                case 1: a += (uint)data[i]; break;
                case 0: return c;
            }
            // final(a,b,c)
            c ^= b; c -= Rotl(b, 14);
            a ^= c; a -= Rotl(c, 11);
            b ^= a; b -= Rotl(a, 25);
            c ^= b; c -= Rotl(b, 16);
            a ^= c; a -= Rotl(c, 4);
            b ^= a; b -= Rotl(a, 14);
            c ^= b; c -= Rotl(b, 24);
            return c;
        }

        // For huge files, allocate the whole file into memory and call HashLittle once.
        public static uint HashLittleFile(string path, uint initval)
        {
            var buf = File.ReadAllBytes(path);
            return HashLittle(buf, 0, buf.Length, initval);
        }

        // ============================================================
        // Pearl Abyss checksum (used by Crimson Desert).
        // Pearl Abyss archive checksum routine used for PAZ/PAMT/PAPGT integrity fields.
        //
        // Variant of Bob Jenkins lookup3 with a custom init and a
        // custom finalisation that mixes Rotl + Rotr.
        //   PA_MAGIC = 558_228_019 (= 0x21456BB3)
        //   init: a = b = c = (uint)(length - PA_MAGIC)
        //   mix:  same six rotations as standard lookup3 (4,6,8,16,19,4)
        //   final: 8-step custom mix (see code below).
        //
        // Used to compute:
        //   * per-paz Crc       - PaChecksum(<entire .paz file>),
        //                         written into 0.pamt at row+4 of each paz
        //   * PAMT HeaderCrc    - PaChecksum(pamt[12..end]),
        //                         written at 0.pamt[0..3]
        //   * papgt HeaderCrc   - PaChecksum(papgt_body[entries+stringtable]),
        //                         written at meta/0.papgt[4..7]
        //   * papgt PamtCrc[i]  - copy of the corresponding archive's PAMT HeaderCrc
        // ============================================================
        public const uint PA_MAGIC = 558228019u;

        public static uint PaChecksum(byte[] data, int offset, int length)
        {
            if (length == 0) return 0u;
            uint a, b, c;
            a = b = c = (uint)(length - PA_MAGIC);
            int p = offset;
            int rem = length;
            while (rem > 12)
            {
                b += (uint)data[p] | ((uint)data[p + 1] << 8) | ((uint)data[p + 2] << 16) | ((uint)data[p + 3] << 24);
                a += (uint)data[p + 4] | ((uint)data[p + 5] << 8) | ((uint)data[p + 6] << 16) | ((uint)data[p + 7] << 24);
                c += (uint)data[p + 8] | ((uint)data[p + 9] << 8) | ((uint)data[p + 10] << 16) | ((uint)data[p + 11] << 24);
                b -= c; b ^= Rotl(c, 4); c += a;
                a -= b; a ^= Rotl(b, 6); b += c;
                c -= a; c ^= Rotl(a, 8); a += b;
                b -= c; b ^= Rotl(c, 16); c += a;
                a -= b; a ^= Rotl(b, 19); b += c;
                c -= a; c ^= Rotl(a, 4); a += b;
                p += 12; rem -= 12;
            }
            if (rem >= 12) c += (uint)data[p + 11] << 24;
            if (rem >= 11) c += (uint)data[p + 10] << 16;
            if (rem >= 10) c += (uint)data[p + 9] << 8;
            if (rem >= 9) c += (uint)data[p + 8];
            if (rem >= 8) a += (uint)data[p + 7] << 24;
            if (rem >= 7) a += (uint)data[p + 6] << 16;
            if (rem >= 6) a += (uint)data[p + 5] << 8;
            if (rem >= 5) a += (uint)data[p + 4];
            if (rem >= 4) b += (uint)data[p + 3] << 24;
            if (rem >= 3) b += (uint)data[p + 2] << 16;
            if (rem >= 2) b += (uint)data[p + 1] << 8;
            if (rem >= 1) b += (uint)data[p];
            // Custom finalisation - note the Rotr at steps 3 and 8.
            uint t1 = (a ^ c) - Rotl(a, 14);
            uint t2 = (b ^ t1) - Rotl(t1, 11);
            uint t3 = (t2 ^ a) - Rotr(t2, 7);
            uint t4 = (t3 ^ t1) - Rotl(t3, 16);
            uint t5 = Rotl(t4, 4);
            uint t6 = (t2 ^ t4) - t5;
            uint t7 = (t6 ^ t3) - Rotl(t6, 14);
            return (t7 ^ t4) - Rotr(t7, 8);
        }

        private static uint Rotr(uint x, int k) { return (x >> k) | (x << (32 - k)); }

        // Full-file PaChecksum - loads the whole file (up to ~2 GB) and hashes it in one pass.
        public static uint PaChecksumFile(string path)
        {
            var data = File.ReadAllBytes(path);
            return PaChecksum(data, 0, data.Length);
        }

        private static uint[] BuildCrcTable(uint poly)
        {
            var t = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                uint c = i;
                for (int j = 0; j < 8; j++)
                {
                    c = ((c & 1) != 0) ? (poly ^ (c >> 1)) : (c >> 1);
                }
                t[i] = c;
            }
            return t;
        }

        public sealed class HashAccumulator
        {
            public uint Crc32 = 0xFFFFFFFFu;
            public uint Crc32C = 0xFFFFFFFFu;
            public uint AdlerA = 1;
            public uint AdlerB = 0;
            public uint Fnv1a = 0x811C9DC5u;
            public ulong Sum32 = 0;
            public uint Xor32 = 0;
            public long ByteCount = 0;
            // For xxHash32 we accumulate via a small streaming impl below.
            public XxHash32State Xx = new XxHash32State();

            public void Update(byte[] buf, int offset, int count)
            {
                if (_crc32IeeeTable == null) _crc32IeeeTable = BuildCrcTable(0xEDB88320u);
                if (_crc32CTable == null) _crc32CTable = BuildCrcTable(0x82F63B78u);
                var crcT = _crc32IeeeTable;
                var crcCT = _crc32CTable;
                uint c = Crc32;
                uint cc = Crc32C;
                uint a = AdlerA;
                uint b = AdlerB;
                uint f = Fnv1a;
                ulong s = Sum32;
                uint x = Xor32;
                for (int i = 0; i < count; i++)
                {
                    byte v = buf[offset + i];
                    c = crcT[(c ^ v) & 0xFFu] ^ (c >> 8);
                    cc = crcCT[(cc ^ v) & 0xFFu] ^ (cc >> 8);
                    a = (a + v) % 65521u;
                    b = (b + a) % 65521u;
                    f = (f ^ v) * 16777619u;
                    s += v;
                    if (((ByteCount + i) & 3) == 0) x ^= (uint)(v << 0);
                    else if (((ByteCount + i) & 3) == 1) x ^= (uint)(v << 8);
                    else if (((ByteCount + i) & 3) == 2) x ^= (uint)(v << 16);
                    else x ^= (uint)(v << 24);
                }
                Crc32 = c;
                Crc32C = cc;
                AdlerA = a;
                AdlerB = b;
                Fnv1a = f;
                Sum32 = s;
                Xor32 = x;
                ByteCount += count;
                Xx.Update(buf, offset, count);
            }

            public uint FinalCrc32 { get { return Crc32 ^ 0xFFFFFFFFu; } }
            public uint FinalCrc32C { get { return Crc32C ^ 0xFFFFFFFFu; } }
            public uint FinalAdler32 { get { return (AdlerB << 16) | AdlerA; } }
            public uint FinalSum32 { get { return (uint)(Sum32 & 0xFFFFFFFFu); } }
            public uint FinalXxHash32 { get { return Xx.Finalize(); } }
        }

        public sealed class XxHash32State
        {
            const uint P1 = 2654435761u;
            const uint P2 = 2246822519u;
            const uint P3 = 3266489917u;
            const uint P4 = 668265263u;
            const uint P5 = 374761393u;
            uint v1, v2, v3, v4;
            byte[] buf = new byte[16];
            int bufLen = 0;
            ulong total = 0;
            uint seed = 0;
            bool large = false;
            public XxHash32State() { Reset(0); }
            public void Reset(uint s)
            {
                seed = s;
                v1 = s + P1 + P2;
                v2 = s + P2;
                v3 = s + 0;
                v4 = s - P1;
                bufLen = 0;
                total = 0;
                large = false;
            }
            static uint Rotl(uint x, int r) { return (x << r) | (x >> (32 - r)); }
            static uint Round(uint acc, uint input) { acc += input * P2; acc = Rotl(acc, 13); acc *= P1; return acc; }
            public void Update(byte[] data, int offset, int count)
            {
                total += (ulong)count;
                if (bufLen != 0)
                {
                    int need = 16 - bufLen;
                    if (count < need)
                    {
                        Buffer.BlockCopy(data, offset, buf, bufLen, count);
                        bufLen += count;
                        return;
                    }
                    Buffer.BlockCopy(data, offset, buf, bufLen, need);
                    int p = 0;
                    v1 = Round(v1, BitConverter.ToUInt32(buf, p)); p += 4;
                    v2 = Round(v2, BitConverter.ToUInt32(buf, p)); p += 4;
                    v3 = Round(v3, BitConverter.ToUInt32(buf, p)); p += 4;
                    v4 = Round(v4, BitConverter.ToUInt32(buf, p));
                    offset += need; count -= need; bufLen = 0; large = true;
                }
                while (count >= 16)
                {
                    v1 = Round(v1, BitConverter.ToUInt32(data, offset)); offset += 4;
                    v2 = Round(v2, BitConverter.ToUInt32(data, offset)); offset += 4;
                    v3 = Round(v3, BitConverter.ToUInt32(data, offset)); offset += 4;
                    v4 = Round(v4, BitConverter.ToUInt32(data, offset)); offset += 4;
                    count -= 16; large = true;
                }
                if (count > 0)
                {
                    Buffer.BlockCopy(data, offset, buf, 0, count);
                    bufLen = count;
                }
            }
            public uint Finalize()
            {
                uint h;
                if (large) h = Rotl(v1, 1) + Rotl(v2, 7) + Rotl(v3, 12) + Rotl(v4, 18);
                else h = seed + P5;
                h += (uint)total;
                int p = 0;
                while (bufLen - p >= 4)
                {
                    h += BitConverter.ToUInt32(buf, p) * P3;
                    h = Rotl(h, 17) * P4;
                    p += 4;
                }
                while (p < bufLen)
                {
                    h += (uint)buf[p] * P5;
                    h = Rotl(h, 11) * P1;
                    p++;
                }
                h ^= h >> 15; h *= P2;
                h ^= h >> 13; h *= P3;
                h ^= h >> 16;
                return h;
            }
        }

        public static HashAccumulator HashFile(string path, long maxBytes, Action<long, long> progress)
        {
            var acc = new HashAccumulator();
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, FileOptions.SequentialScan))
            {
                long total = fs.Length;
                long limit = (maxBytes > 0 && maxBytes < total) ? maxBytes : total;
                var buf = new byte[1 << 20]; // 1 MB chunks
                long done = 0;
                while (done < limit)
                {
                    int want = (int)Math.Min(buf.Length, limit - done);
                    int got = fs.Read(buf, 0, want);
                    if (got <= 0) break;
                    acc.Update(buf, 0, got);
                    done += got;
                    if (progress != null) progress(done, limit);
                }
            }
            return acc;
        }

        public static byte[] ReadFirstBytes(string path, int n)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                long len = fs.Length;
                int take = (int)Math.Min((long)n, len);
                var buf = new byte[take];
                int got = 0;
                while (got < take)
                {
                    int r = fs.Read(buf, got, take - got);
                    if (r <= 0) break;
                    got += r;
                }
                if (got < take) Array.Resize(ref buf, got);
                return buf;
            }
        }

        public static byte[] ReadLastBytes(string path, int n)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                long len = fs.Length;
                int take = (int)Math.Min((long)n, len);
                fs.Seek(len - take, SeekOrigin.Begin);
                var buf = new byte[take];
                int got = 0;
                while (got < take)
                {
                    int r = fs.Read(buf, got, take - got);
                    if (r <= 0) break;
                    got += r;
                }
                if (got < take) Array.Resize(ref buf, got);
                return buf;
            }
        }

        public static HashAccumulator HashBytes(byte[] data)
        {
            var acc = new HashAccumulator();
            acc.Update(data, 0, data.Length);
            return acc;
        }

        public static string Extract(string gamePath, string gameFile, string cacheDir, Action<string> log)
        {
            Directory.CreateDirectory(cacheDir);
            var existing = Locate(cacheDir, gameFile);
            if (existing != null) return existing;

            var pamt = Path.Combine(gamePath, "0008", "0.pamt");
            var pazDir = Path.Combine(gamePath, "0008");
            var basename = Path.GetFileName(gameFile).ToLowerInvariant();
            List<PazEntry> entries;
            try
            {
                entries = ParsePamt(pamt, pazDir);
            }
            catch (Exception ex)
            {
                log("Could not parse archive index: " + ex.Message);
                return null;
            }

            var matches = entries.Where(entry => entry.Path.ToLowerInvariant().Contains(basename)).ToList();
            if (matches.Count == 0)
            {
                log("No archive entry matched " + gameFile + ".");
                return null;
            }

            foreach (var entry in matches)
            {
                try
                {
                    var readSize = entry.Compressed ? entry.CompSize : entry.OrigSize;
                    byte[] blob;
                    using (var stream = File.OpenRead(entry.PazFile))
                    {
                        stream.Seek(entry.Offset, SeekOrigin.Begin);
                        blob = new byte[readSize];
                        stream.Read(blob, 0, blob.Length);
                    }

                    if (entry.Path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                    {
                        log("Skipping encrypted XML extraction for " + entry.Path + ".");
                        continue;
                    }

                    if (entry.Compressed)
                    {
                        if (entry.CompressionType != 2)
                        {
                            log("Skipping unsupported compression type for " + entry.Path + ": " + entry.CompressionType);
                            continue;
                        }
                        blob = Lz4BlockDecompress(blob, entry.OrigSize);
                    }

                    var target = Path.Combine(cacheDir, entry.Path.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    File.WriteAllBytes(target, blob);
                    log("Extracted: " + target);
                }
                catch (Exception ex)
                {
                    log("Could not extract " + entry.Path + ": " + ex.Message);
                }
            }

            return Locate(cacheDir, gameFile);
        }

        private static string Locate(string cacheDir, string gameFile)
        {
            var basename = Path.GetFileName(gameFile);
            return Directory.Exists(cacheDir)
                ? Directory.GetFiles(cacheDir, basename, SearchOption.AllDirectories).OrderBy(path => path.Length).FirstOrDefault()
                : null;
        }

        private static List<PazEntry> ParsePamt(string pamtPath, string pazDir)
        {
            var data = File.ReadAllBytes(pamtPath);
            var pamtStem = Path.GetFileNameWithoutExtension(pamtPath);
            var off = 4;
            var pazCount = ReadU32(data, ref off);
            off += 8;
            for (var i = 0; i < pazCount; i++)
            {
                off += 8;
                if (i < pazCount - 1) off += 4;
            }

            var folderSize = ReadU32(data, ref off);
            var folderEnd = off + (int)folderSize;
            var folderPrefix = "";
            while (off < folderEnd)
            {
                var parent = ReadU32(data, ref off);
                var slen = data[off++];
                var name = Encoding.UTF8.GetString(data, off, slen);
                off += slen;
                if (parent == 0xFFFFFFFF) folderPrefix = name;
            }

            var nodeSize = ReadU32(data, ref off);
            var nodeStart = off;
            var nodes = new Dictionary<uint, Tuple<uint, string>>();
            while (off < nodeStart + nodeSize)
            {
                var rel = (uint)(off - nodeStart);
                var parent = ReadU32(data, ref off);
                var slen = data[off++];
                var name = Encoding.UTF8.GetString(data, off, slen);
                off += slen;
                nodes[rel] = Tuple.Create(parent, name);
            }

            Func<uint, string> buildPath = nodeRef =>
            {
                var parts = new List<string>();
                var cur = nodeRef;
                var guard = 0;
                while (cur != 0xFFFFFFFF && guard++ < 64 && nodes.ContainsKey(cur))
                {
                    var node = nodes[cur];
                    parts.Add(node.Item2);
                    cur = node.Item1;
                }
                parts.Reverse();
                return string.Concat(parts);
            };

            var folderCount = ReadU32(data, ref off);
            off += 4 + (int)folderCount * 16;
            var entries = new List<PazEntry>();
            while (off + 20 <= data.Length)
            {
                var nodeRef = ReadU32(data, ref off);
                var pazOffset = ReadU32(data, ref off);
                var compSize = ReadU32(data, ref off);
                var origSize = ReadU32(data, ref off);
                var flags = ReadU32(data, ref off);
                var pazIndex = (int)(flags & 0xFF);
                var nodePath = buildPath(nodeRef);
                var fullPath = string.IsNullOrEmpty(folderPrefix) ? nodePath : folderPrefix + "/" + nodePath;
                var pazNum = int.Parse(pamtStem) + pazIndex;
                entries.Add(new PazEntry
                {
                    Path = fullPath,
                    PazFile = Path.Combine(pazDir, pazNum + ".paz"),
                    Offset = pazOffset,
                    CompSize = compSize,
                    OrigSize = origSize,
                    Flags = flags
                });
            }
            return entries;
        }

        private static uint ReadU32(byte[] data, ref int off)
        {
            var value = BitConverter.ToUInt32(data, off);
            off += 4;
            return value;
        }

        private static byte[] Lz4BlockDecompress(byte[] data, uint originalSize)
        {
            var output = new List<byte>((int)originalSize);
            var i = 0;
            while (i < data.Length)
            {
                var token = data[i++];
                var literalLen = token >> 4;
                if (literalLen == 15)
                {
                    byte extra;
                    do
                    {
                        extra = data[i++];
                        literalLen += extra;
                    } while (extra == 255);
                }

                for (var j = 0; j < literalLen; j++) output.Add(data[i++]);
                if (i >= data.Length) break;

                var offset = data[i] | (data[i + 1] << 8);
                i += 2;
                if (offset <= 0 || offset > output.Count) throw new InvalidDataException("Invalid LZ4 match offset.");

                var matchLen = token & 0x0F;
                if (matchLen == 15)
                {
                    byte extra;
                    do
                    {
                        extra = data[i++];
                        matchLen += extra;
                    } while (extra == 255);
                }
                matchLen += 4;

                var start = output.Count - offset;
                for (var j = 0; j < matchLen; j++)
                {
                    output.Add(output[start++]);
                }
            }

            if (output.Count != originalSize)
            {
                throw new InvalidDataException("Decompressed size mismatch: got " + output.Count + ", expected " + originalSize + ".");
            }
            return output.ToArray();
        }
    }
}
