namespace StarComms.Core;

public enum PttState { Disarmed, Armed, TransmittingShip, TransmittingCommand, WaitForAllReleased }
public sealed class PushToTalk
{
    readonly HashSet<string> held = [];
    public bool HasHeldInput => held.Count != 0;
    string? active;
    public PttState State { get; private set; } = PttState.Disarmed;
    public VoiceChannel? Channel => State == PttState.TransmittingShip ? VoiceChannel.Ship : State == PttState.TransmittingCommand ? VoiceChannel.Command : null;
    public ulong Burst { get; private set; }
    public void Disarm() { active = null; State = PttState.Disarmed; }
    public void ReleaseAll() { held.Clear(); Disarm(); }
    public void Input(string source, VoiceChannel channel, bool down, bool allowed)
    {
        if (!down) { held.Remove(source); if (source == active) { active = null; State = PttState.WaitForAllReleased; } if (held.Count == 0) State = PttState.Armed; return; }
        if (held.Count == 0 && State == PttState.Disarmed) State = PttState.Armed;
        if (!held.Add(source) || !allowed || State != PttState.Armed) return;
        active = source; Burst = checked(Burst + 1); State = channel == VoiceChannel.Ship ? PttState.TransmittingShip : PttState.TransmittingCommand;
    }
}
