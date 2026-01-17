#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Microsoft.Extensions.Logging;
using Perfetto.Protos;

namespace NanoProfiler
{
    public struct PerfettoProcessorSettings
    {
        /// <summary>
        /// File name the trace will saved to. Should have .trace extension.
        /// </summary>
        public string FileName { get; init; } = string.Empty;
        /// <summary>
        /// Optional process ID to be recorded in the trace.
        /// </summary>
        public int ProcessId { get; init; } = 0;
        /// <summary>
        /// Optional process name to be recorded in the trace.
        /// </summary>
        public string ProcessName { get; init; } = string.Empty;
        /// <summary>
        /// Optimization to reduce the number of samples recorded and the final file size.
        /// If set, samples will be recorded no more frequently than the specified rate.
        /// </summary>
        public TimeSpan CountersSamplingRate { get; init; } = TimeSpan.FromSeconds(1);
        /// <summary>
        /// Logger the processor can use to log warnings and errors.
        /// </summary>
        public ILogger? Logger { get; init; } = null;
        /// <summary>
        /// Scope events aren't logged unless certain criterial is reached.
        /// This is useful to avoid logging a lot of events unless they are important.
        /// F.e. Only log if the frame rate drops below a certain threshold.
        /// </summary>
        public bool DynamicScopes { get; init; } = false;
        public float DynamicScopesTriggerThresholdMs { get; init; } = 50.0f;
        public float DynamicScopesCooldownMs { get; init; } = 2000.0f;
        public int DynamicScopesBufferSize { get; init; } = 1024 * 1024;
        public int StartupFramesToWrite { get; init; } = 20;
        
        public PerfettoProcessorSettings()
        {
        }
    }
    
    public sealed class PerfettoProcessor : 
        IProcessor<Protocol.CaptureStart>,
        IProcessor<Protocol.CaptureStop>,
        IProcessor<Protocol.RegisterMarker>,
        IProcessor<Protocol.RegisterGroup>,
        IProcessor<Protocol.ScopeBegin>,
        IProcessor<Protocol.ScopeEnd>,
        IProcessor<Protocol.ActivityBegin>,
        IProcessor<Protocol.ActivityEnd>,
        IProcessor<Protocol.CounterSample>,
        IProcessor<Protocol.Frame>
    {
        private record struct GroupInfo(ulong TrackId);
        private record struct MarkerInfo(string Name, ulong GroupTrackId, bool Interned, ulong InternId);
        private record struct ActivityInfo(ulong TrackId, ulong InternId);
        private record struct CounterInfo(ulong TrackId, long LastSampleTimestamp, bool HasValue, double Value);
        
        private readonly PerfettoProcessorSettings _settings;
        private readonly Dictionary<uint, GroupInfo> _groups = new();
        private readonly Dictionary<uint, MarkerInfo> _markers = new();
        private readonly Dictionary<string, ActivityInfo> _activities = new();
        private readonly Dictionary<int, ulong> _openActivities = new();
        private readonly Dictionary<uint, CounterInfo> _counters = new();
        private readonly Dictionary<int, ulong> _threadTracks = new();
        private readonly long _dynamicScopesTriggerThresholdTicks;
        private readonly long _dynamicScopesCooldownTicks;
        private readonly StringBuilder _stringBuilder = new(256);
        
        private static readonly uint TrustedPacketSequenceId;
        
        private PerfettoFrameRecorder? _frameRecorder;
        private PerfettoFileWriter? _fileWriter;
        private ulong _scopeRootTrackId;
        private ulong _activityRootTrackId;
        private long _lastFrameTimestamp;
        private long _frameRecorderDisableUntilTimestamp;
        private int _frameIndex;

        static PerfettoProcessor()
        {
            // Unique ID that needs to be the same for all trace packets
            TrustedPacketSequenceId = (uint)DateTime.UtcNow.Ticks;
        }
        
        public PerfettoProcessor(PerfettoProcessorSettings settings)
        {
            _settings = settings;
            _fileWriter = null;
            _frameRecorder = null;
            _dynamicScopesTriggerThresholdTicks = TimeHelper.MillisecondsToTimestamp(_settings.DynamicScopesTriggerThresholdMs);
            _dynamicScopesCooldownTicks = TimeHelper.MillisecondsToTimestamp(_settings.DynamicScopesCooldownMs);
        }

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
            
            if (_settings.DynamicScopes)
                _frameRecorder = new PerfettoFrameRecorder(_settings.DynamicScopesBufferSize, _settings.Logger);
        }
        
