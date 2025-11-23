using System.Runtime.InteropServices;

namespace Meowtron.NanoProfiler
{
    /// <summary>
    /// Primitive to group scopes and activities together. It provides functions to act on
    /// a group of instruments, like setting flags or activating them. For processors that
    /// support it, it'll show activities grouped together in profiler view.
    ///
    /// https://hitzone.atlassian.net/wiki/spaces/HZ/pages/596377609/NanoProfiler+Release+mode+profiler
    /// </summary>
    [StructLayout(LayoutKind.Auto)]
    public readonly struct NanoGroup
    {
        private readonly uint _id;
        private readonly NanoMarkerFlags _flags;
        private readonly NanoProfilerProxy _profiler;

        internal NanoGroup(NanoProfiler profiler, string name, NanoMarkerFlags flags)
        {
            _profiler = profiler.Proxy;
            _flags = flags;
            _id = profiler.RegisterGroup(name, flags);
        }
        
        public NanoActivity CreateActivity(string name) => _profiler.CreateActivity(name, _id, _flags);
        public NanoScope CreateScope(string name) => _profiler.CreateScope(name, _id, _flags);
        public NanoCounter CreateCounter(string name) => _profiler.CreateCounter(name, _id);
    }
}