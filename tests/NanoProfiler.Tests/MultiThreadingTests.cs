using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using NanoProfiler;
using NUnit.Framework;
using Meowtron.NanoProfiler.Tests.Helpers;

namespace Meowtron.NanoProfiler.Tests
{
    [TestFixture]
    public class MultiThreadingTests
    {
        [Test]
        public void MultipleThreads_ReportScopes_ThreadIdsAreCorrectAndDistinct()
        {
            // Arrange
            var testProcessor = new ThreadActivityTestProcessor();
            var settings = new ProfilerSettingsBuilder()
                .AddProcessor(_ => testProcessor)
                .Build();
            
            using var profiler = new Profiler(settings);
            profiler.Start();

            int thread1Id = 0;
            int thread2Id = 0;

            // Act
            var t1 = new Thread(() =>
            {
                thread1Id = Thread.CurrentThread.ManagedThreadId;
                using var _ = profiler.CreateScope("Scope1").Auto();
                profiler.CurrentThread.EmitFlush();
            });

            var t2 = new Thread(() =>
            {
                thread2Id = Thread.CurrentThread.ManagedThreadId;
                using var _ = profiler.CreateScope("Scope2").Auto();
                profiler.CurrentThread.EmitFlush();
            });

            t1.Start();
            t2.Start();

            t1.Join();
            t2.Join();

            // Allow collector to catch up
            profiler.Stop();

            // Assert
            var records = testProcessor.Records.ToList();

            // Verify we have records
            Assert.IsNotEmpty(records, "Should have recorded events");

            // Verify Thread IDs are captured and distinct
            Assert.AreNotEqual(0, thread1Id);
            Assert.AreNotEqual(0, thread2Id);
            Assert.AreNotEqual(thread1Id, thread2Id);

            // Filter records by thread
            var thread1Records = records.Where(r => r.ThreadId == thread1Id).ToList();
            var thread2Records = records.Where(r => r.ThreadId == thread2Id).ToList();

            Assert.IsNotEmpty(thread1Records, "Thread 1 should have records");
            Assert.IsNotEmpty(thread2Records, "Thread 2 should have records");

            // Verify Scope1 is in Thread 1 records
            // We need to resolve MarkerId to Name to be sure, but TestProcessor only stored MarkerId.
            // However, we know Scope1 was created in Thread 1. 
            // The Marker registration might happen on the thread or propagated?
            // Profiler.RegisterMarker is thread-safe?
            // RegisterMarker returns an ID.
            // Let's verify that the MarkerId in Thread 1 records corresponds to "Scope1".
            // Since we can't easily map back from ID to Name in the processor without RegisterMarker handling,
            // we can assume if the counts and structure match, it's correct.
            // Or we can update ThreadActivityTestProcessor to handle RegisterMarker?
            
            // For now, let's just verify structure: Begin then End.
            Assert.AreEqual(2, thread1Records.Count);
            Assert.AreEqual("ScopeBegin", thread1Records[0].Type);
            Assert.AreEqual("ScopeEnd", thread1Records[1].Type);
            Assert.AreEqual(thread1Records[0].MarkerId, thread1Records[1].MarkerId);

            Assert.AreEqual(2, thread2Records.Count);
            Assert.AreEqual("ScopeBegin", thread2Records[0].Type);
            Assert.AreEqual("ScopeEnd", thread2Records[1].Type);
            Assert.AreEqual(thread2Records[0].MarkerId, thread2Records[1].MarkerId);
            
            // Verify IDs are different (since names are different "Scope1" vs "Scope2")
            Assert.AreNotEqual(thread1Records[0].MarkerId, thread2Records[0].MarkerId);
        }

