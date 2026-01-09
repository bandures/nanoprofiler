using System;
using System.Collections.Generic;

namespace NanoProfiler
{
    public class ProfilerSettingsBuilder
    {
        private bool _autoStart;
        private int? _maxGroups;
        private int? _maxMarkers;
        private int? _maxThreads;
        private readonly List<string> _enabledGroups = new();
        private readonly List<IProfilerService> _services = new();
        private readonly List<Func<Profiler, IProcessorBase>> _processorFactories = new();

        public ProfilerSettings Build()
        {
            var settings = new ProfilerSettings()
            {
                AutoStart = _autoStart,
                ProcessorFactories = _processorFactories,
                Services = _services,
                EnabledGroups = _enabledGroups
            };
            if (_maxGroups.HasValue)
                settings = settings with { MaxGroups = _maxGroups.Value };
            if (_maxMarkers.HasValue)
                settings = settings with { MaxMarkers = _maxMarkers.Value };
            if (_maxThreads.HasValue)
                settings = settings with { MaxThreads = _maxThreads.Value };
            return settings;
        }

        public ProfilerSettingsBuilder AddProcessor(Func<Profiler, IProcessorBase> factory)
        {
            _processorFactories.Add(factory);
            return this;
        }

        public ProfilerSettingsBuilder AddService(IProfilerService service)
        {
            _services.Add(service);
            return this;
        }

        public ProfilerSettingsBuilder EnableGroups(params string[] groups)
        {
            _enabledGroups.AddRange(groups);
            return this;
        }

        public ProfilerSettingsBuilder WithGroupsCapacity(int value)
        {
            _maxGroups = value;
            return this;
        }

        public ProfilerSettingsBuilder WithMarkersCapacity(int value)
        {
            _maxMarkers = value;
            return this;
        }

        public ProfilerSettingsBuilder WithThreadsCapacity(int value)
        {
            _maxThreads = value;
            return this;
        }

        public ProfilerSettingsBuilder WithAutoStart(bool value)
        {
            _autoStart = value;
            return this;
        }
    }
}