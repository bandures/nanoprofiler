using System;
using System.Linq;
using NanoProfiler;
using NUnit.Framework;
using Meowtron.NanoProfiler.Tests.Helpers;

namespace Meowtron.NanoProfiler.Tests
{
    public static class NanoGroupTests
    {
        private static string MakeUniqueGroupName() => $"TestGroup-{DateTime.Now:O}";

        [Test]
        public static void NanoGroup_CreatedBeforeProfilerEnabled_InfoReceived()
        {
            var testProcessor = new GroupTestProcessor();
            var settings = new ProfilerSettingsBuilder()
                .AddProcessor(_ => testProcessor)
                .Build();
            using var profiler = new Profiler(settings);
            
            var name = MakeUniqueGroupName();
            _ = profiler.CreateGroup(name);
            
            profiler.Start();
            profiler.Stop();
            
            Assert.AreEqual(1, testProcessor.Groups.Count);
            Assert.AreEqual(name, testProcessor.Groups[0].Name);
        }
        
        [Test]
        public static void NanoGroup_CreatedAfterProfilerEnabled_InfoReceived()
        {
            var testProcessor = new GroupTestProcessor();
            var settings = new ProfilerSettingsBuilder()
                .AddProcessor(_ => testProcessor)
                .Build();
            using var profiler = new Profiler(settings);
            
            profiler.Start();
            
            var name = MakeUniqueGroupName();
            _ = profiler.CreateGroup(name);
            
            profiler.Stop();
            
            Assert.AreEqual(1, testProcessor.Groups.Count);
            Assert.AreEqual(name, testProcessor.Groups[0].Name);
        }
        
        [Test]
        public static void NanoGroup_WithDefaultGroup_AlwaysEnabled()
        {
            var testProcessor = new CountMessagesTestProcessor();
            var settings = new ProfilerSettingsBuilder()
                .AddProcessor(_ => testProcessor)
                .Build();
            using var profiler = new Profiler(settings);
            
            var counter = profiler.CreateCounter("counter");
            var scope = profiler.CreateScope("scope");
            var activity = profiler.CreateActivity("activity");
            
            profiler.Start();
            {
                counter.Sample(1);
                using var _1 = scope.Auto();
                using var _2 = activity.Auto();
            }
            profiler.Stop();
            
            Assert.AreEqual(0, testProcessor.GroupInfoCount);
            Assert.AreEqual(3, testProcessor.MarkerInfoCount);
            Assert.AreEqual(1, testProcessor.CaptureStart);
            Assert.AreEqual(1, testProcessor.CaptureStop);
            Assert.AreEqual(1, testProcessor.ScopeBeginCount);
            Assert.AreEqual(1, testProcessor.ScopeEndCount);
            Assert.AreEqual(1, testProcessor.CounterSamples);
            Assert.AreEqual(1, testProcessor.ActivityBeginCount);
            Assert.AreEqual(1, testProcessor.ActivityEndCount);
        }
        
        [Test]
        public static void NanoGroup_WithDisabledGroup_NoSamplesSent()
        {
            var testProcessor = new CountMessagesTestProcessor();
            var settings = new ProfilerSettingsBuilder()
                .AddProcessor(_ => testProcessor)
                .Build();
            using var profiler = new Profiler(settings);
            
            var name = MakeUniqueGroupName();
            var group = profiler.CreateGroup(name);

            var counter = group.CreateCounter("counter");
            var scope = group.CreateScope("scope");
            var activity = group.CreateActivity("activity");
            
            profiler.Start();
            {
                counter.Sample(1);
                using var _1 = scope.Auto();
                using var _2 = activity.Auto();
            }
            profiler.Stop();
            
            Assert.AreEqual(1, testProcessor.GroupInfoCount);
            Assert.AreEqual(3, testProcessor.MarkerInfoCount);
            Assert.AreEqual(1, testProcessor.CaptureStart);
            Assert.AreEqual(1, testProcessor.CaptureStop);
            Assert.AreEqual(0, testProcessor.ScopeBeginCount);
            Assert.AreEqual(0, testProcessor.ScopeEndCount);
            Assert.AreEqual(0, testProcessor.CounterSamples);
            Assert.AreEqual(0, testProcessor.ActivityBeginCount);
            Assert.AreEqual(0, testProcessor.ActivityEndCount);
        }
        
        [Test]
        public static void NanoGroup_WithEnabledGroup_SamplesSent()
        {
            var groupName = MakeUniqueGroupName();
            
            var testProcessor = new CountMessagesTestProcessor();
            var settings = new ProfilerSettingsBuilder()
                .AddProcessor(_ => testProcessor)
                .EnableGroups(groupName)
                .Build();
            using var profiler = new Profiler(settings);
            
            var group = profiler.CreateGroup(groupName);
            var counter = group.CreateCounter("counter");
            var scope = group.CreateScope("scope");
            var activity = group.CreateActivity("activity");
            
            profiler.Start();
            {
                counter.Sample(1);
                using var _1 = scope.Auto();
                using var _2 = activity.Auto();
                
            }
            profiler.Stop();
            
            Assert.AreEqual(1, testProcessor.GroupInfoCount);
            Assert.AreEqual(3, testProcessor.MarkerInfoCount);
            Assert.AreEqual(1, testProcessor.CaptureStart);
            Assert.AreEqual(1, testProcessor.CaptureStop);
            Assert.AreEqual(1, testProcessor.ScopeBeginCount);
            Assert.AreEqual(1, testProcessor.ScopeEndCount);
            Assert.AreEqual(1, testProcessor.CounterSamples);
            Assert.AreEqual(1, testProcessor.ActivityBeginCount);
            Assert.AreEqual(1, testProcessor.ActivityEndCount);
        }
        
