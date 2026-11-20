using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using StarComms.Core;
using StarComms.Engine;
using System.Threading.Channels;

namespace StarComms.App;

public sealed record AudioDevice(string Id, string Name);
public sealed class AudioService : IAsyncDisposable
{
    readonly object sync = new();
    readonly MMDeviceEnumerator enumerator = new();
    readonly SemaphoreSlim configuration = new(1);
    readonly MMDeviceNotificationClient? notifications;
    public event Action<DataFlow>? DefaultChanged;
    WasapiRecorder? recorder; WasapiPlayer? player;
    readonly VoiceMixer mixer = new();
    readonly Channel<(short[] Pcm, ulong Burst, VoiceChannel Channel)> capture = Channel.CreateBounded<(short[], ulong, VoiceChannel)>(new BoundedChannelOptions(3) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
    readonly CancellationTokenSource stop = new(); readonly Task encoderTask;
    public volatile bool Paused;
    public volatile bool SessionLocked;
    public VoiceChannel Preferred { get { lock (sync) return ptt.Preferred; } }
    readonly VoiceActivation ptt = new();
    Client? client; short[] partial = new short[960]; int position; uint index; ulong encodedBurst; VoiceEncoder? encoder;
    VoiceChannel? lastChannel; ulong lastBurst; uint? lastIndex;
    public bool Muted { get; private set; }
    public bool InputAvailable { get; private set; }
    public bool OutputAvailable { get; private set; }
    public bool Editing { get; set; }
    readonly VoiceActivation preview = new();
    public double LevelDb { get; private set; } = -65;
    public bool PreviewSpeech { get { lock (sync) return preview.Channel.HasValue; } }
    public void SetThreshold(int db)
    {
        lock (sync) { ptt.SetThreshold(db); preview.SetThreshold(db); }
    }
    public VoiceChannel? Transmitting { get { lock (sync) return ptt.Channel; } }
    public event Action<string>? Error;
    public VoiceMixer Mixer => mixer;
    public AudioService()
    {
        encoderTask = Task.Run(EncodeLoop);
        try {
            notifications = enumerator.CreateNotificationClient(false);
            notifications.DefaultDeviceChanged += (_, e) => {
                if (e.Role is Role.Communications or Role.Multimedia) DefaultChanged?.Invoke(e.Flow);
            };
        } catch (System.Runtime.InteropServices.COMException) { }
    }
    public AudioDevice[] Devices(DataFlow flow)
    {
        try { return new[] { new AudioDevice("", "Systémové zařízení") }.Concat(enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active).Select(d => new AudioDevice(d.ID, d.FriendlyName))).ToArray(); }
        catch { return [new("", "Systémové zařízení")]; }
    }
    MMDevice Device(DataFlow flow, string id)
    {
        if (id.Length > 0) return enumerator.GetDevice(id);
        try { return enumerator.GetDefaultAudioEndpoint(flow, Role.Communications); } catch { return enumerator.GetDefaultAudioEndpoint(flow, Role.Multimedia); }
    }
    public async Task Configure(string input, string output)
    {
        await configuration.WaitAsync();
        try {
            if (stop.IsCancellationRequested) return;
            await ConfigureDevices(input, output);
        } finally { configuration.Release(); }
    }
    public async Task StopDevices()
    {
        await configuration.WaitAsync();
        try { await CloseDevices(); } finally { configuration.Release(); }
    }
    async Task CloseDevices()
    {
        Disarm(); InputAvailable = OutputAvailable = false;
        if (recorder != null) { await recorder.DisposeAsync(); recorder = null; }
        if (player != null) { await player.DisposeAsync(); player = null; }
    }
    async Task ConfigureDevices(string input, string output)
    {
        await CloseDevices();
        await Task.Run(() => {
            try {
                player = new WasapiPlayerBuilder().WithDevice(Device(DataFlow.Render, output)).WithLatency(40).Build();
                player.Init(new MixerProvider(mixer).ToWaveProvider()); player.PlaybackStopped += (_, e) => { if (e.Exception != null) { OutputAvailable = false; Disarm(); Error?.Invoke("Sluchátka nejsou dostupná. Vyber jiný výstup."); } }; player.Play(); OutputAvailable = true;
            } catch { Error?.Invoke("Sluchátka nejsou dostupná. Vyber jiný výstup."); }
            try {
                recorder = new WasapiRecorderBuilder().WithDevice(Device(DataFlow.Capture, input)).WithFormat(new WaveFormat(48000, 16, 1)).WithBufferLength(40).Build();
                recorder.DataAvailable += (data, flags, devicePosition, qpcPosition) => Capture(data);
                recorder.RecordingStopped += (_, e) => { if (e.Exception != null) { InputAvailable = false; Disarm(); Error?.Invoke("Mikrofon není dostupný. Ostatní stále slyšíš. Vyber mikrofon."); } };
                recorder.StartRecording(); InputAvailable = true;
            } catch { Error?.Invoke("Mikrofon není dostupný. Ostatní stále slyšíš. Vyber mikrofon."); }
        });
    }
    public void Attach(Client? value)
    {
        Disarm(); mixer.ResetSession();
        if (client != null) { client.Voice -= mixer.Push; client.End -= mixer.End; client.ResetAudio -= Reset; }
        client = value;
        if (client != null) { client.Voice += mixer.Push; client.End += mixer.End; client.ResetAudio += Reset; }
    }
    void Reset() { Disarm(); mixer.Clear(); }
    public void Mute(bool muted) { Muted = muted; Disarm(); }
    public void Disarm() { lock (sync) { Finish(); ptt.Disarm(); preview.Disarm(); LevelDb = -65; position = 0; while (capture.Reader.TryRead(out _)) { } } }
    public void ConfigureModes(bool ship, bool command)
    {
        lock (sync) { Disarm(); ptt.Configure(ship, command); }
    }
    public void SelectAutomatic(VoiceChannel channel)
    {
        lock (sync) { Disarm(); ptt.Select(channel); }
    }
    bool Allowed(VoiceChannel channel)
    {
        var current = client;
        var self = current?.Self;
        return !Paused && !SessionLocked && !Muted && !Editing && InputAvailable && OutputAvailable &&
            current?.VoiceReady == true && self?.ShipId != null &&
            (channel == VoiceChannel.Ship || self.IsCaptain && current.Snapshot?.Members.Any(m =>
                m.IsCaptain && m.MemberId != self.MemberId && m.Status == MemberStatus.Online) == true);
    }
    public void Input(string source, VoiceChannel channel, bool down, bool enabled = true)
    {
        lock (sync) {
            var old = ptt.Channel;
            ptt.Input(source, channel, down, enabled && Allowed(channel));
            if (old != ptt.Channel) { Finish(); position = 0; }
        }
    }
    void Finish() { if (lastChannel is VoiceChannel c && lastIndex.HasValue) client?.EndVoice(c, lastBurst, lastIndex.Value); lastChannel = null; lastIndex = null; }
    void Capture(ReadOnlySpan<byte> data)
    {
        lock (sync) {
            for (int i = 0; i + 1 < data.Length; i += 2)
            {
                short sample = System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(data[i..]);
                partial[position++] = sample;
                if (position != partial.Length) continue;
                position = 0;
                var old = ptt.Channel;
                var rms = VoiceActivation.Rms(partial);
                if (Paused) {
                    LevelDb = Math.Max(-65, 20 * Math.Log10(rms));
                    preview.Frame(rms, true, false, false);
                }
                ptt.Frame(rms, Allowed(VoiceChannel.Ship), Allowed(VoiceChannel.Command), client?.Self?.IsCaptain == true);
                if (old != ptt.Channel) Finish();
                if (ptt.Channel is VoiceChannel channel)
                {
                    capture.Writer.TryWrite((partial, ptt.Burst, channel));
                    partial = new short[960];
                }
            }
        }
    }
    async Task EncodeLoop()
    {
        try { await foreach (var frame in capture.Reader.ReadAllAsync(stop.Token)) {
            try {
                lock (sync) if (ptt.Channel != frame.Channel || ptt.Burst != frame.Burst) continue;
                if (encodedBurst != frame.Burst) { encoder = new(); encodedBurst = frame.Burst; index = 0; }
                var opus = encoder!.Encode(frame.Pcm);
                lock (sync) {
                    if (ptt.Channel != frame.Channel || ptt.Burst != frame.Burst) continue;
                    client?.SendVoice(frame.Channel, frame.Burst, index, opus);
                    lastIndex = index++; lastChannel = frame.Channel; lastBurst = frame.Burst;
                }
            } catch {
                Disarm();
                Error?.Invoke("Zvuk se nepodařilo zpracovat. Zkus znovu vybrat mikrofon.");
            }
        } } catch (OperationCanceledException) { }
    }
    public async Task Tone(string output)
    {
        using var tone = new WasapiPlayerBuilder().WithDevice(Device(DataFlow.Render, output)).WithLatency(40).Build();
        tone.Init(new SignalGenerator(48000, 1) { Gain = .12, Frequency = 660, Type = SignalGeneratorType.Sin }.Take(TimeSpan.FromMilliseconds(350)).ToWaveProvider()); tone.Play(); await Task.Delay(450);
    }
    public async ValueTask DisposeAsync()
    {
        Attach(null); stop.Cancel(); notifications?.Dispose();
        await configuration.WaitAsync();
        try {
            if (recorder != null) await recorder.DisposeAsync();
            if (player != null) await player.DisposeAsync();
            await encoderTask;
            enumerator.Dispose();
        } finally { configuration.Release(); }
    }
    sealed class MixerProvider(VoiceMixer mixer) : ISampleProvider
    {
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(48000, 1);
        float[] frame = []; int offset;
        public int Read(Span<float> buffer) { for (int i = 0; i < buffer.Length; i++) { if (offset >= frame.Length) { frame = mixer.ReadFrame(); offset = 0; } buffer[i] = frame[offset++]; } return buffer.Length; }
    }
}
