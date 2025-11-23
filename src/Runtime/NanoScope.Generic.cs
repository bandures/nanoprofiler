#if !NANO_PROFILER_UNITY_INTEGRATION
using System.Diagnostics;

namespace Meowtron.NanoProfiler
{
    public partial struct NanoScope
    {
        internal NanoScope(NanoProfiler profiler, string name, uint groupId, NanoMarkerFlags flags)
        {
            _profiler = profiler.Proxy;
            _id = profiler.RegisterMarker(name, groupId, flags);
            _groupId = groupId;
            _flags = flags; 
        }

        internal void Begin()
        {
            if (!_profiler.IsGroupEnabled(_groupId))
                return;
            _profiler.CurrentThread.EmitBeginMarker(_id);
        }

        internal void End()
        {
            if (!_profiler.IsGroupEnabled(_groupId))
                return;
            _profiler.CurrentThread.EmitEndMarker(_id);
        }
    }
}
#endif
