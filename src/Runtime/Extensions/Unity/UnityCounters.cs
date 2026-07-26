#if UNITY_2022_3_OR_NEWER
using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.PlayerLoop;

namespace NanoProfiler
{
    /// <summary>
    /// Profiler service which adds CPU, GPU and other
    /// Unity counters to the profiler stream. 
    /// </summary>
    public sealed class UnityCounters : IProfilerService
    {
        /// Unity counters that are replicated into the in-game profiler
        private class UnityCounter
        {
            private readonly CounterUnit _unitType;
            private readonly Counter _counter;
            private ProfilerRecorder _recorder;
            
            public UnityCounter(Profiler profiler, string id, CounterUnit unitType, ProfilerCategory category, string recorderId = null)
            {
                _unitType = unitType;
                _counter = profiler.CreateCounter(id);
                _recorder = new ProfilerRecorder(category, recorderId ?? id);
            }
            
            public void Start() => _recorder.Start();
            public void Stop() => _recorder.Stop();

            public void Sample()
            {
                var value = _recorder.LastValue;
                if (_unitType == CounterUnit.Milliseconds)
                    _counter.Sample((float)value / TimeHelper.NANOSECONDS_PER_MILLISECOND);
                else
                    _counter.Sample(value);
            }
        }

        private readonly Counter _mainThreadFrameCounter;
        private readonly Counter _renderThreadFrameCounter;
        private readonly Counter _gpuFrameCounter;
        private readonly Counter _soundUpdateUsageCounter;
        private readonly Counter _soundDSPUsageCounter;
        private readonly Counter _soundChannelsCountCounter;
        private readonly Counter _soundVoicesCounter;
        private readonly List<UnityCounter> _unityCounters = new();
        private readonly FrameTiming[] _frameTimings = new FrameTiming[1];

        public UnityCounters(Profiler profiler)
        {
            _mainThreadFrameCounter = profiler.CreateCounter(CommonMarkers.CPU_MAIN_THREAD_FRAME_MARKER_ID);
            _renderThreadFrameCounter = profiler.CreateCounter(CommonMarkers.CPU_RENDER_THREAD_FRAME_MARKER_ID);
            _gpuFrameCounter = profiler.CreateCounter(CommonMarkers.GPU_FRAME_MARKER_ID);
            _soundUpdateUsageCounter = profiler.CreateCounter(CommonMarkers.CPU_SOUND_UPDATE_MARKER_ID);
            _soundDSPUsageCounter = profiler.CreateCounter(CommonMarkers.CPU_SOUND_DSP_MARKER_ID);
            _soundChannelsCountCounter = profiler.CreateCounter(CommonMarkers.SOUND_CHANNELS_COUNT_MARKER_ID);
            _soundVoicesCounter = profiler.CreateCounter(CommonMarkers.SOUND_VOICES_COUNT_MARKER_ID);
            
            _unityCounters.AddRange(new List<UnityCounter>()
            {
                // This is changed to FTM direct API instead, due to Unity recorders stop reporting data.
                // It seems that Unity recorders are deactivated? Needs more investigation.
//                new(CPU_MAIN_THREAD_FRAME_MARKER_ID, CounterUnit.Milliseconds, ProfilerCategory.Scripts),
//                new(CPU_RENDER_THREAD_FRAME_MARKER_ID, CounterUnit.Milliseconds, ProfilerCategory.Scripts),
//                new(GPU_FRAME_MARKER_ID, CounterUnit.Milliseconds, ProfilerCategory.Render),
                new(profiler, CommonMarkers.MEMORY_TOTAL_MARKER_ID, CounterUnit.Bytes, ProfilerCategory.Scripts, "App Committed Memory"),
                new(profiler, CommonMarkers.MEMORY_GC_RESERVED_MARKER_ID, CounterUnit.Bytes, ProfilerCategory.Scripts, "GC Reserved Memory"),
                new(profiler, CommonMarkers.MEMORY_GC_USED_MARKER_ID, CounterUnit.Bytes, ProfilerCategory.Scripts, "GC Used Memory"),
                new(profiler, CommonMarkers.MEMORY_OBJECT_COUNT_MARKER_ID, CounterUnit.Default, ProfilerCategory.Memory),
                new(profiler, CommonMarkers.GFX_DRAW_CALLS_COUNT_MARKER_ID, CounterUnit.Default, ProfilerCategory.Render),
                new(profiler, CommonMarkers.GFX_TRIANGLES_COUNT_MARKER_ID, CounterUnit.Default, ProfilerCategory.Render),
            });
        }
        
        public void Start()
        {
            foreach (var counter in _unityCounters)
                counter.Start();

            UnityPlayerLoopTools.InjectSystemSimple(typeof(UnityCounters), typeof(Update), Update);
        }

        public void Stop()
        {
            UnityPlayerLoopTools.RemoveSystemSimple(typeof(UnityCounters));
            
            foreach (var counter in _unityCounters)
                counter.Stop();
        }
        
        private void Update()
        {
            if (!Application.isPlaying)
                return;
            
            foreach (var counter in _unityCounters)
                counter.Sample();

            if (FrameTimingManager.IsFeatureEnabled())
            {
                FrameTimingManager.CaptureFrameTimings();
                uint framesCount = FrameTimingManager.GetLatestTimings((uint)_frameTimings.Length, _frameTimings);
                if (framesCount > 0)
                {
                    var frame = _frameTimings[0];
                    _mainThreadFrameCounter.Sample((float)frame.cpuFrameTime);
                    _renderThreadFrameCounter.Sample((float)frame.cpuRenderThreadFrameTime);
                    _gpuFrameCounter.Sample((float)frame.gpuFrameTime);
                }
            }
        }
    }
}
#endif