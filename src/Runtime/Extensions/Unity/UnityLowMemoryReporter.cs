#if UNITY_2022_3_OR_NEWER
using UnityEngine;

namespace Meowtron.NanoProfiler
{
    public class UnityLowMemoryReporter : INanoProfilerService
    {
        private readonly NanoScope _lowMemoryScope;

        public UnityLowMemoryReporter(NanoProfiler profiler)
        {
            _lowMemoryScope = new NanoScope(profiler, "Low Memory Warning", NanoProfiler.INVALID_ID, NanoMarkerFlags.Default);
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