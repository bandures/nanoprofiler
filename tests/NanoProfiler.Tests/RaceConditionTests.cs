using System;
using System.Threading;
using System.Threading.Tasks;
using NanoProfiler;
using NUnit.Framework;
using Meowtron.NanoProfiler.Tests.Helpers;

namespace Meowtron.NanoProfiler.Tests
{
    [TestFixture]
    public class RaceConditionTests
    {
        [Test]
        public void Profiler_Dispose_WhileThreadIsWriting_ShouldNotCrash()
        {
            // This test attempts to reproduce a race condition where Profiler.Dispose()
            // frees the MessagePipe buffer while a thread is still writing to it.
            
            // We run this multiple times or with high iteration count to increase probability.
            
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
