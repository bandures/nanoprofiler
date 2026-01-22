using Perfetto.Protos;

namespace NanoProfiler
{
    internal class PerfettoThreadScope
    {
        private readonly PerfettoProcessor _processor;
        private readonly PerfettoFileWriter _fileWriter;
        private readonly ulong _trackId;
        private int _depth;
        
        public PerfettoThreadScope(PerfettoProcessor processor, PerfettoFileWriter fileWriter, int processId, int threadId, ulong rootTrackId)
        {
            _processor = processor;
            _fileWriter = fileWriter;
            _depth = 0;
            
            var threadName = threadId == 0 ? "Main Thread" : $"Thread {threadId}";
            _trackId = _fileWriter.AddThreadTrack(processId, threadId, threadName, rootTrackId);
        }

        public void Begin(ref Protocol.ScopeBegin msg)
        {
            var uuid = _processor.GetOrCreateInternedData(msg.MarkerId, null, out var internedData);
                
            var trackEvent = _fileWriter.GetTrackEvent();
            trackEvent.Type = TrackEvent.Types.Type.SliceBegin;
            trackEvent.NameIid = uuid;
            trackEvent.TrackUuid = _trackId;
                
            _fileWriter.AddTimestampEvent(trackEvent, msg.Timestamp, internedData, true);

            _depth++;
        }
        
        public void End(ref Protocol.ScopeEnd msg)
        {
            var trackEvent = _fileWriter.GetTrackEvent();
            trackEvent.Type = TrackEvent.Types.Type.SliceEnd;
            trackEvent.TrackUuid = _trackId;

            _fileWriter.AddTimestampEvent(trackEvent, msg.Timestamp, null, false);

            _depth--;
        }

        public void FrameEnd(long timestamp)
        {
            var msg = new Protocol.ScopeEnd() { Timestamp = timestamp };
            while (_depth > 0)
                End(ref msg);

            _depth = 0;
        }
    }
}