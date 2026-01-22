#nullable enable
using System;
using System.IO;
using System.Runtime.CompilerServices;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using Perfetto.Protos;
using Trace = Perfetto.Protos.Trace;

namespace NanoProfiler
{
    public class PerfettoFileWriter : IDisposable
    {
        private const int MAX_CACHED_PACKETS = 32;
        private static uint _nextUniqueSequenceId;
        
        private readonly uint _trustedPacketSequenceId;
        private readonly string _fileName;
        private readonly ILogger? _logger;
        
        private FileStream? _fileStream;
        private readonly Trace _trace;

        private readonly PerfettoObjectPool<TracePacket> _packetPool;
        private readonly PerfettoObjectPool<TrackEvent> _trackEventPool;
        private readonly PerfettoObjectPool<InternedData> _internedDataPool;
        private readonly PerfettoObjectPool<EventName> _eventNamePool;
        private readonly PerfettoObjectPool<TrackDescriptor> _trackDescriptorPool;
        private readonly PerfettoObjectPool<CounterDescriptor> _counterDescriptorPool;
        private readonly PerfettoObjectPool<ProcessDescriptor> _processDescriptorPool;
        private readonly PerfettoObjectPool<ThreadDescriptor> _threadDescriptorPool;

        static PerfettoFileWriter()
        {
            _nextUniqueSequenceId = 1000;
        }
        
        public PerfettoFileWriter(string fileName, ILogger? logger, uint trustedPacketSequenceId)
        {
            _fileName = fileName;
            _logger = logger;
            _trustedPacketSequenceId = trustedPacketSequenceId;
            _trace = new Trace();
            IsFirstPacketInSequence = true;
            
            _packetPool = new PerfettoObjectPool<TracePacket>(1000);
            _trackEventPool = new PerfettoObjectPool<TrackEvent>(1000);
            _internedDataPool = new PerfettoObjectPool<InternedData>(100);
            _eventNamePool = new PerfettoObjectPool<EventName>(100);
            _trackDescriptorPool = new PerfettoObjectPool<TrackDescriptor>(100);
            _counterDescriptorPool = new PerfettoObjectPool<CounterDescriptor>(30);
            _processDescriptorPool = new PerfettoObjectPool<ProcessDescriptor>(2);
            _threadDescriptorPool = new PerfettoObjectPool<ThreadDescriptor>(10);
        }

        public bool IsFirstPacketInSequence { get; set; }

        public TracePacket GetPacket() => _packetPool.Get();
        public TrackEvent GetTrackEvent() => _trackEventPool.Get();
        public InternedData GetInternedData() => _internedDataPool.Get();
        public EventName GetEventName() => _eventNamePool.Get();
        public TrackDescriptor GetTrackDescriptor() => _trackDescriptorPool.Get();
        public CounterDescriptor GetCounterDescriptor() => _counterDescriptorPool.Get();
        public ProcessDescriptor GetProcessDescriptor() => _processDescriptorPool.Get();
        public ThreadDescriptor GetThreadDescriptor() => _threadDescriptorPool.Get();

