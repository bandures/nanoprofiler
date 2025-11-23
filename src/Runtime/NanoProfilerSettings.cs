using System;
using System.Collections.Generic;

namespace Meowtron.NanoProfiler
{
    public struct NanoProfilerSettings
    {
        public bool AutoStart { get; set; } = false;
        public int MaxGroups { get; init; } = 16;
        public int MaxMarkers { get; init; } = 512;
        public int MaxThreads { get; init; } = 16;
        public IReadOnlyList<string> EnabledGroups { get; init; } = null;
        public IReadOnlyList<INanoProfilerService> Services { get; init; } = null;
        public IReadOnlyList<Func<NanoProfiler, INanoProcessorBase>> ProcessorFactories { get; init; } = null;
        
        public NanoProfilerSettings()
        {
        }
    }
}