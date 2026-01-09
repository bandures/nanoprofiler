using System;
using System.Collections.Generic;

namespace NanoProfiler
{
    public struct ProfilerSettings
    {
        public bool AutoStart { get; set; } = false;
        public int MaxGroups { get; init; } = 16;
        public int MaxMarkers { get; init; } = 512;
        public int MaxThreads { get; init; } = 16;
        public IReadOnlyList<string> EnabledGroups { get; init; } = null;
        public IReadOnlyList<IProfilerService> Services { get; init; } = null;
        public IReadOnlyList<Func<Profiler, IProcessorBase>> ProcessorFactories { get; init; } = null;
        
        public ProfilerSettings()
        {
        }
    }
}