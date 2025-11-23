using System;
using System.Collections.Generic;

namespace Meowtron.NanoProfiler
{
    public class NanoProfilerSettingsBuilder
    {
        private bool _autoStart;
        private int? _maxGroups;
        private int? _maxMarkers;
        private int? _maxThreads;
        private readonly List<string> _enabledGroups = new();
        private readonly List<INanoProfilerService> _services = new();
        private readonly List<Func<NanoProfiler, INanoProcessorBase>> _processorFactories = new();

        public NanoProfilerSettings Build()
        {
            var settings = new NanoProfilerSettings()
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

        public NanoProfilerSettingsBuilder AddProcessor(Func<NanoProfiler, INanoProcessorBase> factory)
        {
            _processorFactories.Add(factory);
            return this;
        }

        public NanoProfilerSettingsBuilder AddService(INanoProfilerService service)
        {
            _services.Add(service);
            return this;
        }

        public NanoProfilerSettingsBuilder EnableGroups(params string[] groups)
        {
            _enabledGroups.AddRange(groups);
            return this;
        }

        public NanoProfilerSettingsBuilder WithGroupsCapacity(int value)
        {
            _maxGroups = value;
            return this;
        }

        public NanoProfilerSettingsBuilder WithMarkersCapacity(int value)
        {
            _maxMarkers = value;
            return this;
        }

        public NanoProfilerSettingsBuilder WithThreadsCapacity(int value)
        {
            _maxThreads = value;
            return this;
        }

        public NanoProfilerSettingsBuilder WithAutoStart(bool value)
        {
            _autoStart = value;
            return this;
        }
    }
}