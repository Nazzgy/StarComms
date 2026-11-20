using System.Collections.Concurrent;
using System.Net;
using StarComms.Core;
using StarComms.Engine;
using Xunit;

namespace StarComms.Tests;

public class FlightVoiceTests
{
    [Fact]
    public void QuietSpeechCanBeEnabledByLoweringTheThreshold()
    {
        var voice = new VoiceActivation();
        var pcm = Enumerable.Repeat((short)200, 960).ToArray();
        voice.Frame(pcm, true, false, false);
        Assert.Null(voice.Channel);
        voice.SetThreshold(-50);
        voice.Frame(pcm, true, false, false);
        Assert.Equal(VoiceChannel.Ship, voice.Channel);
    }

    [Fact]
    public async Task TwoShipsKeepThePrivatePhraseOutOfCrewAudio()
    {
        using var certificate = Certificates.Create();
        await using var host = new Host("Let", certificate);
        host.Start();
        await using var pilot = await host.ConnectLocal("Kapitán A");
        host.Changed += () => { foreach (var pending in host.Pending) host.Decide(pending.Id, true); };
        var address = new JoinAddress(IPAddress.Loopback, host.Port, host.Fingerprint);
        await using var crew = new Client();
        await using var captain = new Client();
        await crew.Connect(address, "Posádka A");
        await captain.Connect(address, "Kapitán B");
        host.Mutate(f => {
            var a = f.CreateShip("Polaris").ShipId;
            var b = f.CreateShip("Gladius").ShipId;
            f.Move(pilot.MemberId, a); f.Move(crew.MemberId, a); f.Move(captain.MemberId, b);
            f.Captain(a, pilot.MemberId); f.Captain(b, captain.MemberId);
        });
        var version = host.Fleet.Snapshot.StateVersion;
        async Task Until(Func<bool> predicate)
        {
            using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (!predicate()) await Task.Delay(10, limit.Token);
        }
        await Until(() => new[] { pilot, crew, captain }.All(c => c.Snapshot?.StateVersion == version && c.VoiceReady));
        var crewFrames = new ConcurrentQueue<VoiceFrame>();
        var commandFrames = new ConcurrentQueue<VoiceFrame>();
        crew.Voice += crewFrames.Enqueue;
        captain.Voice += commandFrames.Enqueue;
        var gate = new VoiceActivation();
        var speech = Enumerable.Range(0, 960).Select(i => (short)(6000 * Math.Sin(i * .12))).ToArray();
        var silence = new short[960];
        VoiceEncoder encoder = new();
        ulong burst = 0;
        uint index = 0;
        async Task Frame(short[] samples)
        {
            gate.Frame(samples, true, true, true);
            if (gate.Channel is VoiceChannel channel) {
                if (gate.Burst != burst) { burst = gate.Burst; index = 0; encoder = new(); }
                pilot.SendVoice(channel, burst, index++, encoder.Encode(samples));
            }
            await Task.Delay(20);
        }
        await Frame(speech);
        await Until(() => !crewFrames.IsEmpty);
        gate.Input("PTT", VoiceChannel.Command, true, true);
        for (int i = 0; i < 4; i++) await Frame(speech);
        await Until(() => commandFrames.Count >= 4);
        gate.Input("PTT", VoiceChannel.Command, false, true);
        for (int i = 0; i < 3; i++) { await Frame(speech); Assert.Null(gate.Channel); }
        for (int i = 0; i < 12; i++) await Frame(silence);
        await Frame(speech);
        await Until(() => crewFrames.Count >= 2);
        Assert.Equal(new ulong[] { 1, 3 }, crewFrames.Select(f => f.TalkBurstId));
        Assert.All(crewFrames, f => Assert.Equal(VoiceChannel.Ship, f.Channel));
        Assert.All(commandFrames, f => { Assert.Equal(2UL, f.TalkBurstId); Assert.Equal(VoiceChannel.Command, f.Channel); });
    }
}

