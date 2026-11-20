using System.Globalization;
using System.Diagnostics;
using System.Net;
using StarComms.Core;
using StarComms.Engine;

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
int seconds = args.Contains("--soak") ? 1800 : 60;
using var cert = Certificates.Create();
await using var host = new Host("Simulace dvanácti", cert);
host.ConnectionError += error => Console.Error.WriteLine(error.GetBaseException().Message);
host.Start();
host.Changed += () => { foreach (var request in host.Pending) host.Decide(request.Id, true); };
var clients = new List<Client>();
var counts = new long[12]; var mixers = Enumerable.Range(0,12).Select(_=>new VoiceMixer()).ToArray();
long sent = 0, dropped = 0, unauthorized = 0;
var process = Process.GetCurrentProcess();
try {
    clients.Add(await host.ConnectLocal("Hostitel"));
    for(int i=1;i<12;i++) { var c=new Client(); clients.Add(c); await c.Connect(new(IPAddress.Loopback,host.Port,host.Fingerprint),"Hráč "+i); }
    host.Mutate(f => { var ships=Enumerable.Range(0,3).Select(i=>f.CreateShip("Loď "+i)).ToArray();for(int i=0;i<12;i++) f.Move(clients[i].MemberId,ships[i/4].ShipId); for(int i=0;i<3;i++) f.Captain(ships[i].ShipId,clients[i*4].MemberId); });
    var timeout=Stopwatch.StartNew();while(clients.Any(c=>c.Snapshot!.StateVersion!=host.Fleet.Snapshot.StateVersion)) { if(timeout.Elapsed.TotalSeconds>5) throw new Exception("Snapshot timeout");await Task.Delay(10); }
    for(int i=0;i<12;i++) { int ix=i; clients[i].Voice += f => { var s=clients[ix].Snapshot!; if(!Expected(s, f, clients[ix].MemberId)) Interlocked.Increment(ref unauthorized);Interlocked.Increment(ref counts[ix]);mixers[ix].Push(f); };clients[i].End += mixers[i].End;clients[i].ResetAudio += mixers[i].Clear; }
    Console.WriteLine("seconds,sent,dropped,received,unauthorized,maxJitterDepth,workingSet,managedBytes,cpuSeconds");
    var clock=Stopwatch.StartNew(); var rng=new Random(7351);var encoders=Enumerable.Range(0,12).Select(_=>new VoiceEncoder()).ToArray();
    var pcm=Enumerable.Range(0,960).Select(i=>(short)(Math.Sin(i*2*Math.PI*220/48000)*9000)).ToArray();
    using var timer=new PeriodicTimer(TimeSpan.FromMilliseconds(20)); uint frame=0;ulong burst=1;
    var delayed=new PriorityQueue<(Client Client,VoiceChannel Channel,ulong Burst,uint Index,byte[] Opus),long>();
    while(clock.Elapsed.TotalSeconds<seconds && await timer.WaitForNextTickAsync()) {
        if(frame>0 && frame%100==0) { for(int i=0;i<12;i++) clients[i].EndVoice(clients[i].Self!.IsCaptain ? VoiceChannel.Command : VoiceChannel.Ship,burst,99); burst++; encoders=Enumerable.Range(0,12).Select(_=>new VoiceEncoder()).ToArray(); }
        if (frame > 0 && frame % 500 == 0) {
            int captainSeat = (int)(frame / 500 % 4);
            host.Mutate(f => {
                for (int group = 0; group < 3; group++) {
                    var captain = clients[group * 4 + captainSeat];
                    f.Captain(f.Get(captain.MemberId).ShipId!.Value, captain.MemberId);
                }
            });
        }
        for(int i=0;i<12;i++) {
            var payload=encoders[i].Encode(pcm);
            if (rng.NextDouble() < .05) { dropped++; continue; }
            delayed.Enqueue((clients[i],clients[i].Self!.IsCaptain ? VoiceChannel.Command : VoiceChannel.Ship,burst,frame%100,payload),clock.ElapsedMilliseconds+25+rng.Next(-20,21));
        }
        while(delayed.TryPeek(out _,out long due)&&due<=clock.ElapsedMilliseconds) { var f=delayed.Dequeue();f.Client.SendVoice(f.Channel,f.Burst,f.Index,f.Opus);sent++; }
        foreach(var mixer in mixers) mixer.ReadFrame();
        frame++;
        if(frame%500==0) { process.Refresh();Console.WriteLine($"{clock.Elapsed.TotalSeconds:F1},{sent},{dropped},{counts.Sum()},{unauthorized},{mixers.Max(m=>m.MaxDepth)},{process.WorkingSet64},{GC.GetTotalMemory(false)},{process.TotalProcessorTime.TotalSeconds:F2}"); }
    }
    if(unauthorized>0 || counts.Any(c=>c==0) || mixers.Any(m=>m.MaxDepth>6)) throw new Exception("Simulation assertions failed");
    Console.WriteLine($"PASS TLS/UDP/Opus clients=12 duration={clock.Elapsed.TotalSeconds:F1}s sent={sent} dropped={dropped} received={counts.Sum()} unauthorized={unauthorized} maxDepth={mixers.Max(m=>m.MaxDepth)}");
} finally { foreach(var c in clients) await c.DisposeAsync(); }

static bool Expected(FleetSnapshot fleet, VoiceFrame frame, uint recipient)
{
    var from = fleet.Members.SingleOrDefault(m => m.MemberId == frame.SenderId);
    var to = fleet.Members.SingleOrDefault(m => m.MemberId == recipient);
    if (from == null || to == null || from == to || from.Status != MemberStatus.Online || to.Status != MemberStatus.Online)
        return false;
    if (from.MembershipEpoch != frame.SenderEpoch || to.MembershipEpoch != frame.RecipientEpoch)
        return false;
    return frame.Channel switch {
        VoiceChannel.Ship => from.ShipId == frame.ShipId && to.ShipId == frame.ShipId,
        VoiceChannel.Command => frame.ShipId == 0 && from.IsCaptain && to.IsCaptain,
        _ => false
    };
}
