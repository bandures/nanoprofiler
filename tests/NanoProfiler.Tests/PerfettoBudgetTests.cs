using System.Collections.Generic;
using System.IO;
using System.Linq;
using NanoProfiler;
using NUnit.Framework;
using Perfetto.Protos;

namespace NanoProfiler.Tests;

/// <summary>End-to-end tests for budget gating, criticality, spikes and metadata.</summary>
public sealed class PerfettoBudgetTests
{
    private const uint NormalMarker = 1;
    private const uint CriticalMarker = 2;
    private const uint CriticalGroup = 10;
    private const int FrameMs = 1; // scope timestamps advance by this within a frame

    private sealed class Driver
    {
        private readonly PerfettoProcessor _processor;
        private long _ts;

        public Driver(PerfettoProcessorSettings settings)
        {
            _processor = new PerfettoProcessor(settings);
            var start = new Protocol.CaptureStart();
            _processor.HandleMessage(0, ref start);

            // Criticality is a group property; the critical marker inherits it.
            var group = new Protocol.RegisterGroup { GroupId = CriticalGroup, Name = "Critical", Flags = MarkerFlags.Critical };
            _processor.HandleMessage(0, ref group);

            var normal = new Protocol.RegisterMarker { MarkerId = NormalMarker, GroupId = 0, Name = "Normal", Flags = MarkerFlags.Default };
            _processor.HandleMessage(0, ref normal);
            var critical = new Protocol.RegisterMarker { MarkerId = CriticalMarker, GroupId = CriticalGroup, Name = "Critical", Flags = MarkerFlags.Default };
            _processor.HandleMessage(0, ref critical);
        }

        public void Scope(uint markerId, long durationTicks)
        {
            Begin(markerId);
            _ts += durationTicks;
            End(markerId);
        }

        public void Begin(uint markerId)
        {
            _ts += 1;
            var begin = new Protocol.ScopeBegin { MarkerId = markerId, Timestamp = _ts };
            _processor.HandleMessage(0, ref begin);
        }

        public void End(uint markerId)
        {
            _ts += 1;
            var end = new Protocol.ScopeEnd { MarkerId = markerId, Timestamp = _ts };
            _processor.HandleMessage(0, ref end);
        }

        /// <summary>Advances one frame lasting <paramref name="frameMs"/> milliseconds.</summary>
        public void Frame(double frameMs)
        {
            _ts += TimeHelper.MillisecondsToTimestamp(frameMs);
            var frame = new Protocol.Frame { Timestamp = _ts };
            _processor.HandleMessage(0, ref frame);
        }

        public void Stop()
        {
            var stop = new Protocol.CaptureStop { Timestamp = _ts };
            _processor.HandleMessage(0, ref stop);
            _processor.Dispose();
        }
    }

    private static Trace ParseTrace(string fileName)
    {
        // The file is a concatenation of serialized Trace messages; protobuf merges
        // their repeated `packet` fields when the whole file is parsed at once.
        var bytes = File.ReadAllBytes(fileName);
        return Trace.Parser.ParseFrom(bytes);
    }

    private static int SliceBeginCount(Trace trace) =>
        trace.Packet.Count(p => p.TrackEvent is { Type: TrackEvent.Types.Type.SliceBegin });

    private static int SliceEndCount(Trace trace) =>
        trace.Packet.Count(p => p.TrackEvent is { Type: TrackEvent.Types.Type.SliceEnd });

    // ---------------------------------------------------------------------

    [Test]
    public static void SlowDevice_DoesNotReplayEveryFrame()
    {
        // A device running steadily at 70ms/frame must NOT trip the spike detector every frame.
        var fileName = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var driver = new Driver(new PerfettoProcessorSettings
        {
            FileName = fileName,
            DynamicScopes = true,
            StartupFramesToWrite = 5,
        });

        const int frames = 500;
        const int scopesPerFrame = 10;
        for (var f = 0; f < frames; f++)
        {
            for (var s = 0; s < scopesPerFrame; s++)
                driver.Scope(NormalMarker, TimeHelper.MillisecondsToTimestamp(FrameMs));
            driver.Frame(70.0);
        }
        driver.Stop();

        var begins = SliceBeginCount(ParseTrace(fileName));
        // Only the startup window (plus a warm-up frame or two) should replay. If every
        // frame replayed this would be ~frames * scopesPerFrame = 5000.
        Assert.LessOrEqual(begins, 15 * scopesPerFrame,
            $"steady slow frames should not keep replaying (got {begins} scope begins)");
        File.Delete(fileName);
    }

