using StarComms.Core;
using StarComms.Engine;
using System.Net;
using System.Security.Cryptography;

int checks = 0;
void Check(bool value, string message) { if (!value) throw new Exception(message); Console.WriteLine("PASS " + message); checks++; }
var fleet = new Fleet("Zkušební flotila");
var people = Enumerable.Range(0, 12).Select(i => fleet.Add("Hráč " + i, i == 0)).ToArray();
var ships = Enumerable.Range(0, 3).Select(i => fleet.CreateShip("Loď " + i)).ToArray();
for (int i = 0; i < 12; i++) fleet.Move(people[i].MemberId, ships[i / 4].ShipId);
for (int i = 0; i < 3; i++) fleet.Captain(ships[i].ShipId, people[i * 4].MemberId);
var snapshot = fleet.Snapshot;
foreach (var m in snapshot.Members) {
    int shipCount = snapshot.Members.Count(r => Fleet.CanReceive(snapshot, m.MemberId, m.MembershipEpoch, r.MemberId, r.MembershipEpoch, VoiceChannel.Ship, m.ShipId!.Value));
    int commandCount = snapshot.Members.Count(r => Fleet.CanReceive(snapshot, m.MemberId, m.MembershipEpoch, r.MemberId, r.MembershipEpoch, VoiceChannel.Command, 0));
    Check(shipCount == 3 && commandCount == (m.IsCaptain ? 2 : 0), "ROUTE member " + m.MemberId);
}
var key = RandomNumberGenerator.GetBytes(32); var prefix = RandomNumberGenerator.GetBytes(4);
using var enc = new DatagramCipher(key.ToArray(), prefix, 1); using var dec = new DatagramCipher(key.ToArray(), prefix, 1);
var packet = enc.Encrypt([1, 2, 3]); var bad = packet.ToArray(); bad[^1] ^= 1;
Check(dec.Decrypt(bad) == null, "UDP bad tag"); Check(dec.Decrypt(packet)!.SequenceEqual(new byte[] { 1, 2, 3 }), "UDP roundtrip"); Check(dec.Decrypt(packet) == null, "UDP replay");
var ptt = new PushToTalk(); ptt.Input("a", VoiceChannel.Ship, true, true); ptt.Input("b", VoiceChannel.Command, true, true); Check(ptt.Channel == VoiceChannel.Ship, "PTT first wins"); ptt.Input("a", VoiceChannel.Ship, false, true); Check(ptt.Channel == null, "PTT no takeover"); ptt.Input("b", VoiceChannel.Command, false, true); ptt.Input("b", VoiceChannel.Command, true, true); Check(ptt.Channel == VoiceChannel.Command, "PTT new press"); ptt.Disarm(); ptt.Input("b", VoiceChannel.Command, true, true); Check(ptt.Channel == null, "PTT held disarm");
using var cert = Certificates.Create();
await using var host = new Host("Socket test", cert); host.Start();
await using var local = await host.ConnectLocal("Hostitel");
var clients = new List<Client> { local };
host.Changed += () => { foreach (var pending in host.Pending) host.Decide(pending.Id, true); };
try {
    for (int i = 1; i < 12; i++) { var client = new Client(); clients.Add(client); await client.Connect(new(IPAddress.Loopback, host.Port, host.Fingerprint), "Klient " + i); }
    Check(clients.All(c => c.VoiceReady), "12 TLS clients + authenticated UDP probes");
    host.Mutate(f => { var a = f.CreateShip("První"); var b = f.CreateShip("Druhá"); var c = f.CreateShip("Třetí"); for (int i = 0; i < 12; i++) f.Move(clients[i].MemberId, new[] { a.ShipId, b.ShipId, c.ShipId }[i / 4]); f.Captain(a.ShipId, clients[0].MemberId); f.Captain(b.ShipId, clients[4].MemberId); f.Captain(c.ShipId, clients[8].MemberId); });
    var deadline = DateTime.UtcNow.AddSeconds(5); while (clients.Any(c => c.Snapshot!.StateVersion != host.Fleet.Snapshot.StateVersion) && DateTime.UtcNow < deadline) await Task.Delay(10);
    var received = new int[12]; for (int i = 0; i < 12; i++) { int index = i; clients[i].Voice += _ => Interlocked.Increment(ref received[index]); }
    async Task CheckRecipients(int sender, VoiceChannel channel, ulong burst, int[] recipients)
    {
        Array.Clear(received);
        clients[sender].SendVoice(channel, burst, 0, [1]);
        await Task.Delay(150);
        var expected = Enumerable.Range(0, clients.Count).Select(i => recipients.Contains(i) ? 1 : 0);
        Check(received.SequenceEqual(expected), $"encrypted {channel}, sender {sender}, recipients {string.Join(",", recipients)}");
    }
    for (int sender = 0; sender < 12; sender++) {
        var crew = Enumerable.Range(sender / 4 * 4, 4).Where(i => i != sender).ToArray();
        await CheckRecipients(sender, VoiceChannel.Ship, 1, crew);
    }
    foreach (var captain in new[] { 0, 4, 8 }) {
        var others = new[] { 0, 4, 8 }.Where(i => i != captain).ToArray();
        await CheckRecipients(captain, VoiceChannel.Command, 2, others);
    }
    host.Mutate(f => f.Captain(f.Get(local.MemberId).ShipId!.Value, clients[1].MemberId));
    await UntilUpdated();
    await CheckRecipients(4, VoiceChannel.Command, 3, [1, 8]);
    await CheckRecipients(5, VoiceChannel.Ship, 2, [4, 6, 7]);
    async Task UntilUpdated()
    {
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (clients.Any(c => c.Snapshot!.StateVersion != host.Fleet.Snapshot.StateVersion))
            await Task.Delay(10, limit.Token);
    }
} finally { foreach (var client in clients.Skip(1)) await client.DisposeAsync(); }
Console.WriteLine($"{checks} checks passed; this is NOT Opus, audio, internet or LOAD-001 verification.");
