using Perfetto.Protos;

namespace NanoProfiler
{
    /// <summary>Writes one thread's scope slices, dropping whole subtrees the budget rejects.</summary>
    internal class PerfettoThreadScope
    {
        private const int NotDropping = int.MaxValue;

        private readonly PerfettoFileWriter _fileWriter;
        private readonly ulong _trackId;

        private int _openDepth;
        // Root depth of the current dropped subtree. A thread track is a strict stack, so a dropped
        // scope must drop all its children too; tracking one depth keeps begins/ends balanced.
        private int _dropDepth = NotDropping;

        public PerfettoThreadScope(PerfettoFileWriter fileWriter, int processId, int threadId, ulong rootTrackId)
        {
            _fileWriter = fileWriter;

            var threadName = threadId == 0 ? "Main Thread" : $"Thread {threadId}";
            _trackId = _fileWriter.AddThreadTrack(processId, threadId, threadName, rootTrackId);
        }

        // Lets the caller skip the marker lookup and interning for scopes that will be dropped anyway.
        public bool IsDropping => _dropDepth != NotDropping;

        public void Begin(long timestamp, ulong internId, InternedData? internedData, bool emit)
        {
            if (!emit)
            {
                if (_dropDepth == NotDropping)
                    _dropDepth = _openDepth;
                _openDepth++;
                return;
            }

            _openDepth++;

            var trackEvent = _fileWriter.GetTrackEvent();
            trackEvent.Type = TrackEvent.Types.Type.SliceBegin;
            trackEvent.NameIid = internId;
            trackEvent.TrackUuid = _trackId;

            _fileWriter.AddTimestampEvent(trackEvent, timestamp, internedData, true);
        }

        public void End(long timestamp)
        {
            if (_openDepth == 0)
                return;

            _openDepth--;

            if (_dropDepth != NotDropping)
            {
                // Leaving the scope that opened the drop region ends it; dropped scopes emit no end.
                if (_openDepth == _dropDepth)
                    _dropDepth = NotDropping;
                return;
            }

            var trackEvent = _fileWriter.GetTrackEvent();
            trackEvent.Type = TrackEvent.Types.Type.SliceEnd;
            trackEvent.TrackUuid = _trackId;

            _fileWriter.AddTimestampEvent(trackEvent, timestamp, null, false);
        }

        public void CloseScopes(long timestamp)
        {
            while (_openDepth > 0)
                End(timestamp);
        }
    }
}
