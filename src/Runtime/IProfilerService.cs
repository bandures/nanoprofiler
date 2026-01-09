namespace NanoProfiler
{
    // Service that needs to run to provide profiler
    // functionality
    public interface IProfilerService
    {
        public void Start();
        public void Stop();
    }
}