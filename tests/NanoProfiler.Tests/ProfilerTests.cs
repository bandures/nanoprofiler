using NanoProfiler;
using System.Linq;
using NUnit.Framework;

namespace NanoProfiler.Tests
{
    public static class ProfilerTests
    {
        [Test]
        // Verify that profiler can handle multiple start/stop without issues
        public static void Profiler_CreateAndDestroy_NoExceptions()
        {
            var settings = new ProfilerSettingsBuilder().Build();
            using var profiler = new Profiler(settings);
        }
        
        [Test]
        // Verify that profiler can handle multiple start/stop without issues
        public static void Profiler_StartMultipleTimes_NoExceptions()
        {
            const int iterationsCount = 10;
            
            var settings = new ProfilerSettingsBuilder().Build();
            using var profiler = new Profiler(settings);

            for (int i = 0; i < iterationsCount; i++)
            {
                profiler.Start();
                profiler.Stop();
            }
        }

        private class DummyProcessor : IProcessorBase
        {
            public void Dispose() { }
        }
        
        [Test]
        public static void Profiler_WithProcessor_ProcessorCreated()
        {
            var settings = new ProfilerSettingsBuilder()
                .AddProcessor(_ => new DummyProcessor())
                .Build();
            using var profiler = new Profiler(settings);

            Assert.IsNotNull(profiler.GetProcessor<DummyProcessor>());
        }

        private class DummyService : IProfilerService
        {
            public int StartCount;
            public int StopCount;

            public void Start() => StartCount++;
            public void Stop() => StopCount++;
        }
        
        [Test]
        public static void Profiler_WithService_ServiceStartsOnProfilerStart()
        {
            var service = new DummyService();
            var settings = new ProfilerSettingsBuilder()
                .AddService(service)
                .Build();
            using var profiler = new Profiler(settings);
            
            profiler.Start();
            profiler.Stop();

            Assert.AreEqual(1, service.StartCount);
            Assert.AreEqual(1, service.StopCount);
        }
        
        [Test]
        public static void Profiler_WithInheritance_RetainGroupsAndMarkers()
        {
            using var originalProfiler = new Profiler(new ProfilerSettings());
            var groupName = "g1";
            var group = new Group(originalProfiler, groupName, MarkerFlags.Default);
            var scopeName = "s1";
            _ = new Scope(originalProfiler, scopeName, Profiler.INVALID_ID, MarkerFlags.Default);
            var scopeInGroupName = "g1s1";
            _ = group.CreateScope(scopeInGroupName);
            var activityNameInGroup = "a1";
            _ = group.CreateActivity(activityNameInGroup);
            
            var settings = new ProfilerSettingsBuilder()
                .Build();
            using var profiler = new Profiler(settings, originalProfiler);

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
        public static void Profiler_WithSettingsLimit_OverflowInstruments()
        {
            const int capacity = 2; 
            
            var settings = new ProfilerSettingsBuilder()
                .WithGroupsCapacity(2)
                .WithMarkersCapacity(2)
                .Build();
            using var profiler = new Profiler(settings);

            for (int i = 0; i < capacity * 2; i++)
                profiler.CreateGroup($"group-{i}");
            
            for (int i = 0; i < capacity * 2; i++)
                profiler.CreateScope($"scope-{i}");
        }

        [Test]
        public static void Recreate_WithSmallerConfiguredCapacity_KeepsExistingInstrumentsUsable()
        {
            using var previous = new Profiler(new ProfilerSettingsBuilder().WithGroupsCapacity(64).Build());

            // Fill past the default capacity so the recreated profiler would have to drop groups.
            for (var i = 0; i < new ProfilerSettings().MaxGroups + 4; i++)
                previous.CreateGroup($"group_{i}");

            var group = previous.CreateGroup("late_group");
            var scope = group.CreateScope("scope");

            using var recreated = new Profiler(new ProfilerSettingsBuilder().EnableGroups("late_group").Build(), previous);

            Assert.IsTrue(group.IsEnabled, "Group id must still resolve to the same group after the profiler is recreated");
            Assert.DoesNotThrow(() =>
            {
                using var _ = scope.Auto();
            });
        }

    }
}
