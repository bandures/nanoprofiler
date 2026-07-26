#if !NANO_PROFILER_UNITY_INTEGRATION
using System.Diagnostics;

namespace NanoProfiler
{
    public partial struct Scope
    {
        internal Scope(Profiler profiler, string name, uint groupId, MarkerFlags flags)
        {
            _profiler = profiler.Proxy;
            _id = profiler.RegisterMarker(name, groupId, flags);
            _groupId = groupId;
            _flags = flags; 
        }

        internal void Begin()
        {
            if (_profiler == null || !_profiler.IsGroupEnabled(_groupId))
                return;
            _profiler.CurrentThread.EmitBeginScope(_id);
        }

        internal void End()
        {
            if (_profiler == null || !_profiler.IsGroupEnabled(_groupId))
                return;
            _profiler.CurrentThread.EmitEndScope(_id);
        }
    }
}
#endif
