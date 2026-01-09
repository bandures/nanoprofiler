namespace NanoProfiler
{
    public readonly partial struct Counter
    {
        internal Counter(Profiler profiler, string name, uint groupId, MarkerFlags flags = MarkerFlags.Default)
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