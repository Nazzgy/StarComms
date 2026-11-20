namespace StarComms.Core;

// All calls are serialized by the audio capture lock. Each frame is 20 ms.
public sealed class VoiceActivation
{
    readonly PushToTalk manual = new();
    int quietFrames;
    bool waitingForSilence;
    public bool ShipAutomatic { get; private set; } = true;
    public bool CommandAutomatic { get; private set; }
    public VoiceChannel Preferred { get; private set; } = VoiceChannel.Ship;
    public VoiceChannel? Channel { get; private set; }
    public ulong Burst { get; private set; }

    public void Configure(bool ship, bool command)
    {
        ShipAutomatic = ship;
        CommandAutomatic = command;
        Select(VoiceChannel.Ship);
    }

    public void Select(VoiceChannel channel)
    {
        Preferred = channel;
        Disarm();
    }

    public void Disarm()
    {
        manual.Disarm();
        Channel = null;
        quietFrames = 0;
        waitingForSilence = true;
    }

    void Change(VoiceChannel? target)
    {
        if (target == Channel) return;
        Channel = target;
        if (target.HasValue) Burst = checked(Burst + 1);
    }

    public void Input(string source, VoiceChannel target, bool down, bool allowed)
    {
        if (target == VoiceChannel.Ship ? ShipAutomatic : CommandAutomatic) return;
        manual.Input(source, target, down, allowed);
        if (down || Channel == target)
        {
            Change(manual.Channel);
            waitingForSilence = true;
            quietFrames = 0;
        }
    }

    public const int DefaultThresholdDb = -35;
    double openLevel = Math.Pow(10, DefaultThresholdDb / 20d);
    public void SetThreshold(int db) => openLevel = Math.Pow(10, Math.Clamp(db, -60, -15) / 20d);
    public static double Rms(ReadOnlySpan<short> pcm)
    {
        double energy = 0;
        foreach (double sample in pcm) energy += sample * sample;
        return pcm.IsEmpty ? 0 : Math.Sqrt(energy / pcm.Length) / 32768;
    }
    public void Frame(ReadOnlySpan<short> pcm, bool shipAllowed, bool commandAllowed, bool captain)
        => Frame(Rms(pcm), shipAllowed, commandAllowed, captain);

    public void Frame(double rms, bool shipAllowed, bool commandAllowed, bool captain)
    {
        if (manual.Channel is VoiceChannel held)
        {
            if (!(held == VoiceChannel.Ship ? shipAllowed : commandAllowed)) Disarm();
            return;
        }
        VoiceChannel? target = ShipAutomatic ? VoiceChannel.Ship : null;
        if (CommandAutomatic && captain && (!ShipAutomatic || Preferred == VoiceChannel.Command))
            target = VoiceChannel.Command;
        if (manual.HasHeldInput || target == null || !(target == VoiceChannel.Ship ? shipAllowed : commandAllowed))
        {
            Change(null);
            quietFrames = 0;
            waitingForSilence = true;
            return;
        }
        if (Channel.HasValue && target != Channel) Disarm();
        // RMS hysteresis and a 240 ms release keep short gaps inside a phrase.
        var threshold = Channel.HasValue || waitingForSilence ? openLevel / 2 : openLevel;
        if (rms >= threshold)
        {
            quietFrames = 0;
            if (!waitingForSilence) Change(target);
        }
        else if (++quietFrames >= 12)
        {
            Change(null);
            quietFrames = 12;
            waitingForSilence = false;
        }
    }
}
