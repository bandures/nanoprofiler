namespace Meowtron.NanoProfiler
{
    /// <summary>
    /// Performance counter to report float value to the Profiler
    /// </summary>
    public readonly partial struct NanoCounter
    {
        private readonly uint _id;
        private readonly uint _groupId;
        private readonly NanoProfilerProxy _profiler;
    }
}
