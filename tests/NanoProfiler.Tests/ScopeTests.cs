using System;
using System.Threading;
using NUnit.Framework;
using NanoProfiler.Tests.Helpers;

namespace NanoProfiler.Tests
{
    public static class ScopeTests
    {
        private static string MakeUniqueScopeName() => $"TestScope-{DateTime.Now:O}";

        [Test]
        public static void NanoScope_CreatedBeforeProfilerStarted_InfoReceived()
        {
            var testProcessor = new MarkerTestProcessor();
            var settings = new ProfilerSettingsBuilder()
                .AddProcessor(_ => testProcessor)
                .Build();
            
            using var profiler = new Profiler(settings);
            
            var markerName = MakeUniqueScopeName();
            _ = profiler.CreateScope(markerName);
            
            profiler.Start();
            profiler.Stop();
            
            Assert.AreEqual(1, testProcessor.Markers.Count);
            Assert.AreEqual(markerName, testProcessor.Markers[0].Name);
        }
        
        [Test]
        public static void NanoScope_CreatedAfterProfilerStarted_InfoReceived()
        {
            var testProcessor = new MarkerTestProcessor();
            var settings = new ProfilerSettingsBuilder()
                .AddProcessor(_ => testProcessor)
                .Build();
            
            using var profiler = new Profiler(settings);
            
            profiler.Start();
            
            var markerName = MakeUniqueScopeName();
            _ = profiler.CreateScope(markerName);
            
            profiler.Stop();
            
            Assert.AreEqual(1, testProcessor.Markers.Count);
            Assert.AreEqual(markerName, testProcessor.Markers[0].Name);
        }
        
        [Test]
        // Simulate heavy load on a single thread
        // This test depends on thread buffer size and marker message size.
        public static void NanoScope_With1000Events_AllEventsReceived()
        {
            const int markersCount = 10;
            const int eventsCount = 1000;

            var testProcessor = new CountMessagesTestProcessor();
            var settings = new ProfilerSettingsBuilder()
                .AddProcessor(_ => testProcessor)
                .Build();
            
            var profiler = new Profiler(settings);

            var markers = new Scope[markersCount];
            for (int i = 0; i < markersCount; i++)
                markers[i] = profiler.CreateScope($"TestScope-{i}");
            
            profiler.Start();
            for (int i = 0; i < eventsCount; i++)
            {
                var marker = markers[i % markers.Length];
                marker.Begin();
                marker.End();
                
                if (i % 200 == 0)
                    profiler.CurrentThread.Flush();
            }
            profiler.Stop();
            
            profiler.Dispose();
            
            Assert.AreEqual(1, testProcessor.CaptureStart);
            Assert.AreEqual(1, testProcessor.CaptureStop);
            Assert.AreEqual(markersCount, testProcessor.MarkerInfoCount);
            Assert.AreEqual(eventsCount, testProcessor.ScopeBeginCount);
            Assert.AreEqual(eventsCount, testProcessor.ScopeEndCount);
        }
        
        [Test]
        public static void NanoScope_WithMultipleThreads_AllEventsReceived()
        {
            const int threadsCount = 10;
            const int eventsCount = 1000;
            const int eventsPerLoop = 10;

            var testProcessor = new CountMessagesTestProcessor();
            var settings = new ProfilerSettingsBuilder()
                .AddProcessor(_ => testProcessor)
                .Build();
            
            var profiler = new Profiler(settings);

            var threads = new Thread[threadsCount];
            for (int i = 0; i < threadsCount; i++)
            {
                var idx = i;
                threads[i] = new Thread(() => ThreadFunc(profiler, idx, eventsCount / eventsPerLoop, eventsPerLoop));
            }
            
            profiler.Start();
            
            // Kick-off all reporting threads 
            for (int i = 0; i < threadsCount; i++)
                threads[i].Start();

            // Wait threads to finish
            for (int i = 0; i < threadsCount; i++)
                threads[i].Join();
            
            profiler.GetStatistics(out _, out var discardedBytes);
            
            profiler.Stop();
            profiler.Dispose();
            
            Assert.Zero(discardedBytes);
            Assert.AreEqual(1, testProcessor.CaptureStart);
            Assert.AreEqual(1, testProcessor.CaptureStop);
            Assert.AreEqual(threadsCount, testProcessor.MarkerInfoCount);
            Assert.AreEqual(eventsCount * threadsCount, testProcessor.ScopeBeginCount);
            Assert.AreEqual(eventsCount * threadsCount, testProcessor.ScopeEndCount);
            
            return;
            
            static void ThreadFunc(Profiler profiler, int idx, int loopsCount, int eventsPerLoop)
            {
                var marker = profiler.CreateScope($"TestScope-{idx}");
            
                for (int i = 0; i < loopsCount; i++)
                {
                    for (int j = 0; j < eventsPerLoop; j++)
                    {
                        marker.Begin();
                        marker.End();
                    }
                
                    // As we're overflowing pipe, we need to sync to avoid data discard in this test
                    profiler.CurrentThread.Flush();
                }
            }
        }
        
        [Test][Ignore("Unstable test")]
        // Simulate heavy load on a single thread
        public static void Profiler_WithThreadsOverflow_TotalOverflowReported()
        {
            const int threadsCount = 10;
            const int eventsCount = 1000;
            
            var testProcessor = new CountMessagesTestProcessor();
            var settings = new ProfilerSettingsBuilder()
                .AddProcessor(_ => testProcessor)
                .Build();
            
            var profiler = new Profiler(settings);

            var threads = new Thread[threadsCount];
            for (int i = 0; i < threadsCount; i++)
                threads[i] = new Thread(() => ThreadFunc(profiler, eventsCount));
            
            profiler.Start();
            
            // Kick-off all reporting threads 
            for (int i = 0; i < threadsCount; i++)
                threads[i].Start();

            // Wait threads to finish
            for (int i = 0; i < threadsCount; i++)
                threads[i].Join();

            profiler.GetStatistics(out long transferredBytes, out long discardedBytes);
            Assert.NotZero(discardedBytes);
            Assert.AreEqual(260983, transferredBytes + discardedBytes);
            
            profiler.Stop();
            profiler.Dispose();

            return;
            
            static void ThreadFunc(Profiler profiler, int loopsCount)
            {
                var marker = new Scope(profiler, MakeUniqueScopeName(), Profiler.INVALID_ID, MarkerFlags.Default);
            
                for (int i = 0; i < loopsCount; i++)
                {
                    marker.Begin();
                    marker.End();
                }
            }
        }
    }
}