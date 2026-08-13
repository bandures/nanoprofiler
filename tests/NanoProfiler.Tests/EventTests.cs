using System;
using System.Threading;
using NanoProfiler.Tests.Helpers;
using NUnit.Framework;

namespace NanoProfiler.Tests
{
    public static class EventTests
    {
        private static string MakeUniqueEventName() => $"TestEvent-{DateTime.Now:O}";

        [Test]
        public static void Event_CreatedBeforeProfilerEnabled_InfoReceived()
        {
            var testProcessor = new MarkerTestProcessor();
            var settings = new ProfilerSettingsBuilder()
                .AddProcessor(_ => testProcessor)
                .Build();
            using var profiler = new Profiler(settings);

            var name = MakeUniqueEventName();
            _ = profiler.CreateEvent(name);

            profiler.Start();
            profiler.Stop();

            Assert.AreEqual(1, testProcessor.Markers.Count);
            Assert.AreEqual(name, testProcessor.Markers[0].Name);
        }

        [Test]
        public static void Event_CreatedAfterProfilerEnabled_InfoReceived()
        {
            var testProcessor = new MarkerTestProcessor();
            var settings = new ProfilerSettingsBuilder()
                .AddProcessor(_ => testProcessor)
                .Build();
            using var profiler = new Profiler(settings);

            profiler.Start();

            var name = MakeUniqueEventName();
            _ = profiler.CreateEvent(name);

            profiler.Stop();

            Assert.AreEqual(1, testProcessor.Markers.Count);
            Assert.AreEqual(name, testProcessor.Markers[0].Name);
        }

        [Test]
        public static void Event_Recorded_AllRecordsReceived()
        {
            const int eventsCount = 10;

            var testProcessor = new CountMessagesTestProcessor();
            var settings = new ProfilerSettingsBuilder()
                .AddProcessor(_ => testProcessor)
                .Build();
            using var profiler = new Profiler(settings);

            var evt = profiler.CreateEvent(MakeUniqueEventName());

            profiler.Start();
            for (var i = 0; i < eventsCount; i++)
            {
                evt.Record();
                // Sleep to let collector threads do the job, otherwise reporting
                // threads do the full loop before switching to the collector
                Thread.Sleep(1);
            }
            profiler.Stop();

            Assert.AreEqual(1, testProcessor.MarkerInfoCount);
            Assert.AreEqual(eventsCount, testProcessor.EventCount);
        }

        [Test]
        public static void Event_RecordedWithParameters_ParametersReceived()
        {
            var testProcessor = new EventTestProcessor();
            var settings = new ProfilerSettingsBuilder()
                .AddProcessor(_ => testProcessor)
                .Build();
            using var profiler = new Profiler(settings);

            var evt = profiler.CreateEvent(MakeUniqueEventName());

            profiler.Start();
            evt.Record(1234, "StringParam", 1.5f);
            profiler.Stop();

            Assert.AreEqual(1, testProcessor.EventRecords.Count);

            var record = testProcessor.EventRecords[0];
            Assert.AreNotEqual(0, record.Timestamp);
            Assert.AreEqual(TypedAttribute.ValueType.Int, record.param1.ParamType);
            Assert.AreEqual(1234, record.param1.AsInt);
            Assert.AreEqual(TypedAttribute.ValueType.String, record.param2.ParamType);
            Assert.AreEqual("StringParam", record.param2.AsString);
            Assert.AreEqual(TypedAttribute.ValueType.Float, record.param3.ParamType);
            Assert.AreEqual(1.5f, record.param3.AsFloat);
        }

        [Test]
        public static void Event_InEnabledGroup_RecordReceived()
        {
            const string groupName = "EnabledEventGroup";

            var testProcessor = new CountMessagesTestProcessor();
            var settings = new ProfilerSettingsBuilder()
                .AddProcessor(_ => testProcessor)
                .EnableGroups(groupName)
                .Build();
            using var profiler = new Profiler(settings);

            var evt = profiler.CreateGroup(groupName).CreateEvent(MakeUniqueEventName());

            profiler.Start();
            evt.Record();
            profiler.Stop();

            Assert.AreEqual(1, testProcessor.EventCount);
        }

        [Test]
        public static void Event_InDisabledGroup_RecordDropped()
        {
            var testProcessor = new CountMessagesTestProcessor();
            var settings = new ProfilerSettingsBuilder()
                .AddProcessor(_ => testProcessor)
                .Build();
            using var profiler = new Profiler(settings);

            var evt = profiler.CreateGroup("DisabledEventGroup").CreateEvent(MakeUniqueEventName());

            profiler.Start();
            evt.Record();
            profiler.Stop();

            Assert.AreEqual(0, testProcessor.EventCount);
        }

        [Test]
        public static void Event_RecordedWhileProfilerStopped_RecordDropped()
        {
            var testProcessor = new CountMessagesTestProcessor();
            var settings = new ProfilerSettingsBuilder()
                .AddProcessor(_ => testProcessor)
                .Build();
            using var profiler = new Profiler(settings);

            var evt = profiler.CreateEvent(MakeUniqueEventName());

            evt.Record();

            profiler.Start();
            profiler.Stop();

            Assert.AreEqual(0, testProcessor.EventCount);
        }
    }
}
