using BenchmarkDotNet.Attributes;
using NanoProfiler;

namespace Meowtron.NanoProfiler.Tests
{
    [MemoryDiagnoser]
    public class ProfilerEnabledNoCollection
    {
        Profiler _profilerInstance;
        Scope _testProfilerMarker;

        [GlobalSetup]
        public void Startup()
        {
            _profilerInstance = new Profiler(new ProfilerSettings());
            _testProfilerMarker = _profilerInstance.CreateScope("TestMarker");
        }

        [GlobalCleanup]
        public void Cleanup()
        {
            _profilerInstance.Dispose();
        }
        
        [Benchmark]
        public void BeginEnd()
        {
            _testProfilerMarker.Begin();
            _testProfilerMarker.End();
        }
        
        [Benchmark]
        public void AutoScope()
        {
            using var _ = _testProfilerMarker.Auto();
        }
    }
}
