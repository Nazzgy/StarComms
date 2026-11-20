using System.Buffers.Binary;
using System.Net;
using System.Security.Cryptography;
using StarComms.Core;
using StarComms.Engine;
using Xunit;

namespace StarComms.Tests;
public class CoreTests
{
    [Theory]
    [InlineData("127.0.0.1", 49555)] [InlineData("127.0.0.1:1234", 1234)] [InlineData("::1", 49555)] [InlineData("[::1]:49999", 49999)]
    public void CON002_Valid(string input, int port) => Assert.Equal(port, JoinAddress.Parse(input).Port);
    [Theory]
    [InlineData("0.0.0.0")] [InlineData("::")] [InlineData("127.0.0.1:0")] [InlineData("127.0.0.1:65536")] [InlineData("host.example")] [InlineData("[::1]:abc")] [InlineData("224.0.0.1")] [InlineData("starcomms://join?v=1&v=1")]
    [InlineData("127.1")] [InlineData("0x7f000001")] [InlineData("0177.0.0.1")]
    public void CON002_Invalid(string input) => Assert.Throws<RuleException>(() => JoinAddress.Parse(input));
    [Fact] public void CON002_Invitation() { var original = new JoinAddress(IPAddress.IPv6Loopback, 49555, new string('a',64)); Assert.Equal(original, JoinAddress.Parse(original.Invitation(original.Fingerprint!))); }
    [Fact] public void AUTH003_CapacityAndUnicode() { var f = new Fleet("Žluťoučká flotila"); f.Add("Jose\u0301"); Assert.Throws<RuleException>(() => f.Add("JOSÉ")); for(int i=1;i<12;i++) f.Add("Hráč"+i); Assert.Throws<RuleException>(()=>f.Add("Třináctý")); }
    [Fact] public void ROUTE_RoleRevocation()
    {
        var f = new Fleet("Test"); var a=f.Add("Host",true); var b=f.Add("Crew"); var c=f.Add("Captain"); var s=f.CreateShip("A"); var t=f.CreateShip("B"); f.Move(a.MemberId,s.ShipId); f.Move(b.MemberId,s.ShipId); f.Move(c.MemberId,t.ShipId); f.Captain(s.ShipId,b.MemberId); f.Captain(t.ShipId,c.MemberId);
        var snap=f.Snapshot; var members=snap.Members; a=members[0];b=members[1];c=members[2];
        Assert.False(Fleet.CanReceive(snap,c.MemberId,c.MembershipEpoch,a.MemberId,a.MembershipEpoch,VoiceChannel.Command,0));
        Assert.True(Fleet.CanReceive(snap,c.MemberId,c.MembershipEpoch,b.MemberId,b.MembershipEpoch,VoiceChannel.Command,0));
        f.Move(b.MemberId,t.ShipId,b.MembershipEpoch); Assert.False(f.Get(b.MemberId).IsCaptain);
        Assert.False(Fleet.CanReceive(f.Snapshot,c.MemberId,c.MembershipEpoch,b.MemberId,b.MembershipEpoch,VoiceChannel.Command,0));
        Assert.Throws<RuleException>(()=>f.Move(b.MemberId,s.ShipId,b.MembershipEpoch));
    }
    [Fact] public void ROLE004_TransferRequiresCurrentCaptain() { var f=new Fleet("T"); var a=f.Add("AA"); var b=f.Add("BB");var s=f.CreateShip("S");f.Move(a.MemberId,s.ShipId); f.Move(b.MemberId,s.ShipId); Assert.Throws<RuleException>(()=>f.Captain(s.ShipId,b.MemberId,a.MemberId,f.Get(a.MemberId).MembershipEpoch)); }
    [Fact] public void ROLE003_DeleteShipClearsRoles() { var f=new Fleet("T");var a=f.Add("AA");var s=f.CreateShip("S"); f.Move(a.MemberId,s.ShipId);f.Captain(s.ShipId,a.MemberId);f.DeleteShip(s.ShipId);Assert.Null(f.Get(a.MemberId).ShipId);Assert.False(f.Get(a.MemberId).IsCaptain); }
    [Theory] [InlineData(VoiceChannel.Ship,VoiceChannel.Command)] [InlineData(VoiceChannel.Command,VoiceChannel.Ship)]
    public void PTT002_FirstWins(VoiceChannel first,VoiceChannel second) { var p=new PushToTalk();p.Input("1",first,true,true);p.Input("1",first,true,true);p.Input("2",second,true,true);Assert.Equal(first,p.Channel);Assert.Equal(1UL,p.Burst);p.Input("1",first,false,true);Assert.Null(p.Channel);p.Input("2",second,true,true);Assert.Null(p.Channel);p.Input("2",second,false,true);p.Input("2",second,true,true);Assert.Equal(second,p.Channel); }
    [Fact] public void PTT003_HeldAcrossDisarm() { var p=new PushToTalk();p.Input("a",VoiceChannel.Ship,true,true);p.Disarm();p.Input("a",VoiceChannel.Ship,true,true);Assert.Null(p.Channel);p.Input("a",VoiceChannel.Ship,false,true);p.Input("a",VoiceChannel.Ship,true,true);Assert.Equal(VoiceChannel.Ship,p.Channel); }
    [Fact] public void UDP001_AllTamperedBitsRejected()
    {
        var key=RandomNumberGenerator.GetBytes(32);var prefix=new byte[]{1,2,3,4}; using var enc=new DatagramCipher(key.ToArray(),prefix,42);var packet=enc.Encrypt([1,2,3]);
        Assert.Equal("FCV1",System.Text.Encoding.ASCII.GetString(packet,0,4));Assert.Equal(42UL,BinaryPrimitives.ReadUInt64BigEndian(packet.AsSpan(8)));Assert.Equal(0UL,BinaryPrimitives.ReadUInt64BigEndian(packet.AsSpan(16)));
        for(int i=0;i<packet.Length;i++) for(int bit=0;bit<8;bit++) { using var dec=new DatagramCipher(key.ToArray(),prefix,42);var bad=packet.ToArray();bad[i]^=(byte)(1<<bit);Assert.Null(dec.Decrypt(bad)); }
    }
    [Fact] public void UDP002_ReorderingReplayAndBadHighSequence()
    {
        var key=RandomNumberGenerator.GetBytes(32);var prefix=new byte[4];using var enc=new DatagramCipher(key.ToArray(),prefix,2);using var dec=new DatagramCipher(key.ToArray(),prefix,2);var packets=Enumerable.Range(0,300).Select(_=>enc.Encrypt([4])).ToArray();
        var bad=packets[0].ToArray();BinaryPrimitives.WriteUInt64BigEndian(bad.AsSpan(16),ulong.MaxValue);Assert.Null(dec.Decrypt(bad)); Assert.NotNull(dec.Decrypt(packets[3]));Assert.NotNull(dec.Decrypt(packets[1]));Assert.Null(dec.Decrypt(packets[1]));Assert.NotNull(dec.Decrypt(packets[299]));Assert.Null(dec.Decrypt(packets[0]));
    }
    [Fact] public void UDP003_ConcurrentNonceUniqueness() { using var enc=new DatagramCipher(RandomNumberGenerator.GetBytes(32),new byte[4],1);var sequences=new System.Collections.Concurrent.ConcurrentBag<ulong>();Parallel.For(0,1000,_=>sequences.Add(BinaryPrimitives.ReadUInt64BigEndian(enc.Encrypt([1]).AsSpan(16))));Assert.Equal(1000,sequences.Distinct().Count()); }
    [Fact] public void UDP005_BoundedMalformedPackets() { using var dec=new DatagramCipher(new byte[32],new byte[4],1);for(int n=0;n<1500;n++) Assert.Null(dec.Decrypt(new byte[n]));Assert.Null(VoiceFrame.Parse(new byte[35])); }
    [Fact] public void UDP007_EndBeforeVoiceAndGenerationReset() { var time=new Clock();var b=new BurstGuard(time);Assert.True(b.Accept(1,40,VoiceChannel.Ship,1,3,true));Assert.True(b.Accept(1,40,VoiceChannel.Ship,1,2));Assert.False(b.Accept(1,40,VoiceChannel.Command,0,2));time.Advance(201);Assert.False(b.Accept(1,40,VoiceChannel.Ship,1,3));Assert.True(b.Accept(2,1,VoiceChannel.Ship,1,0)); }
    [Fact] public async Task TCP001_OneByteReadsAndMultipleFrames() { using var stream=new MemoryStream();await Wire.Write(stream,Envelope.Make("Ping",new{nonce=1UL}),default);await Wire.Write(stream,Envelope.Make("Ping",new{nonce=2UL}),default); stream.Position=0;var fragmented=new FragmentStream(stream.ToArray());Assert.Equal(1UL,(await Wire.Read(fragmented,default)).Body.GetProperty("nonce").GetUInt64());Assert.Equal(2UL,(await Wire.Read(fragmented,default)).Body.GetProperty("nonce").GetUInt64()); }
    [Theory] [InlineData(0)] [InlineData(65537)] public async Task TCP001_InvalidLength(uint length) { byte[] prefix=new byte[4];BinaryPrimitives.WriteUInt32BigEndian(prefix,length);await Assert.ThrowsAsync<InvalidDataException>(()=>Wire.Read(new MemoryStream(prefix),default)); }
    [Fact] public void TLS001_ExactPin() { using var c=Certificates.Create(); Assert.True(Certificates.Valid(c,Certificates.Fingerprint(c)));Assert.False(Certificates.Valid(c,new string('0',64)));Assert.False(Certificates.Valid(c,null)); }
    [Fact] public void VER001_CentralVersion() => Assert.StartsWith("0.1.0+",VersionInfo.Full);
    sealed class Clock:TimeProvider { long value;public override long TimestampFrequency=>1000;public override long GetTimestamp()=>value;public void Advance(long ms)=>value+=ms; }
    sealed class FragmentStream(byte[] bytes):MemoryStream(bytes) { public override ValueTask<int> ReadAsync(Memory<byte> buffer,CancellationToken ct=default)=>base.ReadAsync(buffer[..Math.Min(1,buffer.Length)],ct); }
}
