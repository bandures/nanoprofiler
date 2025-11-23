using System;
using System.Threading;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Meowtron.NanoProfiler
{
    /// <summary>
    /// Small and lean release-mode profiler.
    ///
    /// It supports:
    /// - Scope - for function calls instrumentation
    /// - Activity - for long-running activity instrumentation
    /// - Counter - for gauge value type instrumentation
    /// - Group - for grouping and disabling instruments
    ///
    /// The way it works is by off-loading all expensive operations
    /// to a separate thread where expensive "processors" can process
    /// collected information and send/visualize/etc it for the user.
    /// Thread1 - Marker   - Pack to message pipe \             / - Processor 1
    /// Thread2 - Counter  - Pack to message pipe -> Collector -  - Processor 2
    /// Thread3 - Activity - Pack to message pipe /             \ - Processor 3
    ///
    /// https://hitzone.atlassian.net/wiki/spaces/HZ/pages/596377609/NanoProfiler+Release+mode+profiler
    /// </summary>
    public sealed class NanoProfiler : IDisposable
    {
        public const int INVALID_ID = 0;
        private const string DEFAULT_GROUP_NAME = "<no group>";
        private const string INVALID_MARKER_NAME = "<invalid>";
        public record struct GroupInfo(string Name, NanoMarkerFlags Flags, bool State);
        public record struct MarkerInfo(string Name, uint GroupId, NanoMarkerFlags Flags);
        
        private readonly NanoProfilerSettings _settings;
        private readonly ManualResetEventSlim _dataReadySignal;
        
        // Instruments
        private readonly NanoProfilerProxy _proxy;
        private readonly FixedArray<GroupInfo> _groups;
        private readonly Dictionary<string, uint> _groupNameToId;
        private readonly FixedArray<MarkerInfo> _markers;
        private readonly Dictionary<string, uint> _markerNameToId;

        // Integrations
        private readonly List<INanoProcessorBase> _processors;
        
        // State
        private readonly FixedArray<NanoThreadLocalProfiler> _threads;
        private readonly ThreadLocal<NanoThreadLocalProfiler> _perThreadProfiler;
        
        private bool _enabled;
        private int _nextUniqueId;
        private NanoCollector _collector;
        
        public NanoProfiler(NanoProfilerSettings settings, NanoProfiler inheritFrom = null)
        {
            _settings = settings;
            _dataReadySignal = new ManualResetEventSlim(false);
            
            _enabled = false;
            _groups = new FixedArray<GroupInfo>(settings.MaxGroups);
            _groupNameToId = new Dictionary<string, uint>(settings.MaxGroups);
            _markers = new FixedArray<MarkerInfo>(settings.MaxMarkers);
            _markerNameToId = new Dictionary<string, uint>(settings.MaxMarkers);
            _threads = new FixedArray<NanoThreadLocalProfiler>(settings.MaxThreads);
            
            _perThreadProfiler = new(() => new NanoThreadLocalProfiler(this, _dataReadySignal) { Enabled = _enabled }, true);

            _proxy = inheritFrom?.Proxy ?? new NanoProfilerProxy();

            _proxy.Setup(this);
            SetupDefaultInstruments();
            InheritInstruments(inheritFrom);
            CreateProcessors(out _processors);
            SetupGroups(settings.EnabledGroups);
            
            if (_settings.AutoStart)
                Start();
        }

        public bool Enabled => _enabled;
        public NanoProfilerSettings Settings => _settings;
        internal NanoProfilerProxy Proxy => _proxy;
        internal ThreadLocal<NanoThreadLocalProfiler> PerThreadLocalProfiler => _perThreadProfiler;
        internal NanoThreadLocalProfiler CurrentThread => _perThreadProfiler.Value;
        public IEnumerable<GroupInfo> Groups => _groups;
        public IEnumerable<MarkerInfo> Markers => _markers;
        
        public void Dispose()
        {
            Stop();
            
            _collector?.Dispose();

            lock (_threads)
            {
                foreach (NanoThreadLocalProfiler threadLocalProfiler in _threads)
                    threadLocalProfiler.Dispose();
                _threads.Clear();
            }

            _perThreadProfiler.Dispose();
            _dataReadySignal.Dispose();
        }

        /// <summary>
        /// Enables profiler and starts capturing
        /// </summary>
        public void Start()
        {
            if (_enabled)
                throw new InvalidOperationException("NanoProfiler is already running");

            // Start all services
            if (_settings.Services != null)
            {
                foreach (var service in _settings.Services)
                    service.Start();
            }
            
            // Capture start must be the first command, so we  send it before enabling thread profilers
            _dataReadySignal.Reset();
            CurrentThread.EmitCaptureStart();

            // Set global enable first, so that if a new thread starts it'll inherit the correct state
            _enabled = true;
            
            // Enable all thread profilers
            // _threads/_markers must be locked before enable, otherwise thread might get registered after enable is set
            // info sent, but it'll be sent again? The same applies to markers
            // 
            lock (_threads)
            {
                // Starts collector thread, while threads are locked
                _collector = new NanoCollector(_threads, _processors, _dataReadySignal);

                foreach (NanoThreadLocalProfiler thread in _threads)
                    thread.Enabled = true;
            }

            // Emit all known groups on start
            lock (_groups)
            {
                for (var id = 1; id < _groups.Count; id++)
                {
                    var group = _groups[id];
                    if (group.Flags.HasFlag(NanoMarkerFlags.PreCreated))
                        continue;
                    CurrentThread.EmitGroupInfo((uint)id, group.Name, group.Flags);
                }
            }
            
            // Emit all known markers on start
            lock (_markers)
            {
                for (var id = 1; id < _markers.Count; id++)
                {
                    var marker = _markers[id];
                    if (marker.Flags.HasFlag(NanoMarkerFlags.PreCreated))
                        continue;
                    CurrentThread.EmitMarkerInfo((uint)id, marker.Name, marker.GroupId, marker.Flags);
                }
            }
        }

        /// <summary>
        /// Stops profiler capturing and disables profiler
        /// </summary>
        public void Stop()
        {
            if (!_enabled)
                return;
            
            // Emit stop before the pipe is terminated
            CurrentThread.EmitCaptureStop();

            // Disable right after stop is sent
            _enabled = false;
            
            // Disable all thread profilers to prevent new messages from being sent
            lock (_threads)
            {
                foreach (NanoThreadLocalProfiler thread in _threads)
                    thread.Enabled = false;
            }
            
            // Stops collector thread
            _collector.Dispose();
            _collector = null;
            
            // Stop all services
            if (_settings.Services != null)
            {
                foreach (var service in _settings.Services)
                    service.Stop();
            }

            // Destroy all processors
            foreach (var processor in _processors)
                processor.Dispose();
            _processors.Clear();
        }

        /// <summary>
        /// Flushes all accumulated buffers in pipes and collector
        /// </summary>
        public void Flush()
        {
            CurrentThread.EmitFlush();
        }

        /// <summary>
        /// Returns a new or existing group with the provided name 
        /// </summary>
        public NanoGroup CreateGroup(string name, NanoMarkerFlags flags = NanoMarkerFlags.Default) => new(this, name, flags);
        
        /// <summary>
        /// Returns a new or existing scope instrument assigned to the default group
        /// </summary>
        public NanoScope CreateScope(string name, NanoMarkerFlags flags = NanoMarkerFlags.Default) => new(this, name, INVALID_ID, flags);
        
        /// <summary>
        /// Returns a new or existing activity instrument assigned to the default group
        /// </summary>
        public NanoActivity CreateActivity(string name) => new(this, name, INVALID_ID);
        
        /// <summary>
        /// Returns a new or existing counter instrument assigned to the default group
        /// </summary>
        public NanoCounter CreateCounter(string name) => new(this, name, INVALID_ID);

        /// <summary>
        /// Returns pipes statstics
        /// </summary>
        /// <param name="transferredBytes"></param>
        /// <param name="discardedBytes"></param>
        public void GetStatistics(out long transferredBytes, out long discardedBytes)
        {
            transferredBytes = 0;
            discardedBytes = 0;
            lock (_threads)
            {
                foreach (NanoThreadLocalProfiler thread in _threads)
                {
                    thread.GetStatistics(out var threadTransferredBytes, out var threadDiscardedBytes);
                    transferredBytes += threadTransferredBytes;
                    discardedBytes += threadDiscardedBytes;
                }
            }
        }

        /// <summary>
        /// Returns registered processor
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public T GetProcessor<T>() where T : class, INanoProcessorBase
        {
            foreach (var processorBase in _processors)
            {
                if (processorBase is T processor)
                    return processor;
            }

            return null;
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void MarkNewFrame()
        {
            CurrentThread.EmitNewFrame();
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal int NextUniqueId()
        {
            return Interlocked.Increment(ref _nextUniqueId);
        }

        internal uint RegisterMarker(string name, uint groupId, NanoMarkerFlags flags)
        {
            uint id;
            bool emit = true;
            lock (_markers)
            {
                if (!_markerNameToId.TryGetValue(name, out id))
                {
                    id = (uint)_markers.Count;
                    if (!_markers.Add(new MarkerInfo { Name = name, GroupId = groupId, Flags = flags }))
                        return INVALID_ID;
                    
                    _markerNameToId[name] = id;
                }
                else
                {
                    ref var markerInfo = ref _markers[(int)id];
                    if (markerInfo.Flags.HasFlag(NanoMarkerFlags.PreCreated))
                        markerInfo.Flags = flags;
                    else
                        emit = false;
                }
            }
            
            if (_enabled && emit)
                CurrentThread.EmitMarkerInfo(id, name, groupId, flags);
            
            return id;
        }

        internal uint RegisterGroup(string name, NanoMarkerFlags flags)
        {
            uint id;
            bool emit = true;
            lock (_groups)
            {
                if (!_groupNameToId.TryGetValue(name, out id))
                {
                    id = (uint)_groups.Count;
                    if (!_groups.Add(new GroupInfo { Name = name, Flags = flags}))
                        return INVALID_ID;
                    _groupNameToId[name] = id;
                }
                else
                {
                    ref var groupInfo = ref _groups[(int)id];
                    if (groupInfo.Flags.HasFlag(NanoMarkerFlags.PreCreated))
                        groupInfo.Flags = flags;
                    else
                        emit = false;
                }
            }

            if (_enabled && emit)
                CurrentThread.EmitGroupInfo(id, name, flags);

            return id;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool IsGroupEnabled(uint groupId)
        {
            // ReSharper disable once InconsistentlySynchronizedField
            // Once group is allocated, it's safe to access from any thread.
            return _groups[(int)groupId].State;
        }

        internal void RegisterThread(NanoThreadLocalProfiler thread)
        {
            lock (_threads)
            {
                _threads.Add(thread);
            }
            
            _collector?.RegisterThreadProfiler(thread.ThreadId, thread.MessageReader, thread.ReceivedByCollectorSignal);
        }

        private void SetupDefaultInstruments()
        {
            if (_groups.Count != 0 || _markers.Count != 0)
                throw new Exception("Setup defaults should be called before any other instruments setup");

            // These instruments intentionally not added to name lookup, as we don't want
            // them to be reused if user creates a group/marker/etc with the same name.
            _groups.Add(new GroupInfo(DEFAULT_GROUP_NAME, NanoMarkerFlags.Default, true));
            _markers.Add(new MarkerInfo(INVALID_MARKER_NAME, INVALID_ID, NanoMarkerFlags.Default));
        }
        
        private void CreateProcessors(out List<INanoProcessorBase> processors)
        {
            if (_settings.ProcessorFactories == null)
            {
                processors = new List<INanoProcessorBase>();
                return;
            }
            
            processors = new List<INanoProcessorBase>(_settings.ProcessorFactories.Count);
            foreach (var processorFactory in _settings.ProcessorFactories)
            {
                var processor = processorFactory(this);
                processors.Add(processor);
            }
        }

        private void InheritInstruments(NanoProfiler src)
        {
            if (src == null)
                return;
            
            if (src._groups.Count > _groups.Capacity)
                throw new ArgumentOutOfRangeException($"Groups capacity {_groups.Capacity} is smaller than required to inherit {src._groups.Count} them.");
            if (src._markers.Count > _markers.Capacity)
                throw new ArgumentOutOfRangeException($"Markers capacity {_markers.Capacity} is smaller than required to inherit {src._markers.Count} them.");

            for (var id = 1; id < src._groups.Count; id++)
            {
                ref var group = ref src._groups[id];
                _groups.Add(group);
                _groupNameToId[group.Name] = (uint)id;
            }

            for (var id = 1; id < src._markers.Count; id++)
            {
                ref var marker = ref src._markers[id];
                _markers.Add(marker);
                _markerNameToId[marker.Name] = (uint)id;
            }
        }

        private void SetupGroups(IReadOnlyList<string> enabledGroups)
        {
            if (enabledGroups == null)
                return;

            foreach (var group in enabledGroups)
            {
                if (!_groupNameToId.TryGetValue(group, out var groupId))
                    groupId = RegisterGroup(group, NanoMarkerFlags.PreCreated);

                ref var groupState = ref _groups[(int)groupId];
                groupState = groupState with { State = true };
            }
        }
    }
}