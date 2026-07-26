using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace NanoProfiler
{
    /// <summary>
    /// Instrument to measure sections which is longer than a frame or asynchronous. It doesn't provide
    /// hierarchy, but that also means you can nest activities even if nested activity will end after
    /// any activity higher in the call chain.
    /// 
    /// https://hitzone.atlassian.net/wiki/spaces/HZ/pages/596377609/Profiler+Release+mode+profiler
    /// </summary>
    [StructLayout(LayoutKind.Auto)]
    public readonly struct Activity
    {
        private readonly uint _id;
        private readonly uint _groupId;
        private readonly ProfilerProxy _profiler;
        
        internal Activity(Profiler profiler, string name, uint groupId, MarkerFlags flags = MarkerFlags.Default)
        {
            _profiler = profiler.Proxy;
            _id = profiler.RegisterMarker(name, groupId, flags);
            _groupId = groupId;
        }

        private int Begin(TypedAttribute param1 = default, TypedAttribute param2 = default, TypedAttribute param3 = default)
        {
            if (_profiler == null || !_profiler.IsGroupEnabled(_groupId))
                return Profiler.INVALID_ID;
            return _profiler.CurrentThread.EmitBeginActivity(_id, ref param1, ref param2, ref param3);
        }

        private void End(int uniqueId)
        {
            // uniqueId check also guards against uninitialized _profiler
            if (uniqueId == Profiler.INVALID_ID || !_profiler.IsGroupEnabled(_groupId))
                return;
            _profiler.CurrentThread.EmitEndActivity(_id, uniqueId);
        }

        public readonly struct AutoScope : IDisposable
        {
            private readonly Activity _activity;
            private readonly int _uniqueId = Profiler.INVALID_ID;
        
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public AutoScope(Activity activity, TypedAttribute param1 = default, TypedAttribute param2 = default, TypedAttribute param3 = default)
            {
                _activity = activity;
                _uniqueId = _activity.Begin(param1, param2, param3);
            }

            public void Dispose()
            {
                _activity.End(_uniqueId);
            }
        }
    
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public AutoScope Auto(TypedAttribute param1 = default, TypedAttribute param2 = default, TypedAttribute param3 = default) 
            => new (this, param1, param2, param3);
    }
}