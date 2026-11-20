using StarComms.Engine;
using StarComms.Core;
using Xunit;
namespace StarComms.Tests;
public class AudioTests
{
    [Fact] public void AUD001_ActualOpusRoundtrip() { var pcm=Enumerable.Range(0,960).Select(i=>(short)(Math.Sin(i*2*Math.PI*440/48000)*10000)).ToArray();var bytes=new VoiceEncoder().Encode(pcm);Assert.InRange(bytes.Length,1,1000);var decoded=new VoiceDecoder().Decode(bytes);Assert.Equal(960,decoded.Length);Assert.Contains(decoded,x=>x!=0); }
    [Fact] public void AUD006_IndependentBursts() { var pcm=Enumerable.Range(0,960).Select(i=>(short)(i%200*50)).ToArray();for(int i=0;i<20;i++) Assert.Equal(960,new VoiceDecoder().Decode(new VoiceEncoder().Encode(pcm)).Length); }

    sealed class Clock : TimeProvider
    {
        long ticks;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => ticks;
        public void Advance(int milliseconds) => ticks += milliseconds;
    }

    [Fact]
    public void AUD002_LossBacklogAndRecovery()
    {
        var clock = new Clock();
        var mixer = new VoiceMixer(clock);
        var encoder = new VoiceEncoder();
        var pcm = Enumerable.Range(0, 960).Select(i => (short)(9000 * Math.Sin(i / 15d))).ToArray();
        var packets = Enumerable.Range(0, 32).Select(_ => encoder.Encode(pcm)).ToArray();
        void Push(int i) => mixer.Push(new VoiceFrame(VoiceChannel.Ship, 1, 1, 1, 1, 1, (uint)i, packets[i]));
        Push(0); Push(2); Push(2); Push(3);
        Assert.All(mixer.ReadFrame(), sample => Assert.Equal(0, sample));
        clock.Advance(60);
        Assert.Contains(mixer.ReadFrame(), x => x != 0);
        for (int i = 0; i < 15; i++) { clock.Advance(20); mixer.ReadFrame(); }
        Push(20); Push(22); Push(21);
        clock.Advance(60);
        Assert.Contains(mixer.ReadFrame(), x => x != 0);
        for (int i = 23; i < 32; i++) Push(i);
        Assert.InRange(mixer.MaxDepth, 1, 6);
        clock.Advance(600);
        Assert.All(mixer.ReadFrame(), sample => Assert.Equal(0, sample));
        Assert.Empty(mixer.ActiveSpeakers());
    }

    [Fact]
    public void AUD003_SenderGainAndDucking()
    {
        var clock = new Clock();
        var mixer = new VoiceMixer(clock);
        var encoder = new VoiceEncoder();
        var pcm = Enumerable.Range(0, 960).Select(i => (short)(8000 * Math.Sin(i * 2 * Math.PI / 80))).ToArray();
        double Play(int i, bool command)
        {
            var bytes = encoder.Encode(pcm);
            mixer.Push(new VoiceFrame(VoiceChannel.Ship, 1, 1, 1, 3, 1, (uint)i, bytes));
            if (command) mixer.Push(new VoiceFrame(VoiceChannel.Command, 2, 1, 1, 0, 1, (uint)i, bytes));
            clock.Advance(20);
            var frame = mixer.ReadFrame();
            Assert.All(frame, x => Assert.InRange(x, -1, 1));
            return Math.Sqrt(frame.Average(sample => sample * sample));
        }
        for (int i = 0; i < 10; i++) Play(i, false);
        var normal = Play(10, false);
        Assert.True(normal > .1);
        mixer.MemberVolume(2, 0);
        for (int i = 11; i < 22; i++) Play(i, true);
        Assert.InRange(Play(22, true) / normal, .30, .55);
        for (int i = 23; i < 75; i++) Play(i, false);
        Assert.InRange(Play(75, false) / normal, .9, 1.1);
        mixer.MemberVolume(1, 0);
        Assert.Equal(0, Play(76, false));
        mixer.ResetSession();
        Assert.Equal(1, mixer.MemberVolume(1));
    }
}
