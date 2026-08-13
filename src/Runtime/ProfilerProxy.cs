using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace NanoProfiler
{
    /// <summary>
    /// For efficiency most Profiler instruments are statically created readonly structs.
    /// They need to reference to profiler class, as profiler is the one that holds all the pipes.
    /// At the same time, if we change profiler settings, we need to recreate it.
    ///
    /// To get around it, all instruments reference proxy class instead, which is reused when
    /// profiler class is recreated. The proxy class doesn't do anything other than holding
    /// the current instance of the profiler and some quick access references for efficiency.
    ///
    /// If you have multiple instances of the profiler, each of them will have their own proxy. 
    /// </summary>
    internal sealed class ProfilerProxy
    {
        private Profiler _profiler;
        private ThreadLocal<ThreadLocalProfiler> _perThreadProfiler;

        internal ThreadLocalProfiler CurrentThread => _perThreadProfiler.Value;

        public void Setup(Profiler profiler)
        {
            _profiler = profiler;
            _perThreadProfiler = _profiler.PerThreadLocalProfiler;
        }

        internal void MarkNewFrame() => _profiler.MarkNewFrame();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool IsGroupEnabled(uint groupId)
        {
            return _profiler.IsGroupEnabled(groupId);
        }
        
        internal Group CreateGroup(string name, MarkerFlags flags) => new(_profiler, name, flags);
        internal Scope CreateScope(string name, uint groupId, MarkerFlags flags) => new(_profiler, name, groupId, flags);
        internal Activity CreateActivity(string name, uint groupId, MarkerFlags flags) => new(_profiler, name, groupId, flags);
        internal Counter CreateCounter(string name, uint groupId) => new(_profiler, name, groupId);
        internal Event CreateEvent(string name, uint groupId, MarkerFlags flags) => new(_profiler, name, groupId, flags);
    }
}