    [Test]
    public static void FastGame_WithSpikes_ReplaysTheSpikes()
    {
        // 33ms baseline with a 200ms spike every 50 frames: the spikes must be captured.
        var fileName = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var driver = new Driver(new PerfettoProcessorSettings
        {
            FileName = fileName,
            DynamicScopes = true,
            StartupFramesToWrite = 5,
        });

        const int frames = 300;
        const int scopesPerFrame = 8;
        var spikeCount = 0;
        for (var f = 0; f < frames; f++)
        {
            for (var s = 0; s < scopesPerFrame; s++)
                driver.Scope(NormalMarker, TimeHelper.MillisecondsToTimestamp(FrameMs));

            var isSpike = f > 10 && f % 50 == 0;
            if (isSpike) spikeCount++;
            driver.Frame(isSpike ? 200.0 : 33.0);
        }
        driver.Stop();

        var begins = SliceBeginCount(ParseTrace(fileName));
        Assert.GreaterOrEqual(spikeCount, 3, "sanity: the test should produce several spikes");
        // Startup replays (5) + each spike replays a frame of scopes.
        Assert.GreaterOrEqual(begins, (5 + spikeCount) * scopesPerFrame - scopesPerFrame,
            $"spikes should be replayed (got {begins} scope begins for {spikeCount} spikes)");
        File.Delete(fileName);
    }

    [Test]
    public static void HardBudget_StopsWriting()
    {
        // With a tiny hard ceiling the file must stop growing well before a large
        // unbudgeted run would, and stay near the ceiling.
        var fileName = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        const long maxBytes = 8 * 1024;
        var driver = new Driver(new PerfettoProcessorSettings
        {
            FileName = fileName,
            DynamicScopes = false,
            CriticalBudgetBytes = 4 * 1024,
            MaxFileSizeBytes = maxBytes,
        });

        for (var f = 0; f < 2000; f++)
        {
            for (var s = 0; s < 20; s++)
                driver.Scope(CriticalMarker, TimeHelper.MillisecondsToTimestamp(FrameMs));
            driver.Frame(16.0);
        }
        driver.Stop();

        var size = new FileInfo(fileName).Length;
        // Even critical events are dropped past the hard ceiling. Allow a small margin
        // for the in-flight batch flushed at the frame the ceiling was crossed.
        Assert.LessOrEqual(size, maxBytes + 4 * 1024,
            $"file must stop near the hard ceiling (was {size} bytes)");
        File.Delete(fileName);
    }

    [Test]
    public static void SoftBudget_KeepsCriticalDropsNonCritical()
    {
        // Past the soft budget only critical scopes survive.
        var fileName = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var driver = new Driver(new PerfettoProcessorSettings
        {
            FileName = fileName,
            DynamicScopes = false,
            CriticalBudgetBytes = 1,           // enter critical-only almost immediately
            MaxFileSizeBytes = 64L * 1024 * 1024, // never hard-stop in this test
        });

        // Budget lags one frame (sampled from the flushed position): frame 1 flushes the
        // header, frame 2 then sees the bytes and enters critical-only.
        driver.Frame(16.0);
        driver.Frame(16.0);

        const int rounds = 200;
        for (var i = 0; i < rounds; i++)
        {
            driver.Scope(CriticalMarker, TimeHelper.MillisecondsToTimestamp(FrameMs));
            driver.Scope(NormalMarker, TimeHelper.MillisecondsToTimestamp(FrameMs));
            driver.Frame(16.0);
        }
        driver.Stop();

        var begins = SliceBeginCount(ParseTrace(fileName));
        // Exactly the critical scopes should remain; the normal ones are dropped.
        Assert.AreEqual(rounds, begins,
            $"only critical scopes should survive critical-only mode (got {begins})");
        File.Delete(fileName);
    }