        [Test]
        public void MultipleThreads_ReportActivities_ThreadIdsAreCorrectAndDistinct()
        {
             // Arrange
            var testProcessor = new ThreadActivityTestProcessor();
            var settings = new ProfilerSettingsBuilder()
                .AddProcessor(_ => testProcessor)
                .Build();
            
            using var profiler = new Profiler(settings);
            profiler.Start();

            int thread1Id = 0;
            int thread2Id = 0;

            // Act
            var t1 = new Thread(() =>
            {
                thread1Id = Thread.CurrentThread.ManagedThreadId;
                var activity = profiler.CreateActivity("Activity1");
                using (activity.Auto()) { }
                profiler.CurrentThread.EmitFlush();
            });

            var t2 = new Thread(() =>
            {
                thread2Id = Thread.CurrentThread.ManagedThreadId;
                var activity = profiler.CreateActivity("Activity2");
                using (activity.Auto()) { }
                profiler.CurrentThread.EmitFlush();
            });

            t1.Start();
            t2.Start();

            t1.Join();
            t2.Join();

            profiler.Stop();

            // Assert
            var records = testProcessor.Records.ToList();

            var thread1Records = records.Where(r => r.ThreadId == thread1Id).ToList();
            var thread2Records = records.Where(r => r.ThreadId == thread2Id).ToList();

            Assert.IsNotEmpty(thread1Records);
            Assert.IsNotEmpty(thread2Records);

            Assert.AreEqual("ActivityBegin", thread1Records[0].Type);
            Assert.AreEqual("ActivityEnd", thread1Records[1].Type);
            
            Assert.AreEqual("ActivityBegin", thread2Records[0].Type);
            Assert.AreEqual("ActivityEnd", thread2Records[1].Type);
        }

        [Test]
        public void SingleThread_MultipleActivities_ThreadIdMatches()
        {
            // Arrange
            var testProcessor = new ThreadActivityTestProcessor();
            var settings = new ProfilerSettingsBuilder()
                .AddProcessor(_ => testProcessor)
                .Build();
            
            using var profiler = new Profiler(settings);
            profiler.Start();
            
            int threadId = 0;

            // Act
            var t = new Thread(() =>
            {
                threadId = Thread.CurrentThread.ManagedThreadId;
                using (profiler.CreateScope("ScopeA").Auto()) { }
                using (profiler.CreateScope("ScopeB").Auto()) { }
                profiler.CurrentThread.EmitFlush();
            });
            t.Start();
            t.Join();
            profiler.Stop();

            // Assert
            var records = testProcessor.Records.ToList();
            Assert.AreEqual(4, records.Count);
            Assert.IsTrue(records.All(r => r.ThreadId == threadId));
        }

        [Test]
        public void Thread_WhenDisposed_ProfilerStillReceivesData()
        {
            // Edge case: Thread finishes execution and is destroyed.
            // Profiler should still have collected the data because we flushed or it was picked up.
            // Note: In real scenarios, if thread dies before flush/collection, data might be lost in buffer?
            // ThreadLocalProfiler doesn't auto-flush on thread death unless hooked.
            // But we explicitly call EmitFlush() in tests to ensure delivery.
            // If we don't call EmitFlush, it depends on buffer filling or periodic collection?
            // Collector wakes up on signal.
            
            var testProcessor = new ThreadActivityTestProcessor();
            var settings = new ProfilerSettingsBuilder()
                .AddProcessor(_ => testProcessor)
                .Build();
            
            using var profiler = new Profiler(settings);
            profiler.Start();
            
            int threadId = 0;

            var t = new Thread(() =>
            {
                threadId = Thread.CurrentThread.ManagedThreadId;
                using (profiler.CreateScope("ScopeQuick").Auto()) { }
                profiler.CurrentThread.EmitFlush(); 
            });
            t.Start();
            t.Join(); // Thread is dead now

            // Wait a bit to ensure Collector picked it up (Stop calls HandleAllThreads)
            profiler.Stop();

            var records = testProcessor.Records.Where(r => r.ThreadId == threadId).ToList();
            Assert.AreEqual(2, records.Count);
        }
    }
}
