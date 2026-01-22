#nullable disable
using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace NanoProfiler
{
    public struct ProfilerSettings
    {
        /// <summary>
        /// Enable profiler automatically on creation
        /// </summary>
        public bool AutoStart { get; set; } = false;
        
        /// <summary>
        /// The maximum number of groups supported by the profiler.
        /// Groups over that limit will be ignored.
        /// </summary>
        public int MaxGroups { get; init; } = 16;
        
        /// <summary>
        /// The maximum number of markers supported by the profiler.
        /// Markers over that limit will be ignored.
        /// </summary>
        public int MaxMarkers { get; init; } = 512;
        
        /// <summary>
        /// The maximum number of threads that can be profiled simultaneously.
        /// Threads over that limit will be ignored.
        /// </summary>
        public int MaxThreads { get; init; } = 16;
        
        /// <summary>
        /// List of group names to be enabled.
        /// Events from disabled groups will be ignored.
        /// </summary>
        public IReadOnlyList<string> EnabledGroups { get; init; } = null;
        
        /// <summary>
        /// Services to be used by the profiler.
        /// Useful when something needs to be notified when the profiler starts or stops.
        /// </summary>
        public IReadOnlyList<IProfilerService> Services { get; init; } = null;
        
        /// <summary>
        /// Processor factories to be used by the profiler.
        /// Each factory will be called when the profiler is created to instantiate a processor.
        /// </summary>
        public IReadOnlyList<Func<Profiler, IProcessorBase>> ProcessorFactories { get; init; } = null;
        
        /// <summary>
        /// Logger the profiler and its processors can use to log warnings and errors.
        /// </summary>
        public ILogger Logger { get; init; } = null;
        
        public ProfilerSettings()
        {
        }
    }
}