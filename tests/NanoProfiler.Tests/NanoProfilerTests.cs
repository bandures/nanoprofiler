using System.Linq;
using NUnit.Framework;

namespace Meowtron.NanoProfiler.Tests
{
    public static class NanoProfilerTests
    {
        [Test]
        // Verify that profiler can handle multiple start/stop without issues
        public static void NanoProfiler_CreateAndDestroy_NoExceptions()
        {
            var settings = new NanoProfilerSettingsBuilder().Build();
            using var profiler = new NanoProfiler(settings);
        }
        
        [Test]
        // Verify that profiler can handle multiple start/stop without issues
        public static void NanoProfiler_StartMultipleTimes_NoExceptions()
        {
            const int iterationsCount = 10;
            
            var settings = new NanoProfilerSettingsBuilder().Build();
            using var profiler = new NanoProfiler(settings);

            for (int i = 0; i < iterationsCount; i++)
            {
                profiler.Start();
                profiler.Stop();
            }
        }

        private class DummyProcessor : INanoProcessorBase
        {
            public void Dispose() { }
        }
        
        [Test]
        public static void NanoProfiler_WithProcessor_ProcessorCreated()
        {
            var settings = new NanoProfilerSettingsBuilder()
                .AddProcessor(_ => new DummyProcessor())
                .Build();
            using var profiler = new NanoProfiler(settings);

            Assert.IsNotNull(profiler.GetProcessor<DummyProcessor>());
        }

        private class DummyService : INanoProfilerService
        {
            public int StartCount;
            public int StopCount;

            public void Start() => StartCount++;
            public void Stop() => StopCount++;
        }
        
        [Test]
        public static void NanoProfiler_WithService_ServiceStartsOnProfilerStart()
        {
            var service = new DummyService();
            var settings = new NanoProfilerSettingsBuilder()
                .AddService(service)
                .Build();
            using var profiler = new NanoProfiler(settings);
            
            profiler.Start();
            profiler.Stop();

            Assert.AreEqual(1, service.StartCount);
            Assert.AreEqual(1, service.StopCount);
        }
        
        [Test]
        public static void NanoProfiler_WithInheritance_RetainGroupsAndMarkers()
        {
            using var originalProfiler = new NanoProfiler(new NanoProfilerSettings());
            var groupName = "g1";
            var group = new NanoGroup(originalProfiler, groupName, NanoMarkerFlags.Default);
            var scopeName = "s1";
            _ = new NanoScope(originalProfiler, scopeName, NanoProfiler.INVALID_ID, NanoMarkerFlags.Default);
            var scopeInGroupName = "g1s1";
            _ = group.CreateScope(scopeInGroupName);
            var activityNameInGroup = "a1";
            _ = group.CreateActivity(activityNameInGroup);
            
            var settings = new NanoProfilerSettingsBuilder()
                .Build();
            using var profiler = new NanoProfiler(settings, originalProfiler);

            var groups = profiler.Groups.ToList();
            Assert.AreEqual(2, groups.Count);
            Assert.AreEqual(groupName, groups[1].Name);
            
            var markers = profiler.Markers.ToList();
            Assert.AreEqual(4, markers.Count);
            Assert.AreEqual(scopeName, markers[1].Name);
            Assert.AreEqual(scopeInGroupName, markers[2].Name);
            Assert.AreEqual(activityNameInGroup, markers[3].Name);
        }

        [Test]
        public static void NanoProfiler_WithSettingsLimit_OverflowInstruments()
        {
            const int capacity = 2; 
            
            var settings = new NanoProfilerSettingsBuilder()
                .WithGroupsCapacity(2)
                .WithMarkersCapacity(2)
                .Build();
            using var profiler = new NanoProfiler(settings);

            for (int i = 0; i < capacity * 2; i++)
                profiler.CreateGroup($"group-{i}");
            
            for (int i = 0; i < capacity * 2; i++)
                profiler.CreateScope($"scope-{i}");
        }
    }
}