        [Test]
        public static void NanoGroup_WithFlags_ScopeGetFlags()
        {
            var testProcessor = new MarkerTestProcessor();
            var settings = new ProfilerSettingsBuilder()
                .AddProcessor(_ => testProcessor)
                .Build();
            using var profiler = new Profiler(settings);
            
            var defaultGroup = profiler.CreateGroup("default");
            var globalGroup = profiler.CreateGroup("global", MarkerFlags.ColorRed);
            _ = defaultGroup.CreateScope("default");
            _ = globalGroup.CreateScope("global");
            
            profiler.Start();
            profiler.Stop();
            
            Assert.AreEqual(2, testProcessor.Markers.Count);
            Assert.AreEqual(MarkerFlags.Default, testProcessor.Markers[0].Flags);
            Assert.AreEqual(MarkerFlags.ColorRed, testProcessor.Markers[1].Flags);
        }
        
        [Test]
        public static void NanoGroup_WithMultipleProfilers_DontInterfereWithEachOther()
        {
            var run1 = MakeProfilerRun("run1");
            var run2 = MakeProfilerRun("run2");
            
            run1.profiler.Start();
            run2.profiler.Start();
            run1.profiler.Stop();
            run2.profiler.Stop();
            
            run1.profiler.Dispose();
            run2.profiler.Dispose();
            
            Assert.AreEqual(1, run1.processor.Groups.Count);
            Assert.AreEqual("run1", run1.processor.Groups[0].Name);
            Assert.AreEqual(1, run2.processor.Groups.Count);
            Assert.AreEqual("run2", run2.processor.Groups[0].Name);

            (Profiler profiler, GroupTestProcessor processor) MakeProfilerRun(string name)
            {
                var testProcessor = new GroupTestProcessor();
                var settings = new ProfilerSettingsBuilder()
                    .AddProcessor(_ => testProcessor)
                    .Build();
            
                var profiler = new Profiler(settings);
            
                _ = profiler.CreateGroup(name);

                return (profiler, testProcessor);
            }
        }

        [Test]
        public static void NanoGroup_WithEnabledGroups_MarkedAsPreCreated()
        {
            var settings = new ProfilerSettingsBuilder()
                .EnableGroups("default", "global")
                .Build();
            using var profiler = new Profiler(settings);

            var groups = profiler.Groups.ToList();
            Assert.AreEqual(3, groups.Count);
            Assert.AreEqual(MarkerFlags.Default, groups[0].Flags); // default group
            Assert.AreEqual(MarkerFlags.PreCreated, groups[1].Flags);
            Assert.AreEqual(MarkerFlags.PreCreated, groups[2].Flags);
        }

        [Test]
        public static void NanoGroup_WithPreCreated_FlagsUpdatedOnCreation()
        {
            var settings = new ProfilerSettingsBuilder()
                .EnableGroups("g1", "g2")
                .Build();
            using var profiler = new Profiler(settings);
            
            _ = profiler.CreateGroup("g1");
            _ = profiler.CreateGroup("g2", MarkerFlags.ColorRed);
            
            var groups = profiler.Groups.ToList();
            Assert.AreEqual(3, groups.Count);
            Assert.AreEqual(MarkerFlags.Default, groups[0].Flags); // default group
            Assert.AreEqual(MarkerFlags.Default, groups[1].Flags);
            Assert.AreEqual(MarkerFlags.ColorRed, groups[2].Flags);
        }

        [Test]
        public static void NanoGroup_WithPreCreated_NotSentToProcessor()
        {
            var testProcessor = new GroupTestProcessor();
            var settings = new ProfilerSettingsBuilder()
                .AddProcessor(_ => testProcessor)
                .EnableGroups("g1", "g2")
                .Build();
            using var profiler = new Profiler(settings);
            
            profiler.Start();
            profiler.Stop();

            var groups = profiler.Groups.ToList();
            Assert.AreEqual(0, testProcessor.Groups.Count);
            Assert.AreEqual(3, groups.Count);
        }

        [Test]
        public static void NanoGroup_WithPreCreated_SentOnCreateOnce()
        {
            var testProcessor = new GroupTestProcessor();
            var settings = new ProfilerSettingsBuilder()
                .AddProcessor(_ => testProcessor)
                .EnableGroups("g1", "g2")
                .Build();
            using var profiler = new Profiler(settings);
            
            profiler.Start();

            for (int i = 0; i < 5; i++)
            {
                _ = profiler.CreateGroup("g2");
            }
            
            profiler.Stop();

            var groups = profiler.Groups.ToList();
            Assert.AreEqual(1, testProcessor.Groups.Count);
            Assert.AreEqual(3, groups.Count);
        }
    }
}