using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Meowtron.NanoProfiler
{
    /// <summary>
    /// Instrument to measure a section that is local and synchronous. Scope is hierarchical
    /// and will show nested scope markers within hierarchy. However, it also means that any
    /// child scopes should end before parent scope ends.
    /// 
    /// https://hitzone.atlassian.net/wiki/spaces/HZ/pages/596377609/NanoProfiler+Release+mode+profiler
    /// </summary>
    [StructLayout(LayoutKind.Auto)]
    public readonly partial struct NanoScope
    {
        private readonly uint _id;
        private readonly uint _groupId;
        private readonly NanoProfilerProxy _profiler;
        private readonly NanoMarkerFlags _flags;

        public readonly struct AutoScope : IDisposable
        {
            private readonly NanoScope _scope;
        
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public AutoScope(NanoScope scope)
            {
                _scope = scope;
                _scope.Begin();
            }

            public void Dispose()
            {
                _scope.End();
            }
        }
    
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public AutoScope Auto() => new (this);
    }
}