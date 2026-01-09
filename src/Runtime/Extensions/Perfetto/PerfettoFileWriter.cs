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
        private Trace _trace;

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
        }

        public bool IsFirstPacketInSequence { get; set; }
        
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
                return;
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
            _trace = new Trace();
        }

        public ulong AddProcessTrack(int processId, string? processName)
        {
            var trackId = GetNextUniqueId();
            AddPacket(new TracePacket
            {
                TrackDescriptor = new TrackDescriptor
                {
                    Uuid = trackId,
                    ChildOrdering = TrackDescriptor.Types.ChildTracksOrdering.Chronological,
                    Process = new ProcessDescriptor
                    {
                        Pid = processId,
                        ProcessName = processName ?? "Process"
                    }
                }
            });
            return trackId;
        }

        public ulong AddTrack(string name)
        {
            var trackId = GetNextUniqueId();
            AddPacket(new TracePacket
            {
                TrackDescriptor = new TrackDescriptor
                {
                    Uuid = trackId,
                    ChildOrdering = TrackDescriptor.Types.ChildTracksOrdering.Chronological,
                    Name = name,
                }
            });
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
            
            AddPacket(new TracePacket
            {
                Timestamp = (ulong)TimeHelper.TimestampToNanoseconds(timestamp),
                TrustedPacketSequenceId = _trustedPacketSequenceId,
                TrackEvent = trackEvent,
                InternedData = internedData,
                SequenceFlags = flags,
                FirstPacketOnSequence = firstPacket
            });
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
    }
}

