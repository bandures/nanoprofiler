#if NANO_PROFILER_UNITY_INTEGRATION
using Unity.Profiling;

namespace Meowtron.NanoProfiler
{
    [IgnoredByDeepProfiler]
    public readonly partial struct NanoScope
    {
        private static readonly ProfilerCategory DefaultCategory = new("Red", ProfilerCategoryColor.BurstJobs);
        private static readonly ProfilerCategory RedCategory = new("Red", ProfilerCategoryColor.Memory);
        private static readonly ProfilerCategory CyanCategory = new("Cyan", ProfilerCategoryColor.BurstJobs);
        private static readonly ProfilerCategory YellowCategory = new("Yellow", ProfilerCategoryColor.Lighting);
        private static readonly ProfilerCategory PurpleCategory = new("Purple", ProfilerCategoryColor.UI);
        private static readonly ProfilerCategory GrayCategory = new("Gray", ProfilerCategoryColor.Internal);
        
        private readonly ProfilerMarker _unityProfilerMarker;

        internal NanoScope(NanoProfiler profiler, string name, uint groupId, NanoMarkerFlags flags)
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
            if (!_flags.HasFlag(NanoMarkerFlags.DontReflectToUnity))
                _unityProfilerMarker.Begin();
            _profiler.CurrentThread.EmitBeginMarker(_id);
        }

        internal void End()
        {
            if (!_profiler.IsGroupEnabled(_groupId))
                return;
            _profiler.CurrentThread.EmitEndMarker(_id);
            if (!_flags.HasFlag(NanoMarkerFlags.DontReflectToUnity))
                _unityProfilerMarker.End();
        }

        private static ProfilerCategory GetCategoryForColor(NanoMarkerFlags flags)
        {
            return (flags & NanoMarkerFlags.ColorsMask) switch
            {
                NanoMarkerFlags.ColorRed => RedCategory,
                NanoMarkerFlags.ColorCyan => CyanCategory,
                NanoMarkerFlags.ColorYellow => YellowCategory,
                NanoMarkerFlags.ColorPurple => PurpleCategory,
                NanoMarkerFlags.ColorGray => GrayCategory,
                _ => DefaultCategory
            };
        }
    }
}
#endif