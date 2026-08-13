using NanoProfiler;

#if UNITY_2022_3_OR_NEWER
namespace NanoProfiler
{
    public static class UnityNanoProfilerSettingsExtensions
    {
        public static ProfilerSettingsBuilder AddUnityPlayerLoop(this ProfilerSettingsBuilder builder, Profiler profiler)
        {
            builder.AddService(new UnityPlayerLoop(profiler));
            return builder;
        }
        
        public static ProfilerSettingsBuilder AddUnityCounters(this ProfilerSettingsBuilder builder, Group profilerGroup)
        {
            builder.AddService(new UnityCounters(profilerGroup));
            return builder;
        }
        
        public static ProfilerSettingsBuilder AddUnityLowMemoryMarker(this ProfilerSettingsBuilder builder, Profiler profiler)
        {
            builder.AddService(new UnityLowMemoryReporter(profiler));
            return builder;
        }
    }
}
#endif