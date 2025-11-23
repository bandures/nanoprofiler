using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Meowtron.NanoProfiler
{
    /// <summary>
    /// For efficiency most NanoProfiler instruments are statically created readonly structs.
    /// They need to reference to profiler class, as profiler is the one that holds all the pipes.
    /// At the same time, if we change profiler settings, we need to recreate it.
    ///
    /// To get around it, all instruments reference proxy class instead, which is reused when
    /// profiler class is recreated. The proxy class doesn't do anything other than holding
    /// the current instance of the profiler and some quick access references for efficiency.
    ///
    /// If you have multiple instances of the profiler, each of them will have their own proxy. 
    /// </summary>
    internal sealed class NanoProfilerProxy
    {
        private NanoProfiler _profiler;
        private ThreadLocal<NanoThreadLocalProfiler> _perThreadProfiler;

        internal NanoThreadLocalProfiler CurrentThread => _perThreadProfiler.Value;

        public void Setup(NanoProfiler profiler)
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
        
        internal NanoGroup CreateGroup(string name, NanoMarkerFlags flags) => new(_profiler, name, flags);
        internal NanoScope CreateScope(string name, uint groupId, NanoMarkerFlags flags) => new(_profiler, name, groupId, flags);
        internal NanoActivity CreateActivity(string name, uint groupId, NanoMarkerFlags flags) => new(_profiler, name, groupId, flags);
        internal NanoCounter CreateCounter(string name, uint groupId) => new(_profiler, name, groupId);
    }
}