    [Test]
    public static void SoftBudget_DropsCriticalChildOfDroppedParent()
    {
        // A thread track is a stack: a critical scope nested inside a dropped
        // non-critical scope must be dropped too, otherwise it would mis-nest.
        var fileName = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var driver = new Driver(new PerfettoProcessorSettings
        {
            FileName = fileName,
            DynamicScopes = false,
            CriticalBudgetBytes = 1,
            MaxFileSizeBytes = 64L * 1024 * 1024,
        });

        driver.Frame(16.0);
        driver.Frame(16.0); // critical-only on

        for (var i = 0; i < 100; i++)
        {
            driver.Begin(NormalMarker);   // non-critical -> dropped
            driver.Begin(CriticalMarker); // critical, but inside a dropped parent
            driver.End(CriticalMarker);
            driver.End(NormalMarker);
            driver.Frame(16.0);
        }
        driver.Stop();

        var trace = ParseTrace(fileName);
        Assert.AreEqual(0, SliceBeginCount(trace),
            "a critical child of a dropped parent must also be dropped");
        Assert.AreEqual(0, SliceEndCount(trace));
        File.Delete(fileName);
    }

    [Test]
    public static void SoftBudget_CriticalParentKeepsBalanceDroppingNonCriticalChild()
    {
        // Critical parent emits; its non-critical child is dropped; begins and ends
        // stay balanced (no dangling or mis-nested slices).
        var fileName = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var driver = new Driver(new PerfettoProcessorSettings
        {
            FileName = fileName,
            DynamicScopes = false,
            CriticalBudgetBytes = 1,
            MaxFileSizeBytes = 64L * 1024 * 1024,
        });

        driver.Frame(16.0);
        driver.Frame(16.0); // critical-only on

        const int rounds = 100;
        for (var i = 0; i < rounds; i++)
        {
            driver.Begin(CriticalMarker); // critical -> emitted
            driver.Begin(NormalMarker);   // non-critical child -> dropped
            driver.End(NormalMarker);
            driver.End(CriticalMarker);
            driver.Frame(16.0);
        }
        driver.Stop();

        var trace = ParseTrace(fileName);
        Assert.AreEqual(rounds, SliceBeginCount(trace), "only the critical parents are emitted");
        Assert.AreEqual(rounds, SliceEndCount(trace), "begins and ends stay balanced");
        File.Delete(fileName);
    }

    [Test]
    public static void DynamicScopes_UnbalancedAndCrossFrameScopes_StayBalanced()
    {
        // The per-frame buffer is reset each frame, so scopes can span frame boundaries
        // (begin in one frame, end in the next) or arrive as a stray end. Replay + the
        // post-replay CloseScopes must keep the emitted slices balanced regardless.
        var fileName = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var driver = new Driver(new PerfettoProcessorSettings
        {
            FileName = fileName,
            DynamicScopes = true,
            StartupFramesToWrite = 50, // force every frame to replay in this test
        });

        driver.Begin(NormalMarker); // opens, but the end lands in the next frame's buffer
        driver.Frame(16.0);
        driver.End(NormalMarker);   // real end of the cross-frame scope
        driver.Frame(16.0);
        driver.End(NormalMarker);   // stray end with no matching begin
        driver.Frame(16.0);
        driver.Stop();

        var trace = ParseTrace(fileName);
        Assert.AreEqual(SliceBeginCount(trace), SliceEndCount(trace),
            "begins and ends must balance even with unbalanced / cross-frame input");
        File.Delete(fileName);
    }

    [Test]
    public static void Metadata_WrittenAsTraceAttributes()
    {
        var fileName = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var metadata = new Dictionary<string, string>
        {
            ["device_tier"] = "high",
            ["gpu"] = "Adreno 740",
            ["client_version"] = "0.59.1005",
        };
        var driver = new Driver(new PerfettoProcessorSettings
        {
            FileName = fileName,
            Metadata = metadata,
        });
        driver.Frame(16.0);
        driver.Stop();

        var trace = ParseTrace(fileName);
        var attrsPacket = trace.Packet.FirstOrDefault(p => p.TraceAttributes != null);
        Assert.NotNull(attrsPacket, "a trace_attributes packet must be present");

        var written = attrsPacket!.TraceAttributes.Attribute
            .ToDictionary(a => a.Key, a => a.StringValue);
        Assert.AreEqual(metadata.Count, written.Count);
        foreach (var kv in metadata)
            Assert.AreEqual(kv.Value, written[kv.Key], $"attribute '{kv.Key}' round-trips");
        File.Delete(fileName);
    }
}
