#if UNITY_2022_3_OR_NEWER
namespace Meowtron.NanoProfiler
{
    public static class UnityNanoProfilerSettingsExtensions
    {
        public static NanoProfilerSettingsBuilder AddUnityPlayerLoop(this NanoProfilerSettingsBuilder builder, NanoProfiler profiler)
        {
            builder.AddService(new UnityPlayerLoop(profiler));
            return builder;
        }
        
        public static NanoProfilerSettingsBuilder AddUnityCounters(this NanoProfilerSettingsBuilder builder, NanoProfiler profiler)
        {
            builder.AddService(new UnityCounters(profiler));
            return builder;
        }
        
        public static NanoProfilerSettingsBuilder AddUnityLowMemoryMarker(this NanoProfilerSettingsBuilder builder, NanoProfiler profiler)
        {
            builder.AddService(new UnityLowMemoryReporter(profiler));
            return builder;
        }
    }
}
#endif