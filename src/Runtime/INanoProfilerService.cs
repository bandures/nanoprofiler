namespace Meowtron.NanoProfiler
{
    // Service that needs to run to provide profiler
    // functionality
    public interface INanoProfilerService
    {
        public void Start();
        public void Stop();
    }
}