        public void SetupStream(bool appending)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_fileName)!);
            try
            {
                var fileMode = appending ? FileMode.Append : FileMode.Create;
                _fileStream = new FileStream(_fileName, fileMode, FileAccess.Write, FileShare.None);
            }
            catch (IOException e)
            {
                _logger?.LogWarning("Perfetto tracer failed to open file {0}. Reason: {1}", _fileName, e.Message);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddPacket(TracePacket packet)
        {
            if (_fileStream == null)
                return;
            
            _trace.Packet.Add(packet);
            if (_trace.Packet.Count > MAX_CACHED_PACKETS)
                FlushTrace();
        }

        public void FlushTrace()
        {
            if (_trace.Packet?.Count == 0 || _fileStream == null)
                return;
            
            _trace.WriteTo(_fileStream);

            if (_trace.Packet == null)
                return;
            
            foreach (var packet in _trace.Packet)
                ReturnPacket(packet);
            _trace.Packet.Clear();
        }

        public ulong AddProcessTrack(int processId, string processName)
        {
            var trackId = GetNextUniqueId();
            
            var processDescriptor = GetProcessDescriptor();
            processDescriptor.Pid = processId;
            processDescriptor.ProcessName = processName;
            processDescriptor.ChromeProcessType = ProcessDescriptor.Types.ChromeProcessType.ProcessBrowser;
            
            var trackDescriptor = GetTrackDescriptor();
            trackDescriptor.Uuid = trackId;
            trackDescriptor.ChildOrdering = TrackDescriptor.Types.ChildTracksOrdering.Chronological;
            trackDescriptor.Process = processDescriptor;
            trackDescriptor.Name = processName;
            
            var packet = GetPacket();
            packet.TrackDescriptor = trackDescriptor;
            AddPacket(packet);
            return trackId;
        }

        public ulong AddTrack(string name, ulong parentUuid = 0)
        {
            var trackId = GetNextUniqueId();
            
            var trackDescriptor = GetTrackDescriptor();
            trackDescriptor.Uuid = trackId;
            trackDescriptor.Name = name;
            trackDescriptor.ChildOrdering = TrackDescriptor.Types.ChildTracksOrdering.Chronological;
            if (parentUuid != 0)
                trackDescriptor.ParentUuid = parentUuid;
            
            var packet = GetPacket();
            packet.TrackDescriptor = trackDescriptor;
            AddPacket(packet);
            return trackId;
        }

        public ulong AddThreadTrack(int processId, int threadId, string name, ulong parentUuid)
        {
            var trackId = GetNextUniqueId();

            var threadDescriptor = GetThreadDescriptor();
            threadDescriptor.Pid = processId;
            threadDescriptor.Tid = threadId;
            threadDescriptor.ThreadName = name;
            
            var trackDescriptor = GetTrackDescriptor();
            trackDescriptor.Uuid = trackId;
            trackDescriptor.Name = name;
            trackDescriptor.Thread = threadDescriptor;
            trackDescriptor.ChildOrdering = TrackDescriptor.Types.ChildTracksOrdering.Chronological;
            if (parentUuid != 0)
                trackDescriptor.ParentUuid = parentUuid;
            
            var packet = GetPacket();
            packet.TrackDescriptor = trackDescriptor;
            AddPacket(packet);
            return trackId;
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddTimestampEvent(TrackEvent trackEvent, long timestamp, InternedData? internedData, bool useInterned)
        {
            var firstPacket = IsFirstPacketInSequence;
            if (firstPacket)
                IsFirstPacketInSequence = false;
            
            var flags = (uint)TracePacket.Types.SequenceFlags.SeqUnspecified;
            if (useInterned)
            {
                flags |= (uint)TracePacket.Types.SequenceFlags.SeqNeedsIncrementalState;
                if (firstPacket)
                    flags |= (uint)TracePacket.Types.SequenceFlags.SeqIncrementalStateCleared;
            }

            var packet = GetPacket();
            packet.Timestamp = (ulong)TimeHelper.TimestampToNanoseconds(timestamp);
            packet.TrustedPacketSequenceId = _trustedPacketSequenceId;
            packet.TrackEvent = trackEvent;
            packet.InternedData = internedData;
            packet.SequenceFlags = flags;
            packet.FirstPacketOnSequence = firstPacket;
            
            AddPacket(packet);
        }
        
        public void Dispose()
        {
            FlushTrace();
            _fileStream?.Flush(true);
            _fileStream?.Dispose();
            _fileStream = null;
        }
        
        public ulong GetNextUniqueId()
        {
            _nextUniqueSequenceId++;
            return _nextUniqueSequenceId;
        }

        private void ReturnPacket(TracePacket? packet)
        {
            if (packet == null)
                return;

            ReturnTrackEvent(packet.TrackEvent);
            packet.TrackEvent = null;

            ReturnInternedData(packet.InternedData);
            packet.InternedData = null;

            ReturnTrackDescriptor(packet.TrackDescriptor);
            packet.TrackDescriptor = null;
            
            packet.ClearTimestamp();
            packet.ClearTimestampClockId();
            packet.ClearTrustedPacketSequenceId();
            packet.ClearSequenceFlags();
            packet.ClearFirstPacketOnSequence();
            packet.ClearData();
            
            _packetPool.Return(packet);
        }

        private void ReturnTrackEvent(TrackEvent? trackEvent)
        {
            if (trackEvent == null)
                return;
            
            trackEvent.ClearType();
            trackEvent.ClearTrackUuid();
            trackEvent.ClearNameIid();
            trackEvent.ClearName();
            trackEvent.ClearCounterValue();
            trackEvent.ClearDoubleCounterValue();
            
            _trackEventPool.Return(trackEvent);
        }

        private void ReturnTrackDescriptor(TrackDescriptor? trackDescriptor)
        {
            if (trackDescriptor == null)
                return;
            
            trackDescriptor.ClearUuid();
            trackDescriptor.ClearParentUuid();
            trackDescriptor.ClearName();
            trackDescriptor.ClearChildOrdering();
            
            if (trackDescriptor.Process != null)
            {
                trackDescriptor.Process.ClearPid();
                trackDescriptor.Process.ClearProcessName();
                _processDescriptorPool.Return(trackDescriptor.Process);
                trackDescriptor.Process = null;
            }
            
            if (trackDescriptor.Thread != null)
            {
                trackDescriptor.Thread.ClearPid();
                trackDescriptor.Thread.ClearTid();
                trackDescriptor.Thread.ClearThreadName();
                _threadDescriptorPool.Return(trackDescriptor.Thread);
                trackDescriptor.Thread = null;
            }
            
            if (trackDescriptor.Counter != null)
            {
                trackDescriptor.Counter.ClearUnit();
                _counterDescriptorPool.Return(trackDescriptor.Counter);
                trackDescriptor.Counter = null;
            }
            
            _trackDescriptorPool.Return(trackDescriptor);
        }
        
        private void ReturnInternedData(InternedData? internedData)
        {
            if (internedData == null)
                return;
            
            if (internedData.EventNames != null)
            {
                foreach (var eventName in internedData.EventNames)
                {
                    if (eventName == null)
                        continue;
                    
                    eventName.ClearIid();
                    eventName.ClearName();
                    _eventNamePool.Return(eventName);
                }
                internedData.EventNames.Clear();
            }
            
            _internedDataPool.Return(internedData);
        }
    }
}

