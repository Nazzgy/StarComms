using System.Buffers.Binary;
using System.Security.Cryptography;
using StarComms.Core;

namespace StarComms.Engine;

public sealed class DatagramCipher(byte[] key, byte[] prefix, ulong connectionId) : IDisposable
{
    readonly AesGcm aes = new(key, 16);
    readonly object sync = new();
    readonly HashSet<ulong> received = [];
    ulong next, highest;
    public byte[] Encrypt(ReadOnlySpan<byte> plain)
    {
        lock (sync) {
            if (plain.Length > 1160 || next == ulong.MaxValue) throw new InvalidDataException();
            byte[] p = new byte[plain.Length + 40]; "FCV1"u8.CopyTo(p); p[4] = 1;
            BinaryPrimitives.WriteUInt64BigEndian(p.AsSpan(8), connectionId); BinaryPrimitives.WriteUInt64BigEndian(p.AsSpan(16), next++);
            Span<byte> nonce = stackalloc byte[12]; prefix.CopyTo(nonce); p.AsSpan(16, 8).CopyTo(nonce[4..]);
            aes.Encrypt(nonce, plain, p.AsSpan(24, plain.Length), p.AsSpan(24 + plain.Length, 16), p.AsSpan(0, 24)); return p;
        }
    }
    public byte[]? Decrypt(ReadOnlySpan<byte> packet)
    {
        lock (sync) {
            if (packet.Length is < 41 or > 1200 || !packet[..4].SequenceEqual("FCV1"u8) || packet[4] != 1 || packet[5] != 0 || packet[6] != 0 || packet[7] != 0 || BinaryPrimitives.ReadUInt64BigEndian(packet[8..]) != connectionId) return null;
            ulong seq = BinaryPrimitives.ReadUInt64BigEndian(packet[16..]);
            if (received.Contains(seq) || (highest >= 256 && seq <= highest - 256)) return null;
            byte[] plain = new byte[packet.Length - 40]; Span<byte> nonce = stackalloc byte[12]; prefix.CopyTo(nonce); packet.Slice(16, 8).CopyTo(nonce[4..]);
            try { aes.Decrypt(nonce, packet.Slice(24, plain.Length), packet[^16..], plain, packet[..24]); } catch (CryptographicException) { return null; }
            highest = Math.Max(highest, seq); received.Add(seq); if (highest >= 256) received.RemoveWhere(s => s <= highest - 256); return plain;
        }
    }
    public void Dispose() { lock (sync) { aes.Dispose(); CryptographicOperations.ZeroMemory(key); } }
}
public sealed record VoiceFrame(VoiceChannel Channel, uint SenderId, uint SenderEpoch, uint RecipientEpoch, uint ShipId, ulong TalkBurstId, uint FrameIndex, byte[] Opus)
{
    public byte[] Encode()
    {
        byte[] p = new byte[34 + Opus.Length]; p[0] = 16; p[1] = (byte)Channel;
        BinaryPrimitives.WriteUInt32BigEndian(p.AsSpan(4), SenderId); BinaryPrimitives.WriteUInt32BigEndian(p.AsSpan(8), SenderEpoch); BinaryPrimitives.WriteUInt32BigEndian(p.AsSpan(12), RecipientEpoch); BinaryPrimitives.WriteUInt32BigEndian(p.AsSpan(16), ShipId); BinaryPrimitives.WriteUInt64BigEndian(p.AsSpan(20), TalkBurstId); BinaryPrimitives.WriteUInt32BigEndian(p.AsSpan(28), FrameIndex); BinaryPrimitives.WriteUInt16BigEndian(p.AsSpan(32), (ushort)Opus.Length); Opus.CopyTo(p, 34); return p;
    }
    public static VoiceFrame? Parse(byte[] p)
    {
        if (p.Length < 35 || p[0] != 16 || p[1] is not (1 or 2) || p[2] != 0 || p[3] != 0) return null;
        int n = BinaryPrimitives.ReadUInt16BigEndian(p.AsSpan(32)); if (n is < 1 or > 1000 || n + 34 != p.Length) return null;
        ulong burst = BinaryPrimitives.ReadUInt64BigEndian(p.AsSpan(20)); if (burst == 0) return null;
        return new((VoiceChannel)p[1], BinaryPrimitives.ReadUInt32BigEndian(p.AsSpan(4)), BinaryPrimitives.ReadUInt32BigEndian(p.AsSpan(8)), BinaryPrimitives.ReadUInt32BigEndian(p.AsSpan(12)), BinaryPrimitives.ReadUInt32BigEndian(p.AsSpan(16)), burst, BinaryPrimitives.ReadUInt32BigEndian(p.AsSpan(28)), p[34..]);
    }
}
public sealed class BurstGuard(TimeProvider? clock = null)
{
    readonly TimeProvider time = clock ?? TimeProvider.System;
    uint epoch; ulong burst; VoiceChannel channel; uint ship; uint? last; long? end;
    public bool Accept(uint generation, ulong id, VoiceChannel target, uint shipId, uint index, bool ending = false)
    {
        if (generation != epoch) { epoch = generation; burst = 0; end = null; last = null; }
        if (id == 0 || id < burst) return false;
        if (id > burst) { burst = id; channel = target; ship = shipId; last = null; end = null; }
        if (channel != target || ship != shipId) return false;
        if (end.HasValue && (time.GetElapsedTime(end.Value).TotalMilliseconds > 200 || index > last)) return false;
        if (ending && !end.HasValue) { last = index; end = time.GetTimestamp(); } return true;
    }
}
