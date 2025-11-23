namespace Meowtron.NanoProfiler
{
    public readonly partial struct NanoCounter
    {
        internal NanoCounter(NanoProfiler profiler, string name, uint groupId, NanoMarkerFlags flags = NanoMarkerFlags.Default)
        {
            _profiler = profiler.Proxy;
            _id = profiler.RegisterMarker(name, groupId, flags);
            _groupId = groupId;
        }

        public void Sample(float value)
        {
            if (!_profiler.IsGroupEnabled(_groupId))
                return;
            _profiler.CurrentThread.EmitCounterSample(_id, value);
        }
    }
}