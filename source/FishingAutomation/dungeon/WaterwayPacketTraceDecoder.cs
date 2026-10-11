using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace DungeonVisionBot;

/// <summary>
/// OFFLINE, READ-ONLY research decoder for a legacy community-observed
/// framed stream. Does not capture network traffic, attach to a process,
/// read client memory, or send input. IDs/formats have NOT been validated
/// against the current game version.
/// 
/// Framing observed by artist012/mmbl (2025): uint32 packet type,
/// uint32 payload length, byte encoding (0=raw, 1=Brotli), then payload.
/// Parser is an independent implementation, not copied source code.
/// </summary>
internal sealed class WaterwayPacketTraceDecoder
{
    internal sealed record Observation(
        string Kind,
        string ActorId,
        string? TargetId = null,
        string? Skill = null,
        uint? SkillId = null,
        uint? Damage = null,
        int PacketType = 0);

    internal const uint MonsterType = 1033;
    internal const uint SkillType = 10041;
    internal const uint DamageType = 1283;
    private const int MaxPayload = 1024 * 1024;
    private const int MaxSkillNameBytes = 512;
    private readonly List<byte> _pending = new();

    /// <summary>
    /// Accepts arbitrary TCP-like chunks and emits only complete, validated
    /// observations. Call with the next chunk in exact captured stream order.
    /// Framing corruption is fatal: do not guess packet boundaries.
    /// </summary>
    internal IReadOnlyList<Observation> Feed(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length > MaxPayload + 9 || _pending.Count + bytes.Length > MaxPayload * 2 + 18)
            throw new InvalidDataException("Trace chunk exceeds safety limit.");

        for (int i = 0; i < bytes.Length; i++)
            _pending.Add(bytes[i]);
        var result = new List<Observation>();

        while (_pending.Count >= 9)
        {
            // Explicit unsigned little-endian reads; no native code required.
            uint type = BinaryPrimitives.ReadUInt32LittleEndian(_pending.GetRange(0, 4).ToArray());
            uint length = BinaryPrimitives.ReadUInt32LittleEndian(_pending.GetRange(4, 4).ToArray());
            byte encoding = _pending[8];
            if (length > MaxPayload)
            {
                _pending.Clear();
                throw new InvalidDataException("Untrusted payload length exceeds safety limit.");
            }
            int fullLength = 9 + (int)length;
            if (_pending.Count < fullLength) break;

            byte[] payload = _pending.GetRange(9, (int)length).ToArray();
            _pending.RemoveRange(0, fullLength);

            // Unknown compression encoding: discard rather than guess.
            if (encoding == 1)
            {
                try { payload = BoundedDecompress(payload); }
                catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException) { continue; }
            }
            else if (encoding != 0) continue;

            Observation? observation = TryParse(type, payload);
            if (observation is not null)
                result.Add(observation);
        }
        return result;
    }

    private static byte[] BoundedDecompress(byte[] compressed)
    {
        using var input = new MemoryStream(compressed, writable: false);
        using var brotli = new BrotliStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        byte[] buffer = new byte[8192];
        while (true)
        {
            int read = brotli.Read(buffer, 0, buffer.Length);
            if (read == 0) return output.ToArray();
            if (output.Length + read > MaxPayload)
                throw new InvalidDataException("Uncompressed payload exceeds safety limit.");
            output.Write(buffer, 0, read);
        }
    }

    private static Observation? TryParse(uint type, byte[] data)
    {
        // Fields are taken only from publicly documented community samples.
        // Never infer location, HP, puzzle marks, or portal state from these.
        if (type == MonsterType)
        {
            if (data.Length < 8) return null;
            return new Observation("monster-id", Id(data.AsSpan(0, 8)),
                PacketType: (int)type);
        }

        if (type == SkillType)
        {
            // actor(8), UTF16LE name byte count(4), name(n), skill id(4)
            if (data.Length < 16) return null;
            int nameSize = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(8, 4));
            if (!NameFits(nameSize, data.Length - 16)) return null;
            string name = CleanSkillName(data.AsSpan(12, nameSize));
            uint skillId = BinaryPrimitives.ReadUInt32LittleEndian(
                data.AsSpan(data.Length - 4, 4));
            return new Observation("skill", Id(data.AsSpan(0, 8)),
                Skill: name, SkillId: skillId, PacketType: (int)type);
        }

        if (type == DamageType)
        {
            // actor(8), target(8), name byte count(4), name(n),
            // damage(4), unknown(12), flags(20), skill id(4).
            const int FixedSize = 8 + 8 + 4 + 4 + 12 + 20 + 4;
            if (data.Length < FixedSize) return null;
            int nameSize = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(16, 4));
            if (!NameFits(nameSize, data.Length - FixedSize)) return null;
            int offset = 20 + nameSize;
            if (offset + 40 > data.Length) return null;
            string name = CleanSkillName(data.AsSpan(20, nameSize));
            uint damage = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset, 4));
            uint skillId = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset + 36, 4));
            return new Observation("damage", Id(data.AsSpan(0, 8)),
                Id(data.AsSpan(8, 8)), name, skillId, damage, (int)type);
        }

        return null;
    }

    private static bool NameFits(int length, int maxSpace) =>
        length >= 0 && length <= MaxSkillNameBytes &&
        (length & 1) == 0 && length <= maxSpace;

    private static string CleanSkillName(ReadOnlySpan<byte> bytes)
    {
        string decoded = Encoding.Unicode.GetString(bytes);
        return new string(decoded.Where(c => !char.IsControl(c))
            .Take(96).ToArray());
    }

    private static string Id(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(bytes);
}
