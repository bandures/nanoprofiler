using BenchmarkDotNet.Attributes;

namespace Meowtron.NanoProfiler.Tests
{
    [MemoryDiagnoser]
    public class ProfilerEnabledBenchmark
    {
        NanoProfiler _profilerInstance;
        NanoScope _testProfilerMarker;

        [GlobalSetup]
        public void Startup()
        {
            _profilerInstance = new NanoProfiler(new NanoProfilerSettings());
            _testProfilerMarker = _profilerInstance.CreateScope("TestMarker");
            _profilerInstance.Start();
        }

        [GlobalCleanup]
        public void Cleanup()
        {
            _profilerInstance.Stop();
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
