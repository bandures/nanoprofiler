#nullable enable
using System;

namespace NanoProfiler
{
    /// <summary>Flags a frame as a spike relative to the recent baseline of frame durations.</summary>
    // Median (not mean) of the window, so a single hitch can't inflate the baseline. Allocation-free.
    internal sealed class SpikeDetector
    {
        private readonly long[] _window;
        private readonly long[] _scratch;
        private readonly long _absoluteFloorTicks;
        private readonly float _ratio;

        private int _writeIndex;
        private int _filled;

        public SpikeDetector(int windowFrames, long absoluteFloorTicks, float ratio)
        {
            if (windowFrames < 1)
                throw new ArgumentOutOfRangeException(nameof(windowFrames));

            _window = new long[windowFrames];
            _scratch = new long[windowFrames];
            _absoluteFloorTicks = absoluteFloorTicks;
            _ratio = ratio;
            _writeIndex = 0;
            _filled = 0;
        }

        // Does not mutate the baseline; call Push afterwards so a spike can't poison its own baseline.
        public bool IsSpike(long frameTicks)
        {
            return frameTicks > ThresholdTicks();
        }

        public void Push(long frameTicks)
        {
            _window[_writeIndex] = frameTicks;
            _writeIndex++;
            if (_writeIndex >= _window.Length)
                _writeIndex = 0;
            if (_filled < _window.Length)
                _filled++;
        }

        public void Reset()
        {
            _writeIndex = 0;
            _filled = 0;
        }

        // max(absolute floor, median * ratio); floor only until the first frame is recorded.
        internal long ThresholdTicks()
        {
            if (_filled == 0)
                return _absoluteFloorTicks;

            var relative = (long)(MedianTicks() * _ratio);
            return Math.Max(_absoluteFloorTicks, relative);
        }

        private long MedianTicks()
        {
            // Valid samples always occupy indices [0, _filled) of the ring buffer.
            Array.Copy(_window, 0, _scratch, 0, _filled);
            Array.Sort(_scratch, 0, _filled);
            return _scratch[_filled / 2];
        }
    }
}
