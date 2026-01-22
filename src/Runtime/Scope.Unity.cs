#if NANO_PROFILER_UNITY_INTEGRATION
using Unity.Profiling;

namespace NanoProfiler
{
    [IgnoredByDeepProfiler]
    public readonly partial struct Scope
    {
        private static readonly ProfilerCategory DefaultCategory = new("Red", ProfilerCategoryColor.BurstJobs);
        private static readonly ProfilerCategory RedCategory = new("Red", ProfilerCategoryColor.Memory);
        private static readonly ProfilerCategory CyanCategory = new("Cyan", ProfilerCategoryColor.BurstJobs);
        private static readonly ProfilerCategory YellowCategory = new("Yellow", ProfilerCategoryColor.Lighting);
        private static readonly ProfilerCategory PurpleCategory = new("Purple", ProfilerCategoryColor.UI);
        private static readonly ProfilerCategory GrayCategory = new("Gray", ProfilerCategoryColor.Internal);
        
        private readonly ProfilerMarker _unityProfilerMarker;

        internal Scope(Profiler profiler, string name, uint groupId, MarkerFlags flags)
        {
            _profiler = profiler.Proxy;
            _id = profiler.RegisterMarker(name, groupId, flags);
            _groupId = groupId;
            _flags = flags;
            _unityProfilerMarker = new ProfilerMarker(GetCategoryForColor(flags), name);
        }

        internal void Begin()
        {
            if (!_profiler.IsGroupEnabled(_groupId))
                return;
            if (!_flags.HasFlag(MarkerFlags.DontReflectToUnity))
                _unityProfilerMarker.Begin();
            _profiler.CurrentThread.EmitBeginScope(_id);
        }

        internal void End()
        {
            if (!_profiler.IsGroupEnabled(_groupId))
                return;
            _profiler.CurrentThread.EmitEndScope(_id);
            if (!_flags.HasFlag(MarkerFlags.DontReflectToUnity))
                _unityProfilerMarker.End();
        }

        private static ProfilerCategory GetCategoryForColor(MarkerFlags flags)
        {
            return (flags & MarkerFlags.ColorsMask) switch
            {
                MarkerFlags.ColorRed => RedCategory,
                MarkerFlags.ColorCyan => CyanCategory,
                MarkerFlags.ColorYellow => YellowCategory,
                MarkerFlags.ColorPurple => PurpleCategory,
                MarkerFlags.ColorGray => GrayCategory,
                _ => DefaultCategory
            };
        }
    }
}
#endif