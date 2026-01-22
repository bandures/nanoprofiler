using System.Linq;
using System.Threading;
using NUnit.Framework;
using NanoProfiler.Tests.Helpers;

namespace NanoProfiler.Tests
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
            });

            var t2 = new Thread(() =>
            {
                thread2Id = Thread.CurrentThread.ManagedThreadId;
                using var _ = profiler.CreateScope("Scope2").Auto();
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
            });

            var t2 = new Thread(() =>
            {
                thread2Id = Thread.CurrentThread.ManagedThreadId;
                var activity = profiler.CreateActivity("Activity2");
                using (activity.Auto()) { }
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
            
            var threadId = 0;

            // Act
            var t = new Thread(() =>
            {
                threadId = Thread.CurrentThread.ManagedThreadId;
                using (profiler.CreateScope("ScopeA").Auto()) { }
                using (profiler.CreateScope("ScopeB").Auto()) { }
            });
            t.Start();
            t.Join();
            profiler.Stop();

            // Assert
            var records = testProcessor.Records.ToList();
            Assert.AreEqual(4, records.Count);
            Assert.IsTrue(records.All(r => r.ThreadId == threadId));
        }

        // Edge case: Thread finishes execution and is destroyed.
        // Profiler should still have collected the data because we flushed or it was picked up.
        [Test]
        public void Thread_WhenDisposed_ProfilerStillReceivesData()
        {
            var testProcessor = new ThreadActivityTestProcessor();
            var settings = new ProfilerSettingsBuilder()
                .AddProcessor(_ => testProcessor)
                .Build();
            
            using var profiler = new Profiler(settings);
            profiler.Start();
            
            var threadId = 0;

            var t = new Thread(() =>
            {
                threadId = Thread.CurrentThread.ManagedThreadId;
                using (profiler.CreateScope("ScopeQuick").Auto()) { }
            });
            
            t.Start();
            t.Join();

            profiler.Stop();

            var records = testProcessor.Records.Where(r => r.ThreadId == threadId).ToList();
            Assert.AreEqual(2, records.Count);
        }
    }
}
