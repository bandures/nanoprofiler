using System.Runtime.InteropServices;

namespace NanoProfiler
{
    /// <summary>
    /// Primitive to group scopes and activities together. It provides functions to act on
    /// a group of instruments, like setting flags or activating them. For processors that
    /// support it, it'll show activities grouped together in profiler view.
    ///
    /// https://hitzone.atlassian.net/wiki/spaces/HZ/pages/596377609/Profiler+Release+mode+profiler
    /// </summary>
    [StructLayout(LayoutKind.Auto)]
    public readonly struct Group
    {
        private readonly uint _id;
        private readonly MarkerFlags _flags;
        private readonly ProfilerProxy _profiler;

        internal Group(Profiler profiler, string name, MarkerFlags flags)
        {
            _profiler = profiler.Proxy;
            _flags = flags;
            _id = profiler.RegisterGroup(name, flags);
        }
        
        public bool IsEnabled => _profiler != null && _profiler.IsGroupEnabled(_id);

        public Activity CreateActivity(string name) => _profiler.CreateActivity(name, _id, _flags);
        public Scope CreateScope(string name) => _profiler.CreateScope(name, _id, _flags);
        public Counter CreateCounter(string name) => _profiler.CreateCounter(name, _id);
        public Event CreateEvent(string name) => _profiler.CreateEvent(name, _id, _flags);
    }
}