#nullable enable
using Perfetto.Protos;

namespace NanoProfiler
{
    /// <summary>Writes one thread's scope slices.</summary>
    internal class PerfettoThreadScope
    {
        private const int NOT_DROPPING = int.MaxValue;

        private readonly PerfettoFileWriter _fileWriter;
        private readonly ulong _trackId;

        private int _currentDepth;
        private int _dropDepth = NOT_DROPPING;

        public PerfettoThreadScope(PerfettoFileWriter fileWriter, int processId, int threadId, ulong rootTrackId)
        {
            _fileWriter = fileWriter;

            var threadName = threadId == 0 ? "Main Thread" : $"Thread {threadId}";
            _trackId = _fileWriter.AddThreadTrack(processId, threadId, threadName, rootTrackId);
        }

        public bool IsDropping => _dropDepth != NOT_DROPPING;

        public void Begin(long timestamp, ulong internId, InternedData? internedData, bool emit)
        {
            if (!emit)
            {
                if (_dropDepth == NOT_DROPPING)
                    _dropDepth = _currentDepth;
                _currentDepth++;
                return;
            }

            _currentDepth++;

            var trackEvent = _fileWriter.GetTrackEvent();
            trackEvent.Type = TrackEvent.Types.Type.SliceBegin;
            trackEvent.NameIid = internId;
            trackEvent.TrackUuid = _trackId;

            _fileWriter.AddTimestampEvent(trackEvent, timestamp, internedData, true);
        }

        public void End(long timestamp)
        {
            if (_currentDepth == 0)
                return;

            _currentDepth--;

            if (_dropDepth != NOT_DROPPING)
            {
                // Leaving the scope that opened the drop region ends it, dropped scopes emit no end.
                if (_currentDepth == _dropDepth)
                    _dropDepth = NOT_DROPPING;
                return;
            }

            var trackEvent = _fileWriter.GetTrackEvent();
            trackEvent.Type = TrackEvent.Types.Type.SliceEnd;
            trackEvent.TrackUuid = _trackId;

            _fileWriter.AddTimestampEvent(trackEvent, timestamp, null, false);
        }

        public void CloseScopes(long timestamp)
        {
            while (_currentDepth > 0)
                End(timestamp);
        }
    }
}
