using System;
using System.Threading;
using NanoProfiler;
using NUnit.Framework;
using Meowtron.NanoProfiler.Tests.Helpers;

namespace Meowtron.NanoProfiler.Tests
{
    public class NanoCounterTests
    {
        private static string MakeUniqueCounterName() => $"TestCounter-{DateTime.Now:O}";

        [Test]
        public static void NanoCounter_CreatedBeforeProfilerEnabled_InfoReceived()
        {
            var testProcessor = new MarkerTestProcessor();
            var settings = new ProfilerSettingsBuilder()
                .AddProcessor(_ => testProcessor)
                .Build();
            
            var profiler = new Profiler(settings);

            var name = MakeUniqueCounterName();
            _ = profiler.CreateCounter(name);
            
            profiler.Start();
            profiler.Stop();
            profiler.Dispose();
            
            Assert.AreEqual(1, testProcessor.Markers.Count);
            Assert.AreEqual(name, testProcessor.Markers[0].Name);
        }
        
        [Test]
        public static void NanoCounter_CreatedAfterProfilerEnabled_InfoReceived()
        {
            var testProcessor = new MarkerTestProcessor();
            var settings = new ProfilerSettingsBuilder()
                .AddProcessor(_ => testProcessor)
                .Build();
            
            var profiler = new Profiler(settings);
            profiler.Start();

            var name = MakeUniqueCounterName();
            _ = profiler.CreateCounter(name);
            
            profiler.Stop();
            profiler.Dispose();
            
            Assert.AreEqual(1, testProcessor.Markers.Count);
            Assert.AreEqual(name, testProcessor.Markers[0].Name);
        }
        
        [Test]
        public static void Profiler_Counter_SampleSent()
        {
            const int eventsCount = 10;

            var testProcessor = new CountMessagesTestProcessor();
            var settings = new ProfilerSettingsBuilder()
                .AddProcessor(_ => testProcessor)
                .Build();
            
            var profiler = new Profiler(settings);

            var counter = profiler.CreateCounter(MakeUniqueCounterName());
            
            profiler.Start();
            for (int i = 0; i < eventsCount; i++)
            {
                counter.Sample(i);
                // Sleep to let collector threads do the job, otherwise reporting
                // threads do the full loop before switching to the collector
                Thread.Sleep(1);
            }
            profiler.Stop();
            
            profiler.Dispose();
            
            Assert.AreEqual(1, testProcessor.CaptureStart);
            Assert.AreEqual(1, testProcessor.CaptureStop);
            Assert.AreEqual(1, testProcessor.MarkerInfoCount);
            Assert.AreEqual(eventsCount, testProcessor.CounterSamples);
        }
    }
}