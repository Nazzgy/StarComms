using StarComms.Core;
using Xunit;

namespace StarComms.Tests;

public class VoiceActivationTests
{
    static readonly short[] Speech = Enumerable.Repeat((short)1600, 960).ToArray();
    static readonly short[] Silence = new short[960];
    static void Pause(VoiceActivation voice)
    {
        for (int i = 0; i < 12; i++) voice.Frame(Silence, true, true, true);
    }

    [Theory]
    [InlineData(true, false, VoiceChannel.Ship)]
    [InlineData(false, true, VoiceChannel.Command)]
    public void HeldButtonOverridesSpeechUntilNextPhrase(bool ship, bool command, VoiceChannel auto)
    {
        var voice = new VoiceActivation();
        voice.Configure(ship, command);
        Pause(voice);
        voice.Frame(Speech, true, true, true);
        Assert.Equal(auto, voice.Channel);
        var manual = ship ? VoiceChannel.Command : VoiceChannel.Ship;
        voice.Input("key", manual, true, true);
        voice.Frame(Speech, true, true, true);
        Assert.Equal(manual, voice.Channel);
        voice.Input("key", manual, false, true);
        voice.Frame(Speech, true, true, true);
        Assert.Null(voice.Channel);
        Pause(voice);
        voice.Frame(Speech, true, true, true);
        Assert.Equal(auto, voice.Channel);
        Assert.Equal(3UL, voice.Burst);
    }

    [Fact]
    public void TwoAutomaticChannelsRequireAnExplicitTargetAndPermission()
    {
        var voice = new VoiceActivation();
        voice.Configure(true, true);
        Pause(voice);
        voice.Frame(Speech, true, true, true);
        Assert.Equal(VoiceChannel.Ship, voice.Channel);
        voice.Select(VoiceChannel.Command);
        Pause(voice);
        voice.Frame(Speech, true, true, true);
        Assert.Equal(VoiceChannel.Command, voice.Channel);
        voice.Frame(Speech, true, false, true);
        Assert.Null(voice.Channel);
        voice.Frame(Speech, true, false, false);
        Assert.Null(voice.Channel);
    }

    [Fact]
    public void ManualModesNeverOpenOnSpeech()
    {
        var voice = new VoiceActivation();
        voice.Configure(false, false);
        Pause(voice);
        voice.Frame(Speech, true, true, true);
        Assert.Null(voice.Channel);
    }
}
