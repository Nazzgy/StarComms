using Concentus.Enums;
using Concentus;
using StarComms.Core;

namespace StarComms.Engine;

static class ManagedOpus
{
    static ManagedOpus() => OpusCodecFactory.AttemptToUseNativeLibrary = false;
    public static IOpusEncoder Encoder() => OpusCodecFactory.CreateEncoder(48000, 1, OpusApplication.OPUS_APPLICATION_VOIP);
    public static IOpusDecoder Decoder() => OpusCodecFactory.CreateDecoder(48000, 1);
}

public sealed class VoiceEncoder
{
    readonly IOpusEncoder encoder = ManagedOpus.Encoder();
    public VoiceEncoder() { encoder.Bitrate = 32000; encoder.Complexity = 5; encoder.UseDTX = false; encoder.UseInbandFEC = true; encoder.PacketLossPercent = 5; }
    public byte[] Encode(short[] pcm)
    {
        if (pcm.Length != 960) throw new ArgumentException("Expected 960 samples");
        byte[] result = new byte[1000]; int length = encoder.Encode(pcm, 960, result, result.Length); if (length is < 1 or > 1000) throw new InvalidDataException(); return result[..length];
    }
}
public sealed class VoiceDecoder
{
    readonly IOpusDecoder decoder = ManagedOpus.Decoder();
    public short[] Decode(byte[]? opus, bool fec = false)
    {
        short[] pcm = new short[960]; int n = decoder.Decode(opus.AsSpan(), pcm, 960, fec); if (n != 960) throw new InvalidDataException("OPUS_FRAME_SIZE"); return pcm;
    }
}
public sealed class VoiceMixer(TimeProvider? clock = null)
{
    sealed class Speaker(VoiceFrame first, long now)
    {
        public VoiceFrame Frame = first;
        public VoiceDecoder Decoder = new();
        public SortedDictionary<uint, byte[]> Queue = [];
        public uint Next = first.FrameIndex;
        public long Start = now;
        public long Last = now;
        public long? End;
        public uint? LastIndex;
        public int Missing;
        public bool Playing;
    }
    readonly object sync = new(); readonly TimeProvider time = clock ?? TimeProvider.System;
    readonly Dictionary<uint, Speaker> speakers = [];
    readonly Dictionary<uint, float> volumes = [];
    public float ShipVolume { get; set; } = 1;
    public float CommandVolume { get; set; } = 1;
    public bool Ducking { get; set; } = true;
    float duck = 1; long commandLast;
    public int MaxDepth { get; private set; }
    public void MemberVolume(uint id, float volume) { lock (sync) volumes[id] = Math.Clamp(volume, 0, 2); }
    public float MemberVolume(uint id) { lock (sync) return volumes.GetValueOrDefault(id, 1f); }
    public void ResetSession() { lock (sync) { speakers.Clear(); volumes.Clear(); } }
    public (uint Member, VoiceChannel Channel)[] ActiveSpeakers()
    {
        lock (sync) return speakers.Where(pair => pair.Value.Playing && time.GetElapsedTime(pair.Value.Last).TotalMilliseconds < 200)
            .Select(pair => (pair.Key, pair.Value.Frame.Channel)).ToArray();
    }
    public void Clear() { lock (sync) speakers.Clear(); }
    public void Push(VoiceFrame f)
    {
        lock (sync) {
            long now = time.GetTimestamp();
            if (!speakers.TryGetValue(f.SenderId, out var s) || s.Frame.SenderEpoch != f.SenderEpoch || s.Frame.TalkBurstId != f.TalkBurstId) speakers[f.SenderId] = s = new(f, now);
            if (f.FrameIndex < s.Next || s.LastIndex is uint end && f.FrameIndex > end || s.End is long stamp && time.GetElapsedTime(stamp, now).TotalMilliseconds > 200) return;
            if (time.GetElapsedTime(s.Last, now).TotalMilliseconds > 120 || s.Queue.Count >= 6) { s.Queue.Clear(); s.Next = f.FrameIndex; s.Start = now; s.Playing = false; s.Decoder = new(); }
            s.Queue.TryAdd(f.FrameIndex, f.Opus); s.Last = now; MaxDepth = Math.Max(MaxDepth, s.Queue.Count);
        }
    }
    public void End(VoiceEnd end)
    {
        lock (sync) {
            if (!speakers.TryGetValue(end.SenderId, out var s)) { s = new(new(end.Channel, end.SenderId, end.SenderEpoch, end.RecipientEpoch, end.ShipId, end.TalkBurstId, 0, []), time.GetTimestamp()); speakers[end.SenderId] = s; }
            if (s.Frame.SenderEpoch == end.SenderEpoch && s.Frame.TalkBurstId == end.TalkBurstId && s.End == null) { s.End = time.GetTimestamp(); s.LastIndex = end.LastFrameIndex; }
        }
    }
    public float[] ReadFrame()
    {
        lock (sync) {
            float[] mixed = new float[960]; long now = time.GetTimestamp();
            bool command = speakers.Values.Any(s => s.Frame.Channel == VoiceChannel.Command && time.GetElapsedTime(s.Last, now).TotalMilliseconds < 200);
            if (command) commandLast = now;
            float target = Ducking && (command || commandLast != 0 && time.GetElapsedTime(commandLast, now).TotalMilliseconds < 200) ? .4f : 1;
            foreach (var pair in speakers.ToArray()) {
                var s = pair.Value;
                if (s.End is long end && time.GetElapsedTime(end, now).TotalMilliseconds >= 200 || s.LastIndex is uint index && s.Next > index || time.GetElapsedTime(s.Last, now).TotalMilliseconds > 500) { speakers.Remove(pair.Key); continue; }
                if (!s.Playing && time.GetElapsedTime(s.Start, now).TotalMilliseconds < 60) continue; s.Playing = true;
                short[] pcm;
                try {
                    if (s.Queue.Remove(s.Next, out var opus)) { pcm = s.Decoder.Decode(opus); s.Missing = 0; }
                    else if (s.Queue.TryGetValue(s.Next + 1, out var next)) { pcm = s.Decoder.Decode(next, true); s.Missing++; }
                    else { s.Missing++; pcm = s.Missing <= 3 ? s.Decoder.Decode(null) : new short[960]; }
                } catch { pcm = new short[960]; s.Decoder = new(); }
                s.Next++; float volume = volumes.GetValueOrDefault(pair.Key, 1) * (s.Frame.Channel == VoiceChannel.Command ? CommandVolume : ShipVolume);
                for (int i = 0; i < mixed.Length; i++) { float d = s.Frame.Channel == VoiceChannel.Ship ? duck + (target - duck) * Math.Min(1, i / 1440f) : 1; mixed[i] += pcm[i] / 32768f * volume * d; }
            }
            duck += (target - duck) * (target < duck ? 2f / 3 : .1f);
            for (int i = 0; i < mixed.Length; i++) mixed[i] = Math.Clamp(mixed[i], -1, 1); return mixed;
        }
    }
}
