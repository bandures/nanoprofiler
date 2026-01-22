using System;
using System.IO;
using System.Text;
using NanoProfiler;
using NUnit.Framework;

namespace NanoProfiler.Tests;

public static class PerfettoProcessorTests
{
    [Test, Timeout(1000)]
    public static void PerfettoProcessor_CaptureStartStop_FileExists()
    {
        var testFileName = Path.GetTempFileName();
            
        var processor = new PerfettoProcessor(new PerfettoProcessorSettings() { FileName = testFileName });
            
        var startMsg = new Protocol.CaptureStart();
        processor.HandleMessage(0, ref startMsg);
        var stopMsg = new Protocol.CaptureStop();
        processor.HandleMessage(0, ref stopMsg);

        File.Exists(testFileName);
    }

    [Test, Timeout(1000)]
    public static void PerfettoProcessor_WithInstruments_NoExceptions()
    {
        var fileName = Path.GetTempFileName();

        var testProcessor = new PerfettoProcessor(new PerfettoProcessorSettings() { FileName = fileName });
        var settings = new ProfilerSettingsBuilder()
            .AddProcessor(_ => testProcessor)
            .EnableGroups("group")
            .Build();
            
        using (var profiler = new Profiler(settings))
        {
            TestAllInstruments(profiler);
        }
            
        Assert.IsTrue(File.Exists(fileName));
    }

    [Test, Timeout(1000)]
    public static void PerfettoProcessor_UsingScope_NameInternalized()
    {
        const int iterationCount = 1000;
        
        var fileName = Path.GetTempFileName();

        var testProcessor = new PerfettoProcessor(new PerfettoProcessorSettings() { FileName = fileName });
        var settings = new ProfilerSettingsBuilder()
            .AddProcessor(_ => testProcessor)
            .Build();

        var longName = GenerateRandomString(100);
        using (var profiler = new Profiler(settings))
        {
            var scope = profiler.CreateScope(longName);
            
            profiler.Start();
            for (var i = 0; i < iterationCount; i++)
            {
                using var _ = scope.Auto();
            }
            profiler.Stop();
        }
            
        var fileInfo = new FileInfo(fileName);
        Assert.IsTrue(File.Exists(fileName));
        Assert.NotZero(fileInfo.Length);
        Assert.LessOrEqual(fileInfo.Length, 70 * 1024);
    }

    [Test, Timeout(1000)]
    public static void PerfettoProcessor_UsingActivity_NameInternalized()
    {
        const int iterationCount = 1000;
        
        var fileName = Path.GetTempFileName();

        var testProcessor = new PerfettoProcessor(new PerfettoProcessorSettings() { FileName = fileName });
        var settings = new ProfilerSettingsBuilder()
            .AddProcessor(_ => testProcessor)
            .Build();

        var longName = GenerateRandomString(100);
        using (var profiler = new Profiler(settings))
        {
            var activity = profiler.CreateActivity(longName);
            
            profiler.Start();
            for (var i = 0; i < iterationCount; i++)
            {
                using var _ = activity.Auto();
            }
            profiler.Stop();
        }
            
        var fileInfo = new FileInfo(fileName);
        Assert.IsTrue(File.Exists(fileName));
        Assert.NotZero(fileInfo.Length);
        Assert.LessOrEqual(fileInfo.Length, 70 * 1024);
    }

    [Test, Timeout(1000)]
    public static void PerfettoProcessor_WithRestart_ValidStream()
    {
        var fileName = Path.GetTempFileName();

        var testProcessor = new PerfettoProcessor(new PerfettoProcessorSettings() { FileName = fileName });
        var settings = new ProfilerSettingsBuilder()
            .AddProcessor(_ => testProcessor)
            .EnableGroups("group")
            .Build();

        {
            using var profiler1 = new Profiler(settings);
            TestAllInstruments(profiler1);

            using var profiler2 = new Profiler(settings, profiler1);
            TestAllInstruments(profiler2);
        }
            
        Assert.IsTrue(File.Exists(fileName));
    }

    private static void TestAllInstruments(Profiler profiler)
    {
        var group = profiler.CreateGroup("group");
        var scope = profiler.CreateScope("scope");
        var counter = profiler.CreateCounter("counter");
        var activity = profiler.CreateActivity("activity");
        var scopeInGroup = group.CreateScope("scopeInGroup");
            
        profiler.Start();
        {
            counter.Sample(2.0f);
            using var _1 = scope.Auto();
            using var _2 = activity.Auto();
            using var _3 = scopeInGroup.Auto();
        }
        profiler.Stop();
    }

    private static string GenerateRandomString(int length)
    {
        var sb = new StringBuilder(length);
        var rng = new Random();
        for (var i = 0; i < length; i++)
        {
            var ch = (char)rng.Next(65, 90);
            sb.Append(ch);
        }
        return sb.ToString();
    }
}
