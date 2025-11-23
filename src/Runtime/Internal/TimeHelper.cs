using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Meowtron.NanoProfiler
{
    public static class TimeHelper
    {
        private static readonly double NanosecondsPerTick;
        public const long NANOSECONDS_PER_MICROSECOND = 1000L;
        public const long NANOSECONDS_PER_MILLISECOND = 1000L * 1000L;

        static TimeHelper()
        {
            NanosecondsPerTick = 1_000_000_000.0 / Stopwatch.Frequency;
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double TimestampToNanoseconds(long timestamp)
        {
            return timestamp * NanosecondsPerTick;
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double TimestampToMicroseconds(long timestamp)
        {
            return timestamp * NanosecondsPerTick / NANOSECONDS_PER_MICROSECOND;
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double TimestampToMilliseconds(long timestamp)
        {
            return timestamp * NanosecondsPerTick / NANOSECONDS_PER_MILLISECOND;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double NanosecondsToMilliseconds(double nanoseconds)
        {
            return nanoseconds / NANOSECONDS_PER_MILLISECOND;
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long MillisecondsToTimestamp(double milliseconds)
        {
            return (long)(milliseconds * NANOSECONDS_PER_MILLISECOND / NanosecondsPerTick);
        }
    }
}