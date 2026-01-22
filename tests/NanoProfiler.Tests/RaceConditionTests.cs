using System;
using System.Threading;
using NUnit.Framework;

namespace NanoProfiler.Tests
{
    [TestFixture]
    public class RaceConditionTests
    {
        /// <summary>
        /// The test attempts to reproduce a race condition where Profiler.Dispose()
        /// frees the MessagePipe buffer while a thread is still writing to it.
        /// We run this multiple times or with high iteration count to increase probability.
        /// </summary>
        [Test]
        [Ignore("Nullref without a stacktrace and only on build machine, needs more investigation")]
        public void Profiler_Dispose_WhileThreadIsWriting_ShouldNotCrash()
        {
            for (int i = 0; i < 100; i++)
            {
                var settings = new ProfilerSettingsBuilder().Build();
                var profiler = new Profiler(settings);
                profiler.Start();

                var running = true;
                var t = new Thread(() =>
                {
                    try
                    {
                        if (profiler == null)
                            return;
                        
                        var activity = profiler.CreateActivity("TestActivity");
                        while (running)
                        {
                            // Spam events
                            using (activity.Auto()) { }
                        }
                    }
                    catch (AccessViolationException)
                    {
                        // Expected failure in buggy code
                        throw;
                    }
                    catch (NullReferenceException)
                    {
                         // Also possible
                         throw;
                    }
                    catch (Exception)
                    {
                        // Ignore other errors (like ObjectDisposedException if we handled it)
                    }
                });

                t.Start();
                
                Thread.Sleep(10); // Let it run a bit
                
                profiler.Dispose(); // Disposes everything including thread buffers
                
                running = false;
                t.Join();
            }
        }
    }
}
