using System;
using NUnit.Framework;
using Meowtron.NanoProfiler.Tests.Helpers;

namespace Meowtron.NanoProfiler.Tests
{
    public static class NanoActivityTests
    {
        private static string MakeUniqueActivityName() => $"TestActivity-{DateTime.Now:O}";

        [Test, Timeout(1000)]
        public static void NanoActivity_CreatedBeforeProfilerStarted_InfoReceived()
        {
            var testProcessor = new MarkerTestProcessor();
            var settings = new NanoProfilerSettingsBuilder()
                .AddProcessor(_ => testProcessor)
                .Build();
            
            var profiler = new NanoProfiler(settings);
            
            var name = MakeUniqueActivityName();
            _ = profiler.CreateActivity(name);
            
            profiler.Start();
            profiler.Stop();
            
            profiler.Dispose();
            
            Assert.AreEqual(1, testProcessor.Markers.Count);
            Assert.AreEqual(name, testProcessor.Markers[0].Name);
        }
        
        [Test, Timeout(1000)]
        public static void NanoActivity_CreatedAfterProfilerStarted_InfoReceived()
        {
            var testProcessor = new MarkerTestProcessor();
            var settings = new NanoProfilerSettingsBuilder()
                .AddProcessor(_ => testProcessor)
                .Build();
            
            var profiler = new NanoProfiler(settings);
            
            profiler.Start();
            
            var name = MakeUniqueActivityName();
            _ = profiler.CreateActivity(name);
            
            profiler.Stop();
            
            profiler.Dispose();
            
            Assert.AreEqual(1, testProcessor.Markers.Count);
            Assert.AreEqual(name, testProcessor.Markers[0].Name);
        }
        
        [Test, Timeout(1000)]
        public static void NanoActivity_UsingActivity_MatchingMessagesCount()
        {
            var testProcessor = new CountMessagesTestProcessor();
            var settings = new NanoProfilerSettingsBuilder()
                .AddProcessor(_ => testProcessor)
                .Build();
            using var profiler = new NanoProfiler(settings);
            
            var name = MakeUniqueActivityName();
            var activity = profiler.CreateActivity(name);
        
            profiler.Start();
            using (var _ = activity.Auto())
            {
            }
            profiler.Stop();
            
            // Validate that result has the marker
            Assert.AreEqual(1, testProcessor.ActivityBeginCount);
            Assert.AreEqual(1, testProcessor.ActivityEndCount);
        }
        
        [Test, Timeout(1000)]
        public static void NanoActivity_WithSingleActivation_MatchingUniqueId()
        {
            var testProcessor = new ActivityTestProcessor();
            var settings = new NanoProfilerSettingsBuilder()
                .AddProcessor(_ => testProcessor)
                .Build();
            using var profiler = new NanoProfiler(settings);
                
            var name = MakeUniqueActivityName();
            var activity = profiler.CreateActivity(name);
        
            profiler.Start();
            using (var _ = activity.Auto())
            {
            }
            profiler.Stop();
            
            // Validate that activity marker id and unique id are the same
            Assert.AreEqual(2, testProcessor.ActivityRecords.Count);
            Assert.AreEqual(testProcessor.ActivityRecords[0].MarkerId, testProcessor.ActivityRecords[1].MarkerId);
            Assert.AreEqual(testProcessor.ActivityRecords[0].UniqueId, testProcessor.ActivityRecords[1].UniqueId);
        }
        
        [Test, Timeout(1000)]
        public static void NanoActivity_WithMultipleOverlappingActivations_MatchingUniqueId()
        {
            var testProcessor = new ActivityTestProcessor();
            var settings = new NanoProfilerSettingsBuilder()
                .AddProcessor(_ => testProcessor)
                .Build();
            using var profiler = new NanoProfiler(settings);
            
            var name = MakeUniqueActivityName();
            var activity = profiler.CreateActivity(name);
        
            profiler.Start();
            var a1 = activity.Auto();
            var a2 = activity.Auto();
            var a3 = activity.Auto();
            a1.Dispose();
            a3.Dispose();
            a2.Dispose();
            
            profiler.Stop();
                
            // Validate that activities marker id and unique id are the same and in the same begin/end order 
            Assert.AreEqual(6, testProcessor.ActivityRecords.Count);
            Assert.AreEqual(testProcessor.ActivityRecords[0].MarkerId, testProcessor.ActivityRecords[3].MarkerId);
            Assert.AreEqual(testProcessor.ActivityRecords[0].UniqueId, testProcessor.ActivityRecords[3].UniqueId);
            Assert.AreEqual(testProcessor.ActivityRecords[1].MarkerId, testProcessor.ActivityRecords[5].MarkerId);
            Assert.AreEqual(testProcessor.ActivityRecords[1].UniqueId, testProcessor.ActivityRecords[5].UniqueId);
            Assert.AreEqual(testProcessor.ActivityRecords[2].MarkerId, testProcessor.ActivityRecords[4].MarkerId);
            Assert.AreEqual(testProcessor.ActivityRecords[2].UniqueId, testProcessor.ActivityRecords[4].UniqueId);
        }
        
        [Test, Timeout(1000)]
        [TestCase(true)]
        [TestCase(false)]
        [TestCase(12)]
        [TestCase(12.0f)]
        [TestCase("Test")]
        public static void NanoActivity_WithParams_SameValueReceived(object value)
        {
            var testProcessor = new ActivityTestProcessor();
            var settings = new NanoProfilerSettingsBuilder()
                .AddProcessor(_ => testProcessor)
                .Build();
            using var profiler = new NanoProfiler(settings);
            
            var name = MakeUniqueActivityName();
            var activity = profiler.CreateActivity(name);
        
            profiler.Start();
            {
                var arg = MakeAttribute(value);
                using var _ = activity.Auto(arg, arg, arg);
            }
            profiler.Stop();

            var valueAsString = value.ToString();
            Assert.AreEqual(2, testProcessor.ActivityRecords.Count);
            Assert.AreEqual(testProcessor.ActivityRecords[0].MarkerId, testProcessor.ActivityRecords[1].MarkerId);
            Assert.AreEqual(testProcessor.ActivityRecords[0].UniqueId, testProcessor.ActivityRecords[1].UniqueId);
            Assert.AreEqual(valueAsString, testProcessor.ActivityRecords[0].param1.ToString());
            Assert.AreEqual(valueAsString, testProcessor.ActivityRecords[0].param2.ToString());
            Assert.AreEqual(valueAsString, testProcessor.ActivityRecords[0].param3.ToString());
            
            return;

            TypedAttribute MakeAttribute(object src)
            {
                switch (src)
                {
                    case bool boolValue:
                        return boolValue;
                    case int intValue:
                        return intValue;
                    case float floatValue:
                        return floatValue;
                    case string stringValue:
                        return stringValue;
                    default:
                        throw new ArgumentException($"Unexpected argument type {src.GetType().Name}");
                }
            }
        }
    }
}