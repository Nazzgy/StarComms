using System.Globalization;
using System.Reflection;
using System.Text;

namespace StarComms.Core;

public static class VersionInfo
{
    public static string Full => typeof(VersionInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
    public static string Display => "v" + Full.Replace("+", " · build ");
}
public enum VoiceChannel : byte { Ship = 1, Command = 2 }
public enum MemberStatus { Online, Reconnecting }
public sealed record Member(uint MemberId, string Nickname, uint? ShipId = null, bool IsCaptain = false, bool IsHost = false, MemberStatus Status = MemberStatus.Online, uint MembershipEpoch = 1);
public sealed record Ship(uint ShipId, string Name, string Model = "", uint? CaptainMemberId = null);
public sealed record FleetSnapshot(Guid SessionId, ulong StateVersion, string FleetName, Member[] Members, Ship[] Ships);
public sealed class RuleException(string code) : Exception(code);

// The host serializes mutations and final voice sends under Sync. Snapshots contain immutable records.
public sealed class Fleet
{
    public object Sync { get; } = new();
    readonly Dictionary<uint, Member> members = [];
    readonly Dictionary<uint, Ship> ships = [];
    uint nextMember, nextShip;
    ulong version;
    public Guid SessionId { get; } = Guid.NewGuid();
    public string Name { get; }
    public Fleet(string name) => Name = Text(name, 1, 40);
    public static string Text(string value, int min, int max)
    {
        value = value.Trim().Normalize(NormalizationForm.FormC);
        var length = new StringInfo(value).LengthInTextElements;
        if (length < min || length > max || value.Any(char.IsControl)) throw new RuleException("INVALID_NAME");
        return value;
    }
    public FleetSnapshot Snapshot { get { lock (Sync) return new(SessionId, version, Name, members.Values.ToArray(), ships.Values.ToArray()); } }
    public Member Get(uint id) { lock (Sync) return members.TryGetValue(id, out var m) ? m : throw new RuleException("NOT_FOUND"); }
    void Changed(Member m) { members[m.MemberId] = m with { MembershipEpoch = checked(m.MembershipEpoch + 1) }; }
    public Member Add(string name, bool host = false)
    {
        lock (Sync) {
            name = Text(name, 2, 24);
            if (members.Values.Any(m => string.Equals(m.Nickname, name, StringComparison.OrdinalIgnoreCase))) throw new RuleException("NAME_IN_USE");
            var m = new Member(checked(++nextMember), name, IsHost: host); members.Add(m.MemberId, m); version++; return m;
        }
    }
    public Ship CreateShip(string name, string model = "")
    {
        lock (Sync) { if (ships.Count >= 12) throw new RuleException("FULL"); var s = new Ship(checked(++nextShip), Text(name, 1, 32), Text(model, 0, 48)); ships.Add(s.ShipId, s); version++; return s; }
    }
    public void RenameShip(uint id, string name, string model)
    { lock (Sync) { ships[id] = ships[id] with { Name = Text(name, 1, 32), Model = Text(model, 0, 48) }; version++; } }
    public void Move(uint id, uint? ship, uint? expectedEpoch = null)
    {
        lock (Sync) {
            var m = Get(id); if (expectedEpoch.HasValue && m.MembershipEpoch != expectedEpoch) throw new RuleException("STALE_STATE");
            if (ship.HasValue && !ships.ContainsKey(ship.Value)) throw new RuleException("NOT_FOUND");
            if (m.ShipId == ship) return;
            if (m.IsCaptain && m.ShipId.HasValue) ships[m.ShipId.Value] = ships[m.ShipId.Value] with { CaptainMemberId = null };
            Changed(m with { ShipId = ship, IsCaptain = false }); version++;
        }
    }
    public void Captain(uint ship, uint target, uint? actor = null, uint? expectedEpoch = null)
    {
        lock (Sync) {
            if (!ships.TryGetValue(ship, out var s)) throw new RuleException("NOT_FOUND");
            var m = Get(target);
            if (actor.HasValue) { var a = Get(actor.Value); if (a.MembershipEpoch != expectedEpoch) throw new RuleException("STALE_STATE"); if (s.CaptainMemberId != actor) throw new RuleException("FORBIDDEN"); }
            if (m.ShipId != ship || m.Status != MemberStatus.Online) throw new RuleException("FORBIDDEN");
            if (s.CaptainMemberId == target) return;
            if (s.CaptainMemberId is uint old) Changed(Get(old) with { IsCaptain = false });
            Changed(m with { IsCaptain = true }); ships[ship] = s with { CaptainMemberId = target }; version++;
        }
    }
    public void ClaimCaptain(uint ship, uint actor, uint expectedEpoch)
    {
        lock (Sync) {
            var member = Get(actor);
            if (member.MembershipEpoch != expectedEpoch) throw new RuleException("STALE_STATE");
            if (member.ShipId != ship || member.Status != MemberStatus.Online) throw new RuleException("FORBIDDEN");
            if (ships[ship].CaptainMemberId is uint captain && captain != actor) throw new RuleException("CAPTAIN_TAKEN");
            Captain(ship, actor);
        }
    }
    public void DeleteEmptyShip(uint id)
    {
        lock (Sync) {
            if (!ships.ContainsKey(id)) throw new RuleException("NOT_FOUND");
            if (members.Values.Any(m => m.ShipId == id)) throw new RuleException("SHIP_OCCUPIED");
            DeleteShip(id);
        }
    }
    public void DeleteShip(uint id)
    { lock (Sync) { foreach (var m in members.Values.Where(m => m.ShipId == id).ToArray()) Changed(m with { ShipId = null, IsCaptain = false }); ships.Remove(id); version++; } }
    public void SetStatus(uint id, MemberStatus status) { lock (Sync) { Changed(Get(id) with { Status = status }); version++; } }
    public void Remove(uint id)
    { lock (Sync) { var m = Get(id); if (m.IsCaptain && m.ShipId.HasValue) ships[m.ShipId.Value] = ships[m.ShipId.Value] with { CaptainMemberId = null }; members.Remove(id); version++; } }
    public static bool CanReceive(FleetSnapshot s, uint sender, uint senderEpoch, uint recipient, uint recipientEpoch, VoiceChannel channel, uint ship)
    {
        var a = s.Members.FirstOrDefault(m => m.MemberId == sender); var b = s.Members.FirstOrDefault(m => m.MemberId == recipient);
        if (a == null || b == null || a == b || a.Status != MemberStatus.Online || b.Status != MemberStatus.Online || a.MembershipEpoch != senderEpoch || b.MembershipEpoch != recipientEpoch || a.ShipId == null || b.ShipId == null) return false;
        return channel switch { VoiceChannel.Ship => ship == a.ShipId && b.ShipId == a.ShipId, VoiceChannel.Command => ship == 0 && a.IsCaptain && b.IsCaptain, _ => false };
    }
}