        public void HandleMessage(int threadId, ref Protocol.CaptureStop msg)
        {
            _fileWriter?.Dispose();
            _fileWriter = null;
            _frameRecorder?.Dispose();
            _frameRecorder = null;
            _lastFrameTimestamp = 0; 
            _scopeRootTrackId = 0;
            _activityRootTrackId = 0;
            
            _groups.Clear();
            _markers.Clear();
            _activities.Clear();
            _openActivities.Clear();
            _counters.Clear();
            _threadTracks.Clear();
        }

        public void HandleMessage(int threadId, ref Protocol.RegisterGroup msg)
        {
            if (_fileWriter == null)
                return;
            if (_groups.ContainsKey(msg.GroupId))
                return;

            var uuid = _fileWriter.GetNextUniqueId();
            var packet = _fileWriter.GetPacket();
            var trackDescriptor = _fileWriter.GetTrackDescriptor();
            
            trackDescriptor.Uuid = uuid;
            trackDescriptor.Name = msg.Name;
            trackDescriptor.ParentUuid = _activityRootTrackId;
            trackDescriptor.ChildOrdering = TrackDescriptor.Types.ChildTracksOrdering.Chronological;
            
            packet.TrackDescriptor = trackDescriptor;
            _fileWriter.AddPacket(packet);

            _groups[msg.GroupId] = new GroupInfo(uuid);
        }

