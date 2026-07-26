using NUnit.Framework;

namespace NanoProfiler.Tests;

public static class SpikeDetectorTests
{
    [Test]
    public static void NoBaseline_FallsBackToAbsoluteFloor()
    {
        var detector = new SpikeDetector(windowFrames: 60, absoluteFloorTicks: 50, ratio: 1.5f);

        Assert.IsFalse(detector.IsSpike(50), "equal to floor is not a spike");
        Assert.IsFalse(detector.IsSpike(49));
        Assert.IsTrue(detector.IsSpike(51));
    }

    [Test]
    public static void StableBaseline_ThresholdIsMedianTimesRatio()
    {
        var detector = new SpikeDetector(windowFrames: 60, absoluteFloorTicks: 50, ratio: 1.5f);
        for (var i = 0; i < 60; i++)
            detector.Push(100);

        // median 100 * 1.5 = 150, above the 50 floor.
        Assert.AreEqual(150, detector.ThresholdTicks());
        Assert.IsFalse(detector.IsSpike(140));
        Assert.IsTrue(detector.IsSpike(160));
    }

    [Test]
    public static void SlowDevice_SteadyLongFramesAreNotSpikes()
    {
        // Reproduces the runaway: a device running at a constant ~60ms/frame must
        // NOT flag every frame once the baseline reflects that speed.
        var detector = new SpikeDetector(windowFrames: 60, absoluteFloorTicks: 50, ratio: 1.5f);
        for (var i = 0; i < 60; i++)
            detector.Push(60);

        for (var i = 0; i < 1000; i++)
            Assert.IsFalse(detector.IsSpike(60), "steady slow frame must not be a spike");

        // But a genuine spike relative to the raised baseline (60*1.5=90) still fires.
        Assert.IsTrue(detector.IsSpike(100));
    }

    [Test]
    public static void RelativeThresholdNeverDropsBelowFloor()
    {
        var detector = new SpikeDetector(windowFrames: 30, absoluteFloorTicks: 50, ratio: 1.5f);
        for (var i = 0; i < 30; i++)
            detector.Push(10); // baseline 10 -> relative 15 < floor

        Assert.AreEqual(50, detector.ThresholdTicks());
        Assert.IsFalse(detector.IsSpike(40));
        Assert.IsTrue(detector.IsSpike(51));
    }

    [Test]
    public static void IsSpike_DoesNotMutateBaseline()
    {
        var detector = new SpikeDetector(windowFrames: 60, absoluteFloorTicks: 50, ratio: 1.5f);
        for (var i = 0; i < 60; i++)
            detector.Push(100);

        for (var i = 0; i < 100; i++)
            detector.IsSpike(100_000);

        Assert.AreEqual(150, detector.ThresholdTicks(), "querying spikes must not change the baseline");
    }

    [Test]
    public static void SingleSpikeDoesNotPoisonMedianBaseline()
    {
        var detector = new SpikeDetector(windowFrames: 60, absoluteFloorTicks: 50, ratio: 1.5f);
        for (var i = 0; i < 59; i++)
            detector.Push(100);
        detector.Push(100_000); // one huge outlier in the window

        // Median is robust to the single outlier -> baseline stays ~100.
        Assert.AreEqual(150, detector.ThresholdTicks());
    }

    [Test]
    public static void RingBuffer_EvictsOldSamplesOnWraparound()
    {
        var detector = new SpikeDetector(windowFrames: 4, absoluteFloorTicks: 1, ratio: 1.0f);
        detector.Push(10);
        detector.Push(10);
        detector.Push(10);
        detector.Push(10);
        Assert.AreEqual(10, detector.ThresholdTicks());

        detector.Push(100);
        detector.Push(100);
        detector.Push(100);
        detector.Push(100);
        Assert.AreEqual(100, detector.ThresholdTicks(), "old samples must be evicted");
    }

    [Test]
    public static void ShortWindow_AdaptsIntoAndOutOfSustainedSlowness()
    {
        // The short default window reacts within ~half its length in both directions:
        // it settles into sustained slowness (e.g. loading) and re-arms quickly after,
        // which is what lets it catch the first spike following a loading transition.
        // Adapts INTO sustained slowness within ~half the window.
        var adaptIn = new SpikeDetector(windowFrames: 8, absoluteFloorTicks: 50, ratio: 1.5f);
        for (var i = 0; i < 8; i++)
            adaptIn.Push(30); // baseline 30 -> threshold 50
        Assert.IsTrue(adaptIn.IsSpike(100), "a jump from the 30 baseline is a spike");
        for (var i = 0; i < 4; i++)
            adaptIn.Push(100);
        Assert.IsFalse(adaptIn.IsSpike(100), "sustained slowness stops being flagged within ~half the window");

        // Re-arms OUT of slowness within ~half the window as the slow frames age out.
        var reArm = new SpikeDetector(windowFrames: 8, absoluteFloorTicks: 50, ratio: 1.5f);
        for (var i = 0; i < 8; i++)
            reArm.Push(100); // fully slow window -> threshold 150
        Assert.IsFalse(reArm.IsSpike(100));
        for (var i = 0; i < 5; i++)
            reArm.Push(30);
        Assert.IsTrue(reArm.IsSpike(100), "detector re-arms within ~half the window after returning to normal");
    }

    [Test]
    public static void Reset_ClearsBaseline()
    {
        var detector = new SpikeDetector(windowFrames: 8, absoluteFloorTicks: 50, ratio: 1.5f);
        for (var i = 0; i < 8; i++)
            detector.Push(1000);
        Assert.AreEqual(1500, detector.ThresholdTicks());

        detector.Reset();
        Assert.AreEqual(50, detector.ThresholdTicks(), "after reset only the floor applies");
    }
}
