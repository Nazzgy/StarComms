using StarComms.App;
using Xunit;

namespace StarComms.Tests;

public sealed class LoggingTests
{
    [Fact]
    public async Task DisabledRotationAndBoundedQueue()
    {
        string folder = Directory.CreateTempSubdirectory("starcomms-log-").FullName;
        try {
            await using var log = new DiagnosticLog(folder, 4000);
            log.Event("START");
            Assert.Empty(Directory.GetFiles(folder));
            string foreign = Path.Combine(folder, "StarComms-foreign.log");
            await File.WriteAllTextAsync(foreign, "keep");
            await log.Set(true);
            for (int batch = 0; batch < 8; batch++) {
                for (int i = 0; i < 250; i++) log.Event("CONNECTED", 7);
                await Task.Delay(20);
            }
            Parallel.For(0, 50000, i => log.Event("TOP_SECRET", (uint)i));
            await log.Set(false);
            var own = Directory.GetFiles(folder).Where(p => p != foreign).ToArray();
            Assert.Equal(5, own.Length);
            Assert.Equal("keep", await File.ReadAllTextAsync(foreign));
            var contents = string.Join(" ", own.Select(File.ReadAllText));
            Assert.DoesNotContain("TOP_SECRET", contents);
            Assert.Contains("ERROR member=", contents);
            log.Event("LEFT");
            Assert.DoesNotContain("LEFT", string.Join(" ", own.Select(File.ReadAllText)));
            foreach (var path in own) using (File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
        } finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public async Task UnwritableDestinationRaisesOneFailure()
    {
        string file = Path.GetTempFileName();
        try {
            await using var log = new DiagnosticLog(file, 1024);
            int errors = 0;
            log.Failed += () => errors++;
            await log.Set(true);
            Assert.False(log.Enabled);
            Assert.Equal(1, errors);
            log.Event("START");
            Assert.Equal(0, new FileInfo(file).Length);
        } finally { File.Delete(file); }
    }
}
