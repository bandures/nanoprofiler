namespace NanoProfiler
{
    /// <summary>
    /// Performance counter to report float value to the Profiler
    /// </summary>
    public readonly partial struct Counter
    {
        private readonly uint _id;
        private readonly uint _groupId;
        private readonly ProfilerProxy _profiler;
    }
}
