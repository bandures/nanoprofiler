#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Extensions.Logging;
using Perfetto.Protos;

namespace NanoProfiler
{
    public sealed class PerfettoProcessor :
        IProcessor<Protocol.CaptureStart>,
        IProcessor<Protocol.CaptureStop>,
        IProcessor<Protocol.RegisterMarker>,
        IProcessor<Protocol.RegisterGroup>,
        IProcessor<Protocol.ScopeBegin>,
        IProcessor<Protocol.ScopeEnd>,
        IProcessor<Protocol.ActivityBegin>,
        IProcessor<Protocol.ActivityEnd>,
        IProcessor<Protocol.Event>,
        IProcessor<Protocol.CounterSample>,
        IProcessor<Protocol.Frame>
    {
        private record struct GroupInfo(ulong TrackId, MarkerFlags Flags);
        private record struct MarkerInfo(string Name, ulong GroupTrackId, bool IsCritical, bool Interned, ulong InternId);
        private record struct ActivityInfo(ulong TrackId, ulong InternId);
        private record struct CounterInfo(ulong TrackId, bool IsCritical, long LastSampleTimestamp, bool HasValue, double Value);

        private readonly PerfettoProcessorSettings _settings;
        private readonly Dictionary<uint, GroupInfo> _groups = new();
        private readonly Dictionary<uint, MarkerInfo> _markers = new();
        private readonly Dictionary<string, ActivityInfo> _activities = new();
        private readonly Dictionary<string, ulong> _eventNames = new();
        private readonly Dictionary<int, ulong> _openActivities = new();
        private readonly Dictionary<uint, CounterInfo> _counters = new();
        private readonly Dictionary<int, PerfettoThreadScope> _threadTracks = new();
        private readonly long _spikeMinimumTicks;
        private readonly long _countersSamplingRateTicks;
        private readonly StringBuilder _stringBuilder = new(256);
        
        private static readonly uint TrustedPacketSequenceId;
        
        private readonly SpikeDetector _spikeDetector;

        private PerfettoFrameRecorder? _frameRecorder;
        private PerfettoFileWriter? _fileWriter;
        private ulong _scopeRootTrackId;
        private ulong _activityRootTrackId;
        private long _lastFrameTimestamp;
        private int _frameIndex;

        private bool _criticalOnly;   // >= CriticalBudgetBytes: keep only critical events
        private bool _hardStop;       // >= MaxFileSizeBytes: stop writing entirely

        static PerfettoProcessor()
        {
            // Unique ID that needs to be the same for all trace packets
            TrustedPacketSequenceId = (uint)DateTime.UtcNow.Ticks;
        }
        
        public PerfettoProcessor(PerfettoProcessorSettings settings)
        {
            if (settings.CriticalBudgetBytes >= settings.MaxFileSizeBytes)
            {
                throw new ArgumentException(
                    $"{nameof(settings.CriticalBudgetBytes)} ({settings.CriticalBudgetBytes}) must be below " +
                    $"{nameof(settings.MaxFileSizeBytes)} ({settings.MaxFileSizeBytes}); otherwise the critical-only phase never runs.", nameof(settings));
            }

            _settings = settings;
            _fileWriter = null;
            _frameRecorder = null;
            _spikeMinimumTicks = TimeHelper.MillisecondsToTimestamp(_settings.SpikeMinimumMs);
            _countersSamplingRateTicks = TimeHelper.MillisecondsToTimestamp(_settings.CountersSamplingRate.TotalMilliseconds);
            _spikeDetector = new SpikeDetector(_settings.SpikeWindowFrames, _spikeMinimumTicks, _settings.SpikeRatioMultiplier);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool ShouldEmit(bool isCritical) => !_hardStop && (!_criticalOnly || isCritical);

        public void Dispose()
        {
            _fileWriter?.Dispose();
            _frameRecorder?.Dispose();
        }

        public void HandleMessage(int threadId, ref Protocol.CaptureStart msg)
        {
            var appending = File.Exists(_settings.FileName);
            _fileWriter = new PerfettoFileWriter(_settings.FileName, _settings.Logger, TrustedPacketSequenceId);
            _fileWriter.SetupStream(appending);
            _fileWriter.IsFirstPacketInSequence = !appending;
            _scopeRootTrackId = _fileWriter.AddProcessTrack(_settings.ProcessId, _settings.ProcessName);
            _activityRootTrackId = _fileWriter.AddTrack("Activity");

            // Metadata is a one-shot header
            if (!appending && _settings.Metadata != null)
                _fileWriter.AddTraceAttributes(_settings.Metadata);

            _criticalOnly = false;
            _hardStop = false;
            _spikeDetector.Reset();

            if (_settings.DynamicScopes)
                _frameRecorder = new PerfettoFrameRecorder(_settings.DynamicScopesBufferSize, _settings.Logger);
        }
        
        public void HandleMessage(int threadId, ref Protocol.CaptureStop msg)
        {
            FlushCounters(msg.Timestamp);
            
            foreach (var threadScope in _threadTracks.Values)
                threadScope.CloseScopes(msg.Timestamp);
            
            _fileWriter?.Dispose();
            _fileWriter = null;
            _frameRecorder?.Dispose();
            _frameRecorder = null;
            _lastFrameTimestamp = 0;
            _scopeRootTrackId = 0;
            _activityRootTrackId = 0;
            _criticalOnly = false;
            _hardStop = false;
            
            _groups.Clear();
            _markers.Clear();
            _activities.Clear();
            _eventNames.Clear();
            _openActivities.Clear();
            _counters.Clear();
            _threadTracks.Clear();
        }

        private void FlushCounters(long timestamp)
        {
            if (_fileWriter == null)
                return;

            foreach (var counter in _counters.Values)
            {
                if (!counter.HasValue)
                    continue;

                if (!ShouldEmit(counter.IsCritical))
                    continue;

                var trackEvent = _fileWriter.GetTrackEvent();
                trackEvent.Type = TrackEvent.Types.Type.Counter;
                trackEvent.TrackUuid = counter.TrackId;
                trackEvent.CounterValue = (long)counter.Value;
            
                _fileWriter.AddTimestampEvent(trackEvent, timestamp, null, false);
            }
        }

        public void HandleMessage(int threadId, ref Protocol.RegisterGroup msg)
        {
            if (_fileWriter == null)
                return;

            if (_groups.TryGetValue(msg.GroupId, out var existing))
            {
                if (existing.Flags != msg.Flags)
                {
                    _settings.Logger?.LogError($"Perfetto group {msg.GroupId} '{msg.Name}' re-registered with changed flags " +
                        $"({existing.Flags} -> {msg.Flags}); group flags are expected to be immutable.");
                }
                return;
            }

            var uuid = _fileWriter.GetNextUniqueId();
            var packet = _fileWriter.GetPacket();
            var trackDescriptor = _fileWriter.GetTrackDescriptor();

            trackDescriptor.Uuid = uuid;
            trackDescriptor.Name = msg.Name;
            trackDescriptor.ParentUuid = _activityRootTrackId;
            trackDescriptor.ChildOrdering = TrackDescriptor.Types.ChildTracksOrdering.Chronological;

            packet.TrackDescriptor = trackDescriptor;
            _fileWriter.AddPacket(packet);

            _groups[msg.GroupId] = new GroupInfo(uuid, msg.Flags);
        }

        public void HandleMessage(int threadId, ref Protocol.RegisterMarker msg)
        {
            if (_fileWriter == null)
                return;
            
            var groupTrack = _activityRootTrackId;
            var groupCritical = false;
            if (_groups.TryGetValue(msg.GroupId, out var groupInfo))
            {
                groupTrack = groupInfo.TrackId;
                groupCritical = groupInfo.Flags.HasFlag(MarkerFlags.Critical);
            }

            _markers[msg.MarkerId] = new MarkerInfo(msg.Name, groupTrack, groupCritical, false, 0);
        }

        public void HandleMessage(int threadId, ref Protocol.ScopeBegin msg)
        {
            if (_fileWriter == null)
                return;
            
            if (_settings.DynamicScopes)
            {
                WriteFrameScope(threadId, Protocol.MessageType.ScopeBegin, ref msg);
            }
            else
            {
                BeginScope(threadId, ref msg);
            }
        }

        public void HandleMessage(int threadId, ref Protocol.ScopeEnd msg)
        {
            if (_fileWriter == null)
                return;
            
            if (_settings.DynamicScopes)
            {
                WriteFrameScope(threadId, Protocol.MessageType.ScopeEnd, ref msg);
            }
            else
            {
                EndScope(threadId, ref msg);
            }
        }

        public void HandleMessage(int threadId, ref Protocol.ActivityBegin msg)
        {
            if (_fileWriter == null)
                return;

            if (!_markers.TryGetValue(msg.MarkerId, out var markerInfo))
                return;

            if (!ShouldEmit(markerInfo.IsCritical))
                return;

            var name = MakeUniqueId(markerInfo.Name, msg.Param1, msg.Param2, msg.Param3, _stringBuilder);
            var activity = RegisterActivity(msg.UniqueId, name, markerInfo.GroupTrackId, out var internedData);
            
            var trackEvent = _fileWriter.GetTrackEvent();
            trackEvent.Type = TrackEvent.Types.Type.SliceBegin;
            trackEvent.NameIid = activity.InternId;
            trackEvent.TrackUuid = activity.TrackId;
            
            _fileWriter.AddTimestampEvent(trackEvent, msg.Timestamp, internedData, true);
        }

        public void HandleMessage(int threadId, ref Protocol.ActivityEnd msg)
        {
            if (_fileWriter == null)
                return;
            
            // Emit if the begin was emitted
            if (!_openActivities.Remove(msg.UniqueId, out var trackId))
                return;

            var trackEvent = _fileWriter.GetTrackEvent();
            trackEvent.Type = TrackEvent.Types.Type.SliceEnd;
            trackEvent.TrackUuid = trackId;

            _fileWriter.AddTimestampEvent(trackEvent, msg.Timestamp, null, false);
        }

        public void HandleMessage(int threadId, ref Protocol.Event msg)
        {
            if (_fileWriter == null)
                return;

            if (!_markers.TryGetValue(msg.MarkerId, out var markerInfo))
                return;

            if (!ShouldEmit(markerInfo.IsCritical))
                return;

            var name = MakeUniqueId(markerInfo.Name, msg.Param1, msg.Param2, msg.Param3, _stringBuilder);
            var internId = RegisterEventName(name, out var internedData);

            var trackEvent = _fileWriter.GetTrackEvent();
            trackEvent.Type = TrackEvent.Types.Type.Instant;
            trackEvent.NameIid = internId;
            trackEvent.TrackUuid = markerInfo.GroupTrackId;

            _fileWriter.AddTimestampEvent(trackEvent, msg.Timestamp, internedData, true);
        }

        public void HandleMessage(int threadId, ref Protocol.CounterSample msg)
        {
            if (_fileWriter == null)
                return;

            if (!_counters.TryGetValue(msg.MarkerId, out var info) && !RegisterCounter(msg.MarkerId, out info))
                return;

            if (!ShouldEmit(info.IsCritical))
                return;

            if (msg.Timestamp - info.LastSampleTimestamp < _countersSamplingRateTicks)
            {
                if (!info.HasValue || msg.Value > info.Value)
                    _counters[msg.MarkerId] = info with { HasValue = true, Value = msg.Value };
                return;
            }

            var value = info.HasValue ? Math.Max(info.Value, msg.Value) : msg.Value;
            
            _counters[msg.MarkerId] = info with { LastSampleTimestamp = msg.Timestamp, HasValue = false };
            
            var trackEvent = _fileWriter.GetTrackEvent();
            trackEvent.Type = TrackEvent.Types.Type.Counter;
            trackEvent.TrackUuid = info.TrackId;
            trackEvent.CounterValue = (long)value;
            
            _fileWriter.AddTimestampEvent(trackEvent, msg.Timestamp, null, false);
        }

        public void HandleMessage(int threadId, ref Protocol.Frame msg)
        {
            if (_fileWriter == null)
                return;

            // Budget is re-evaluated once per frame and applies until the next one.
            var fileSize = _fileWriter.FileSize;
            _criticalOnly = fileSize >= _settings.CriticalBudgetBytes;
            _hardStop = fileSize >= _settings.MaxFileSizeBytes;

            _frameIndex++;

            var startupFrame = _frameIndex <= _settings.StartupFramesToWrite;

            var spikeFrame = false;
            if (_settings.DynamicScopes && _lastFrameTimestamp != 0)
            {
                var frameTicks = msg.Timestamp - _lastFrameTimestamp;
                spikeFrame = _spikeDetector.IsSpike(frameTicks);
                _spikeDetector.Push(frameTicks);
            }

            if (_settings.DynamicScopes && !_hardStop && _frameRecorder is { IsValid: true } && (spikeFrame || startupFrame))
            {
                ReplayFrameBuffer();

                foreach (var threadScope in _threadTracks.Values)
                    threadScope.CloseScopes(msg.Timestamp);
            }

            _lastFrameTimestamp = msg.Timestamp;
            _frameRecorder?.Reset();
            _fileWriter.FlushTrace();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void WriteFrameScope<T>(int threadId, Protocol.MessageType msgType, ref T value) where T : unmanaged
        {
            if (_frameRecorder == null)
                return;
            
            var msgTypeInt = (int)msgType;
            _frameRecorder.Write(ref msgTypeInt);
            _frameRecorder.Write(ref threadId);
            _frameRecorder.Write(ref value);
        }

        private void ReplayFrameBuffer()
        {
            if (_frameRecorder == null || !_frameRecorder.IsValid)
                return;
            
            var position = 0;
            while (position < _frameRecorder.Position)
            {
                var msgType = 0;
                var threadId = 0;
                
                if (!_frameRecorder.Read(ref position, ref msgType))
                    break;
                
                if (!_frameRecorder.Read(ref position, ref threadId))
                    break;
                    
                switch ((Protocol.MessageType)msgType)
                {
                    case Protocol.MessageType.ScopeBegin:
                    {
                        Protocol.ScopeBegin msg = default;
                        if (_frameRecorder.Read(ref position, ref msg))
                            BeginScope(threadId, ref msg);
                        break;
                    }
                    case Protocol.MessageType.ScopeEnd:
                    {
                        Protocol.ScopeEnd msg = default;
                        if (_frameRecorder.Read(ref position, ref msg))
                            EndScope(threadId, ref msg);
                        break;
                    }
                    default:
                        _settings.Logger?.LogWarning($"Invalid message type {msgType} in the recorded frame scopes buffer.");
                        return;
                }
            }
        }
        
        internal PerfettoThreadScope GetOrCreateThreadScope(int threadId)
        {
            if (_threadTracks.TryGetValue(threadId, out var threadScope))
                return threadScope;

            threadScope = new PerfettoThreadScope(_fileWriter!, _settings.ProcessId, threadId, _scopeRootTrackId);
            _threadTracks[threadId] = threadScope;
            return threadScope;
        }

        private void BeginScope(int threadId, ref Protocol.ScopeBegin msg)
        {
            var threadScope = GetOrCreateThreadScope(threadId);

            // A dropped parent drops its whole subtree, so skip the lookup and interning.
            if (threadScope.IsDropping)
            {
                threadScope.Begin(msg.Timestamp, 0, null, false);
                return;
            }

            // One lookup yields both criticality and name; unknown markers emit unnamed.
            var found = _markers.TryGetValue(msg.MarkerId, out var markerInfo);
            if (!ShouldEmit(found && markerInfo.IsCritical))
            {
                threadScope.Begin(msg.Timestamp, 0, null, false);
                return;
            }

            ulong internId = 0;
            InternedData? internedData = null;
            if (found)
                internId = ResolveInternId(msg.MarkerId, markerInfo, out internedData);

            threadScope.Begin(msg.Timestamp, internId, internedData, true);
        }

        private void EndScope(int threadId, ref Protocol.ScopeEnd msg)
        {
            GetOrCreateThreadScope(threadId).End(msg.Timestamp);
        }

        private ulong ResolveInternId(uint markerId, MarkerInfo markerInfo, out InternedData? internedData)
        {
            if (markerInfo.Interned)
            {
                internedData = null;
                return markerInfo.InternId;
            }

            var uuid = _fileWriter!.GetNextUniqueId();
            internedData = _fileWriter.GetInternedData();

            var eventName = _fileWriter.GetEventName();
            eventName.Iid = uuid;
            eventName.Name = markerInfo.Name;

            internedData.EventNames.Add(eventName);

            _markers[markerId] = markerInfo with { Interned = true, InternId = uuid };
            return uuid;
        }
        
        private (ulong TrackId, ulong InternId) RegisterActivity(int uniqueId, string name, ulong parentTrackId, out InternedData internedData)
        {
            if (_activities.TryGetValue(name, out var info))
            {
                internedData = null!;
                _openActivities[uniqueId] = info.TrackId;
                return (info.TrackId, info.InternId);
            }

            var trackId = _fileWriter!.GetNextUniqueId();
            var packet = _fileWriter.GetPacket();
            var trackDescriptor = _fileWriter.GetTrackDescriptor();
            
            trackDescriptor.Uuid = trackId;
            trackDescriptor.Name = name;
            trackDescriptor.ParentUuid = parentTrackId;
            
            packet.TrackDescriptor = trackDescriptor;
            _fileWriter.AddPacket(packet);
            
            var internId = _fileWriter.GetNextUniqueId();
            internedData = _fileWriter.GetInternedData();
            
            var eventName = _fileWriter.GetEventName();
            eventName.Iid = internId;
            eventName.Name = name;
            
            internedData.EventNames.Add(eventName);

            _activities[name] = new ActivityInfo(trackId, internId);
            _openActivities[uniqueId] = trackId;
            return (trackId, internId);
        }

        private ulong RegisterEventName(string name, out InternedData? internedData)
        {
            if (_eventNames.TryGetValue(name, out var known))
            {
                internedData = null;
                return known;
            }

            var internId = _fileWriter!.GetNextUniqueId();
            internedData = _fileWriter.GetInternedData();

            var eventName = _fileWriter.GetEventName();
            eventName.Iid = internId;
            eventName.Name = name;

            internedData.EventNames.Add(eventName);

            _eventNames[name] = internId;
            return internId;
        }

        private bool RegisterCounter(uint markerId, out CounterInfo info)
        {
            if (!_markers.TryGetValue(markerId, out var markerInfo))
            {
                info = default;
                return false;
            }

            var uuid = _fileWriter!.GetNextUniqueId();
            var packet = _fileWriter.GetPacket();
            var trackDescriptor = _fileWriter.GetTrackDescriptor();
            
            trackDescriptor.Uuid = uuid;
            trackDescriptor.Name = $"{markerInfo.Name} Counter";
            trackDescriptor.ParentUuid = markerInfo.GroupTrackId;
            
            var counterDescriptor = _fileWriter.GetCounterDescriptor();
            counterDescriptor.Unit = CounterDescriptor.Types.Unit.Count;
            trackDescriptor.Counter = counterDescriptor;
            
            packet.TrackDescriptor = trackDescriptor;
            _fileWriter.AddPacket(packet);

            info = new CounterInfo(uuid, markerInfo.IsCritical, 0, false, 0);
            _counters[markerId] = info;
            return true;
        }
        
        private static string MakeUniqueId(string name, TypedAttribute param1, TypedAttribute param2, TypedAttribute param3, StringBuilder sb)
        {
            sb.Clear();

            // Special case if the first parameter is a string. For UX bring it first.
            if (param1.ParamType == TypedAttribute.ValueType.String)
            {
                sb.Append(param1.AsString);
                sb.Append(" - ");
                sb.Append(name);
            }
            else
            {
                sb.Append(name);
            }

            if (param1.ParamType != TypedAttribute.ValueType.None && param1.ParamType != TypedAttribute.ValueType.String)
            {
                sb.Append('[');
                AppendTypedAttribute(sb, param1);
                sb.Append(']');
            }
            if (param2.ParamType != TypedAttribute.ValueType.None)
            {
                sb.Append('[');
                AppendTypedAttribute(sb, param2);
                sb.Append(']');
            }
            if (param3.ParamType != TypedAttribute.ValueType.None)
            {
                sb.Append('[');
                AppendTypedAttribute(sb, param3);
                sb.Append(']');
            }

            return sb.ToString();
        }

        private static void AppendTypedAttribute(StringBuilder sb, TypedAttribute param)
        {
            switch (param.ParamType)
            {
                case TypedAttribute.ValueType.None:
                    break;
                case TypedAttribute.ValueType.Bool:
                    sb.Append(param.AsBool);
                    break;
                case TypedAttribute.ValueType.Int:
                    sb.Append(param.AsInt);
                    break;
                case TypedAttribute.ValueType.Float:
                    sb.Append(param.AsFloat.ToString(CultureInfo.InvariantCulture));
                    break;
                case TypedAttribute.ValueType.String:
                    sb.Append(param.AsString);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }
}