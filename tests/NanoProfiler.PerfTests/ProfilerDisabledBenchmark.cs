using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;

BenchmarkRunner.Run<Meowtron.NanoProfiler.Tests.ProfilerDisabledBenchmark>();
BenchmarkRunner.Run<Meowtron.NanoProfiler.Tests.ProfilerEnabledBenchmark>();
BenchmarkRunner.Run<Meowtron.NanoProfiler.Tests.ProfilerEnabledNoCollection>();

namespace Meowtron.NanoProfiler.Tests
{
    [MemoryDiagnoser]
    public class ProfilerDisabledBenchmark
    {
        NanoProfiler? _profilerInstance;
        NanoScope _testProfilerMarker;

        [GlobalSetup]
        public void Startup()
        {
            _profilerInstance = new NanoProfiler(new NanoProfilerSettings());
            _testProfilerMarker = _profilerInstance.CreateScope("TestMarker");
        }

        [GlobalCleanup]
        public void Cleanup()
        {
            _profilerInstance?.Dispose();
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
