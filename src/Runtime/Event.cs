using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace NanoProfiler
{
    /// <summary>
    /// Instrument to record a point in time, such as an input or a state transition. It has no
    /// duration and no nesting, so it needs no matching end call.
    /// </summary>
    [StructLayout(LayoutKind.Auto)]
    public readonly struct Event
    {
        private readonly uint _id;
        private readonly uint _groupId;
        private readonly ProfilerProxy _profiler;

        internal Event(Profiler profiler, string name, uint groupId, MarkerFlags flags = MarkerFlags.Default)
        {
            _profiler = profiler.Proxy;
            _id = profiler.RegisterMarker(name, groupId, flags);
            _groupId = groupId;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Record(TypedAttribute param1 = default, TypedAttribute param2 = default, TypedAttribute param3 = default)
        {
            if (_profiler == null || !_profiler.IsGroupEnabled(_groupId))
                return;

            _profiler.CurrentThread.EmitEvent(_id, ref param1, ref param2, ref param3);
        }
    }
}
