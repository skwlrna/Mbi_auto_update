using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using DungeonVisionBot;

static byte[] Packet(uint type, byte[] payload, byte encoding = 0)
{
    byte[] frame = new byte[9 + payload.Length];
    BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(0, 4), type);
    BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(4, 4), (uint)payload.Length);
    frame[8] = encoding;
    payload.CopyTo(frame, 9);
    return frame;
}

static byte[] SkillPayload()
{
    byte[] name = Encoding.Unicode.GetBytes("WaterWave");
    byte[] payload = new byte[8 + 4 + name.Length + 4];
    for (int i = 0; i < 8; i++) payload[i] = (byte)(i + 1);
    BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(8, 4), name.Length);
    name.CopyTo(payload, 12);
    BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(payload.Length - 4), 99);
    return payload;
}

static byte[] DamagePayload()
{
    byte[] name = Encoding.Unicode.GetBytes("Wave");
    byte[] payload = new byte[8 + 8 + 4 + name.Length + 4 + 12 + 20 + 4];
    for (int i = 0; i < 8; i++)
    {
        payload[i] = (byte)(i + 1);
        payload[i + 8] = (byte)(i + 9);
    }
    BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(16, 4), name.Length);
    name.CopyTo(payload, 20);
    int offset = 20 + name.Length;
    BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(offset, 4), 12345);
    BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(offset + 36, 4), 88);
    return payload;
}

static void Assert(bool ok, string why)
{
    if (!ok) throw new Exception(why);
}

int passed = 0;
var monster = Packet(WaterwayPacketTraceDecoder.MonsterType,
    new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
var skill = Packet(WaterwayPacketTraceDecoder.SkillType, SkillPayload());
var damage = Packet(WaterwayPacketTraceDecoder.DamageType, DamagePayload());

{
    // Framing may arrive split at any byte; never read incomplete data.
    var decoder = new WaterwayPacketTraceDecoder();
    foreach (byte b in monster.AsSpan(0, 8).ToArray())
        Assert(decoder.Feed(new byte[] { b }).Count == 0, "premature decode");
    var results = decoder.Feed(monster.AsSpan(8));
    Assert(results.Count == 1 &&
           results[0].Kind == "monster-id" &&
           results[0].ActorId == "0102030405060708", "monster ID");
    passed++;
}
{
    var decoder = new WaterwayPacketTraceDecoder();
    var pair = monster.Concat(skill).Concat(damage).ToArray();
    var result = decoder.Feed(pair);
    Assert(result.Count == 3, "multi-frame");
    Assert(result[1].Kind == "skill" && result[1].Skill == "WaterWave" &&
           result[1].SkillId == 99, "skill fields");
    Assert(result[2].Kind == "damage" && result[2].Skill == "Wave" &&
           result[2].TargetId == "090A0B0C0D0E0F10" &&
           result[2].Damage == 12345 && result[2].SkillId == 88, "damage fields");
    passed++;
}
{
    byte[] compressed;
    using (var output = new MemoryStream())
    {
        using (var encoder = new BrotliStream(output, CompressionLevel.Fastest, leaveOpen: true))
            encoder.Write(SkillPayload());
        compressed = output.ToArray();
    }
    var decoded = new WaterwayPacketTraceDecoder()
        .Feed(Packet(WaterwayPacketTraceDecoder.SkillType, compressed, encoding: 1));
    Assert(decoded.Count == 1 && decoded[0].SkillId == 99, "Brotli");
    passed++;
}
{
    var decoder = new WaterwayPacketTraceDecoder();
    Assert(decoder.Feed(Packet(999999, new byte[] { 1 })).Count == 0, "unknown type");
    Assert(decoder.Feed(Packet(WaterwayPacketTraceDecoder.MonsterType,
        new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }, encoding: 2)).Count == 0, "unknown encoding");
    passed++;
}
{
    // Malformed packet payload cannot become a game event.
    var payload = SkillPayload();
    BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(8, 4), 999999);
    Assert(new WaterwayPacketTraceDecoder()
        .Feed(Packet(WaterwayPacketTraceDecoder.SkillType, payload)).Count == 0, "name bounds");
    passed++;
}
{
    var decoder = new WaterwayPacketTraceDecoder();
    byte[] oversized = new byte[9];
    BinaryPrimitives.WriteUInt32LittleEndian(oversized.AsSpan(4, 4), 0xFFFFFFFF);
    bool threw = false;
    try { decoder.Feed(oversized); }
    catch (InvalidDataException) { threw = true; }
    Assert(threw, "reject oversized frame");
    Assert(decoder.Feed(monster).Count == 1, "decoder reset");
    passed++;
}
{
    var decoder = new WaterwayPacketTraceDecoder();
    var badBrotli = Packet(WaterwayPacketTraceDecoder.SkillType,
        new byte[] { 0xFF, 0xFE, 0xFD, 0xFC }, encoding: 1);
    Assert(decoder.Feed(badBrotli).Count == 0, "invalid Brotli fail closed");
    Assert(decoder.Feed(monster).Count == 1, "recover after malformed payload");
    passed++;
}
Console.WriteLine($"Waterway packet trace regression: {passed} PASS");

// Optional OFFLINE replay: custom concatenated [type, size, encoding, payload] trace.
// Not a PCAP parser, does not capture traffic or modify the game process.
if (args.Length == 1)
{
    var path = Path.GetFullPath(args[0]);
    var info = new FileInfo(path);
    if (!info.Exists || info.Length > 32 * 1024 * 1024)
        throw new InvalidDataException("Replay trace not found or exceeds 32 MiB.");
    var decoder = new WaterwayPacketTraceDecoder();
    using var file = File.OpenRead(path);
    byte[] buffer = new byte[8192];
    int printed = 0;
    while (true)
    {
        int n = file.Read(buffer, 0, buffer.Length);
        if (n == 0) break;
        foreach (var evt in decoder.Feed(buffer.AsSpan(0, n)))
        {
            if (printed++ >= 50) break;
            Console.WriteLine($"{evt.Kind} actor={evt.ActorId} target={evt.TargetId} skill={evt.Skill} skillId={evt.SkillId} damage={evt.Damage}");
        }
    }
    Console.WriteLine("Replay finished (max 50 observations displayed).");
}
