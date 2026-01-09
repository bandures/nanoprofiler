#if UNITY_2022_3_OR_NEWER
using UnityEngine;

namespace NanoProfiler
{
    public class UnityLowMemoryReporter : IProfilerService
    {
        private readonly Scope _lowMemoryScope;

        public UnityLowMemoryReporter(Profiler profiler)
        {
            _lowMemoryScope = new Scope(profiler, "Low Memory Warning", Profiler.INVALID_ID, MarkerFlags.Default);
        }
        
        public void Start()
        {
            Application.lowMemory += OnLowMemory;
        }

        public void Stop()
        {
            Application.lowMemory -= OnLowMemory;
        }

        private void OnLowMemory()
        {
            using var _ = _lowMemoryScope.Auto();
        }
    }
}
#endif