        public void HandleMessage(int threadId, ref Protocol.RegisterMarker msg)
        {
            if (_fileWriter == null)
                return;
            
            var groupTrack = _activityRootTrackId;
            if (_groups.TryGetValue(msg.GroupId, out var groupInfo))
                groupTrack = groupInfo.TrackId;
            
            _markers[msg.MarkerId] = new MarkerInfo(msg.Name, groupTrack, false, 0);
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
                var uuid = GetOrCreateInternedData(msg.MarkerId, null, out var internedData);
                var trackId = GetOrCreateThreadTrack(threadId);
                
                var trackEvent = _fileWriter.GetTrackEvent();
                trackEvent.Type = TrackEvent.Types.Type.SliceBegin;
                trackEvent.NameIid = uuid;
                trackEvent.TrackUuid = trackId;
                
                _fileWriter.AddTimestampEvent(trackEvent, msg.Timestamp, internedData, true);
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
                var trackId = GetOrCreateThreadTrack(threadId);
                
                var trackEvent = _fileWriter.GetTrackEvent();
                trackEvent.Type = TrackEvent.Types.Type.SliceEnd;
                trackEvent.TrackUuid = trackId;

                _fileWriter.AddTimestampEvent(trackEvent, msg.Timestamp, null, false);
            }
        }
        
        public void HandleMessage(int threadId, ref Protocol.ActivityBegin msg)
        {
            if (_fileWriter == null)
                return;
            
            if (!_markers.TryGetValue(msg.MarkerId, out var markerInfo))
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
            
            if (!_openActivities.Remove(msg.UniqueId, out var trackId))
                return;
            
            var trackEvent = _fileWriter.GetTrackEvent();
            trackEvent.Type = TrackEvent.Types.Type.SliceEnd;
            trackEvent.TrackUuid = trackId;
            
            _fileWriter.AddTimestampEvent(trackEvent, msg.Timestamp, null, false);
        }

        public void HandleMessage(int threadId, ref Protocol.CounterSample msg)
        {
            if (_fileWriter == null)
                return;
            
            if (!_counters.TryGetValue(msg.MarkerId, out var info) && !RegisterCounter(msg.MarkerId, out info))
                return;

            // We take the maximum value recorded during the sampling period
            var value = info.HasValue ? Math.Max(info.Value, msg.Value) : msg.Value;
            
            var elapsedMs = TimeHelper.TimestampToMilliseconds(msg.Timestamp - info.LastSampleTimestamp);
            if (elapsedMs < _settings.CountersSamplingRate.TotalMilliseconds)
            {
                _counters[msg.MarkerId] = info with { HasValue = true, Value = value };
                return;
            }
            
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
            
            _frameIndex++;

            var startupFrame = _frameIndex <= _settings.StartupFramesToWrite;
            var spikeFrame = msg.Timestamp - _lastFrameTimestamp > _dynamicScopesTriggerThresholdTicks;
            if (_settings.DynamicScopes && 
                _frameRecorder is { IsValid: true } && 
                (spikeFrame && msg.Timestamp > _frameRecorderDisableUntilTimestamp || startupFrame))
            {
                ReplayFrameBuffer();
                _frameRecorderDisableUntilTimestamp = msg.Timestamp + _dynamicScopesCooldownTicks;
            }
            
            _lastFrameTimestamp = msg.Timestamp;
            _frameRecorder?.Reset();
            _fileWriter.FlushTrace();
        }

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
                        {
                            var uuid = GetOrCreateInternedData(msg.MarkerId, null, out var internedData);
                            var trackId = GetOrCreateThreadTrack(threadId);
                            
                            var trackEvent = _fileWriter!.GetTrackEvent();
                            trackEvent.Type = TrackEvent.Types.Type.SliceBegin;
                            trackEvent.NameIid = uuid;
                            trackEvent.TrackUuid = trackId;
                            
                            _fileWriter!.AddTimestampEvent(trackEvent, msg.Timestamp, internedData, true);
                        }
                        break;
                    }
                    case Protocol.MessageType.ScopeEnd:
                    {
                        Protocol.ScopeEnd msg = default;
                        if (_frameRecorder.Read(ref position, ref msg))
                        {
                            var trackId = GetOrCreateThreadTrack(threadId);
                            
                            var trackEvent = _fileWriter!.GetTrackEvent();
                            trackEvent.Type = TrackEvent.Types.Type.SliceEnd;
                            trackEvent.TrackUuid = trackId;
                            
                            _fileWriter!.AddTimestampEvent(trackEvent, msg.Timestamp, null, false);
                        }
                        break;
                    }
                    default:
                        _settings.Logger?.LogWarning($"Invalid message type {msgType} in the recorded frame scopes buffer.");
                        return;
                }
            }
        }
        
        private ulong GetOrCreateThreadTrack(int threadId)
        {
            if (_threadTracks.TryGetValue(threadId, out var trackId))
                return trackId;
            
            trackId = _fileWriter!.AddThreadTrack(threadId, $"Thread {threadId}", _scopeRootTrackId);
            _threadTracks[threadId] = trackId;
            return trackId;
        }

        private ulong GetOrCreateInternedData(in uint markerId, in string? customName, out InternedData? internedData)
        {
            if (!_markers.TryGetValue(markerId, out var markerInfo))
            {
                internedData = null;
                return 0;
            }

            if (markerInfo.Interned)
            {
                internedData = null;
                return markerInfo.InternId;
            }
             
            var uuid = _fileWriter!.GetNextUniqueId();
            internedData = _fileWriter.GetInternedData();
            
            var eventName = _fileWriter.GetEventName();
            eventName.Iid = uuid;
            eventName.Name = customName ?? markerInfo.Name;
            
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

            info = new CounterInfo(uuid, 0, false, 0);
            _counters[markerId] = info;
            return true;
        }
        
        private static string MakeUniqueId(string name, TypedAttribute param1, TypedAttribute param2, TypedAttribute param3, StringBuilder sb)
        {
            sb.Clear();
            
            // Special case if the first parameter is a string. For UX bring it first than
            sb.Append(param1.ParamType == TypedAttribute.ValueType.String ? $"{param1.AsString} - {name}" : name);

            if (param1.ParamType != TypedAttribute.ValueType.None && param1.ParamType != TypedAttribute.ValueType.String)
                sb.Append($"[{FormatTypedAttribute(param1)}");
            if (param2.ParamType != TypedAttribute.ValueType.None)
                sb.Append($"[{FormatTypedAttribute(param2)}]");
            if (param3.ParamType != TypedAttribute.ValueType.None)
                sb.Append($"[{FormatTypedAttribute(param3)}]");
            
            return sb.ToString();
        }

        private static string FormatTypedAttribute(TypedAttribute param)
        {
            switch (param.ParamType)
            {
                case TypedAttribute.ValueType.None:
                    return string.Empty;
                case TypedAttribute.ValueType.Bool:
                    return param.AsBool.ToString();
                case TypedAttribute.ValueType.Int:
                    return param.AsInt.ToString();
                case TypedAttribute.ValueType.Float:
                    return param.AsFloat.ToString(CultureInfo.InvariantCulture);
                case TypedAttribute.ValueType.String:
                    return param.AsString;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }
}