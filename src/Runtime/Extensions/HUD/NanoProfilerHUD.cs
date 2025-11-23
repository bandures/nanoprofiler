#if UNITY_2022_3_OR_NEWER
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using Unity.Collections;
using UnityEngine;
using UnityEngine.PlayerLoop;
using UnityEngine.Rendering.Universal;
using Screen = UnityEngine.Device.Screen;

namespace Meowtron.NanoProfiler
{
    /// <summary>
    /// HUD interface for the in-game profiler
    /// </summary>
    public partial class NanoProfilerHUD: 
        INanoProcessor<NanoProtocol.CaptureStart>, 
        INanoProcessor<NanoProtocol.CaptureStop>, 
        INanoProcessor<NanoProtocol.Frame>, 
        INanoProcessor<NanoProtocol.RegisterMarker>, 
        INanoProcessor<NanoProtocol.ScopeBegin>, 
        INanoProcessor<NanoProtocol.ScopeEnd>, 
        INanoProcessor<NanoProtocol.CounterSample>
    {
        public enum ViewMode
        {
            None,
            Summary,
            Detailed,
            Game,
            Debug,
        }
        
        private const float BASIC_INFO_UPDATE_DELAY = 1.0f;
        private const string FRAME_TRANSFERRED_BYTES_ID = "NanoProfiler.FRAME_SENT_BYTES_ID";
        private const string FRAME_DISCARDED_BYTES_ID = "NanoProfiler.FRAME_DISCARDED_BYTES_ID";
        
        private class CounterHistory
        {
            public const int HISTORY_SIZE = 100;

            private long _lastTimestamp;
            
            public CounterHistory(string id, string label, float warningLevel, float criticalLevel, NanoUnit unitType)
            {
                Id = id;
                Label = label;
                WarningLevel = warningLevel;
                CriticalLevel = criticalLevel;
                UnitType = unitType;
                Values = new float[HISTORY_SIZE];
                
                _lastTimestamp = 0;
            }
            
            public string Id { get; }
            public string Label { get; }
            public float WarningLevel { get; }
            public float CriticalLevel { get; }
            public NanoUnit UnitType { get; }
            public float[] Values { get; }
            public ref long LastTimestamp => ref _lastTimestamp;
        }
        
        public record struct AssetDownloadingProgress
        {
            public float Progress;
            public string DownloadedItems;
            public string TotalItems;
            public string Item;
            public bool IsVisible => string.IsNullOrEmpty(DownloadedItems) == false && string.IsNullOrEmpty(TotalItems) == false;
        }
        
        private readonly IReadOnlyList<CounterHistory> _counters = new List<CounterHistory>
        {
            new(CommonMarkers.CPU_MAIN_THREAD_FRAME_MARKER_ID, "CPU MT", 33, 66, NanoUnit.Milliseconds),
            new(CommonMarkers.CPU_RENDER_THREAD_FRAME_MARKER_ID, "CPU RT", 33, 66, NanoUnit.Milliseconds),
            new(CommonMarkers.CPU_SOUND_DSP_MARKER_ID, "DSP", 33, 66, NanoUnit.Default),
            new(CommonMarkers.CPU_SOUND_UPDATE_MARKER_ID, "Sound", 33, 66, NanoUnit.Default),
            new(CommonMarkers.SOUND_CHANNELS_COUNT_MARKER_ID, "Channels", 16, 32, NanoUnit.Default),
            new(CommonMarkers.SOUND_VOICES_COUNT_MARKER_ID, "Voices", 32, 64, NanoUnit.Default),
            new(CommonMarkers.GPU_FRAME_MARKER_ID, "GPU", 33, 66, NanoUnit.Milliseconds),
            new(CommonMarkers.GFX_DRAW_CALLS_COUNT_MARKER_ID, "DrawCalls", 400, 600, NanoUnit.Default),
            new(CommonMarkers.GFX_TRIANGLES_COUNT_MARKER_ID, "Triangles", 160_000, 200_000, NanoUnit.Default),
            new(CommonMarkers.MEMORY_TOTAL_MARKER_ID, "Memory", 1_500_000_000, 2_500_000_000, NanoUnit.Default),
            new(CommonMarkers.LOGIC_MODEL_UPDATE_MARKER_ID, "Model", 6, 11, NanoUnit.Milliseconds),
            new(CommonMarkers.LOGIC_VIEW_UPDATE_MARKER_ID, "View", 1.5f, 3, NanoUnit.Milliseconds),
            new(CommonMarkers.LOGIC_UI_UPDATE_MARKER_ID, "UI", 1.5f, 3, NanoUnit.Milliseconds),
            new(FRAME_TRANSFERRED_BYTES_ID, "Sent Bytes", 0, 1024, NanoUnit.Default),
            new(FRAME_DISCARDED_BYTES_ID, "Discarded Bytes", 0, 1024, NanoUnit.Default),
        };

        private readonly NanoProfiler _profiler;

        private bool _disposed;
        private bool _profilerActive;
        private ViewMode _viewMode;
        private float _basicInfoLastUpdate;
        private int _errorsCount;
        private int _warningsCount;
        private NativeText _fpsString;
        private NativeText _memoryString;
        private NativeText _watermarkString;
        private NativeText _errorsAndWarningsString;
        private NativeText _downloadingProgressString;
        private NanoProfilerHUDFeature _hudFeature;

        private int _frameIndex;
        private readonly List<CounterHistory> _visibleCounters;
        private readonly Dictionary<uint, CounterHistory> _markerIdToCounter;

        private long _transferredBytesLastValue;
        private long _discardedBytesLastValue;
        private readonly CounterHistory _transferredBytesCounter;
        private readonly CounterHistory _discardedBytesCounter;
        private AssetDownloadingProgress _downloadingProgress;

        public NanoProfilerHUD(NanoProfiler profiler)
        {
            _profiler = profiler;
            _profilerActive = false;
            
            _disposed = false;
            _frameIndex = 0;
            _visibleCounters = new List<CounterHistory>();
            _markerIdToCounter = new Dictionary<uint, CounterHistory>();

            _transferredBytesLastValue = 0;
            _discardedBytesLastValue = 0;
            _transferredBytesCounter = GetCounterByName(FRAME_TRANSFERRED_BYTES_ID);
            _discardedBytesCounter = GetCounterByName(FRAME_DISCARDED_BYTES_ID);
            
            _viewMode = ViewMode.None;

            _fpsString = new NativeText(16, Allocator.Persistent);
            _memoryString = new NativeText(64, Allocator.Persistent);
            _watermarkString = new NativeText(0, Allocator.Persistent);
            _errorsAndWarningsString = new NativeText(32, Allocator.Persistent);
            _downloadingProgressString = new NativeText(64, Allocator.Persistent);
            
            HudLogCounter.OnLogEmitted += LogCounterCallback;
            UnityPlayerLoopTools.InjectSystemSimple(typeof(NanoProfilerHUD), typeof(Update), Update);
        }

        public bool ShowProfiler { get; set; }
        public bool ShowDebugInfo { get; set; }

        public void Dispose()
        {
            UnityPlayerLoopTools.RemoveSystemSimple(typeof(NanoProfilerHUD));
            HudLogCounter.OnLogEmitted -= LogCounterCallback;
            
            _fpsString.Dispose();
            _memoryString.Dispose();
            _watermarkString.Dispose();
            _errorsAndWarningsString.Dispose();
            _downloadingProgressString.Dispose();

            _disposed = true;
        }
        
        public string WatermarkLabel
        {
            get => _watermarkString.ToString();
            set
            {
                _watermarkString.Dispose();
                _watermarkString = new NativeText(value.Length, Allocator.Persistent);
                _watermarkString.Append(value);
            }
        }
        
        public ViewMode Mode
        {
            get => _viewMode;
            set
            {
                if (_viewMode == value)
                    return;
                
                HideAllCounters();
         
                ShowProfiler = true;
                _viewMode = value;
            
                switch (value)
                {
                    case ViewMode.Summary:
                        ShowCounter(CommonMarkers.CPU_MAIN_THREAD_FRAME_MARKER_ID);
                        ShowCounter(CommonMarkers.CPU_RENDER_THREAD_FRAME_MARKER_ID);
                        ShowCounter(CommonMarkers.GPU_FRAME_MARKER_ID);
                        ShowCounter(CommonMarkers.MEMORY_TOTAL_MARKER_ID);
                        break;
                    case ViewMode.Detailed:
                        ShowCounter(CommonMarkers.CPU_MAIN_THREAD_FRAME_MARKER_ID);
                        ShowCounter(CommonMarkers.CPU_RENDER_THREAD_FRAME_MARKER_ID);
                        ShowCounter(CommonMarkers.CPU_SOUND_DSP_MARKER_ID);
                        ShowCounter(CommonMarkers.CPU_SOUND_UPDATE_MARKER_ID);
                        ShowCounter(CommonMarkers.GPU_FRAME_MARKER_ID);
                        ShowCounter(CommonMarkers.GFX_DRAW_CALLS_COUNT_MARKER_ID);
                        ShowCounter(CommonMarkers.GFX_TRIANGLES_COUNT_MARKER_ID);
                        ShowCounter(CommonMarkers.MEMORY_TOTAL_MARKER_ID);
                        break;
                    case ViewMode.Game:
                        ShowCounter(CommonMarkers.LOGIC_MODEL_UPDATE_MARKER_ID);
                        ShowCounter(CommonMarkers.LOGIC_VIEW_UPDATE_MARKER_ID);
                        ShowCounter(CommonMarkers.LOGIC_UI_UPDATE_MARKER_ID);
                        break;
                    case ViewMode.Debug:
                        ShowCounter(CommonMarkers.CPU_MAIN_THREAD_FRAME_MARKER_ID);
                        ShowCounter(CommonMarkers.CPU_RENDER_THREAD_FRAME_MARKER_ID);
                        ShowCounter(CommonMarkers.GPU_FRAME_MARKER_ID);
                        ShowCounter(FRAME_TRANSFERRED_BYTES_ID);
                        ShowCounter(FRAME_DISCARDED_BYTES_ID);
                        break;
                    default:
                        ShowProfiler = false;
                        break;
                }
            }
        }
        
        public AssetDownloadingProgress DownloadingProgress { set => _downloadingProgress = value; }

        private void Draw()
        {
            if (!_hudFeature || !_hudFeature.IsReady())
                return;
            
            if (ShowDebugInfo)
                DrawBasicInfo();
            
            if (ShowProfiler && _profilerActive)
                DrawProfilerHud();
        }

        public void HandleMessage(int threadId, ref NanoProtocol.CaptureStart msg)
        {
            _profilerActive = true;
        }

        public void HandleMessage(int threadId, ref NanoProtocol.CaptureStop msg)
        {
            _profilerActive = false;
        }

        public void HandleMessage(int threadId, ref NanoProtocol.Frame msg)
        {
            ReportDataStatistics();
            
            _frameIndex++;
            if (_frameIndex >= CounterHistory.HISTORY_SIZE)
                _frameIndex = 0;

            // Reset values
            foreach (var counter in _counters)
                counter.Values[_frameIndex] = 0;
        }

        public void HandleMessage(int threadId, ref NanoProtocol.RegisterMarker msg)
        {
            var counter = GetCounterByName(msg.Name);
            if (counter != null)
                _markerIdToCounter[msg.MarkerId] = counter;
        }

        public void HandleMessage(int threadId, ref NanoProtocol.ScopeBegin msg)
        {
            if (!_markerIdToCounter.TryGetValue(msg.MarkerId, out var counter))
                return;

            counter.LastTimestamp = msg.Timestamp;
        }

        public void HandleMessage(int threadId, ref NanoProtocol.ScopeEnd msg)
        {
            if (!_markerIdToCounter.TryGetValue(msg.MarkerId, out var counter))
                return;

            counter.Values[_frameIndex] += (float)TimeHelper.TimestampToMilliseconds(msg.Timestamp - counter.LastTimestamp);
        }

        public void HandleMessage(int threadId, ref NanoProtocol.CounterSample msg)
        {
            if (!_markerIdToCounter.TryGetValue(msg.MarkerId, out var counter))
                return;

            counter.Values[_frameIndex] = msg.Value;
        }

        private void Update()
        {
            // We unsubscribe in Dispose(), but Unity might cache player loop and tick us one more time
            if (_disposed)
                return;
            
            if (!_hudFeature || !_hudFeature.IsReady())
                InitOnce();
            
            Draw();
        }
        
        private void InitOnce()
        {
            var renderer = UniversalRenderPipeline.asset;
            foreach (var feature in renderer.scriptableRendererData.rendererFeatures)
            {
                if (feature is NanoProfilerHUDFeature nanoProfilerHUDFeature)
                    _hudFeature = nanoProfilerHUDFeature;
            }
        }

        private void DrawBasicInfo()
        {
            var safeArea = Screen.safeArea;

            if (Time.realtimeSinceStartup - _basicInfoLastUpdate > BASIC_INFO_UPDATE_DELAY)
            {
                _basicInfoLastUpdate = Time.realtimeSinceStartup;
                
                // Memory counter
                long currentMono = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong();
                long maxMono = UnityEngine.Profiling.Profiler.GetMonoHeapSizeLong();
                long currentNative = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong();
                long maxNative = UnityEngine.Profiling.Profiler.GetTotalReservedMemoryLong();
                long currentTotal = currentMono + currentNative;
                long maxTotal = maxMono + maxNative;
                _memoryString.Clear();
                FormatValue(ref _memoryString, currentTotal);
                _memoryString.Append("/");
                FormatValue(ref _memoryString, maxTotal);

                // FPS counter
                var fpsValue = Time.timeScale > Mathf.Epsilon ? 1 / Time.smoothDeltaTime : 1 / Time.unscaledDeltaTime;
                _fpsString.Clear();
                _fpsString.Append(fpsValue >= 29.9 ? "^2" : fpsValue < 19.9 ? "^1" : "^3");
                FormatFloatNumber(ref _fpsString, 1 / Time.smoothDeltaTime, 1);
                
                // Errors & Warnings counters
                _errorsAndWarningsString.Clear();
                _errorsAndWarningsString.Append("^1");
                _errorsAndWarningsString.Append(_errorsCount);
                _errorsAndWarningsString.Append("^0|^3");
                _errorsAndWarningsString.Append(_warningsCount);

                if (_downloadingProgress.IsVisible)
                {
                    _downloadingProgressString.Clear();
                    _downloadingProgressString.Append("[");
                    _downloadingProgressString.Append($"{_downloadingProgress.Progress:P0}");
                    _downloadingProgressString.Append("|");
                    _downloadingProgressString.Append(_downloadingProgress.DownloadedItems);
                    _downloadingProgressString.Append("/");
                    _downloadingProgressString.Append(_downloadingProgress.TotalItems);

                    if (!string.IsNullOrEmpty(_downloadingProgress.Item))
                    {
                        _downloadingProgressString.Append("|");
                        _downloadingProgressString.Append(_downloadingProgress.Item);
                    }

                    _downloadingProgressString.Append("]");
                }
            }

            // Add labels in the bottom right corner of the screen
            int posY = (int)safeArea.yMax - NanoProfilerHUDFeature.CharHeight;
            int xMax = (int)safeArea.xMax;
#if DEVELOPMENT_BUILD
            xMax -= 100;
#endif
            _hudFeature.AddText(xMax - 200, posY, _watermarkString.AsReadOnly());
            _hudFeature.AddText(xMax - 500, posY, _memoryString.AsReadOnly());
            _hudFeature.AddText(xMax - 640, posY, _fpsString.AsReadOnly());
            _hudFeature.AddText(xMax - 780, posY, _errorsAndWarningsString.AsReadOnly());
            
            if (_downloadingProgress.IsVisible)
            {
                const int spaceAfterLastLabelInChars = 1;
                int offset = xMax - (780 + NanoProfilerHUDFeature.CharWidth * (spaceAfterLastLabelInChars + _downloadingProgressString.Length));
                _hudFeature.AddText(offset, posY, _downloadingProgressString.AsReadOnly());
            }
        }
        
        private void DrawProfilerHud()
        {
            // Background
            _hudFeature?.AddQuad(10, 10, 820, 20 + _visibleCounters.Count * 20, 7);
            
            // This is called on main thread, why collector thread might be
            // collecting values. As long as it's just Accumulator values -
            // we don't care.
            // Nb! Will need locks if any structural changes introduced.
            int sampleFrame = _frameIndex > 0 ? _frameIndex - 1 : CounterHistory.HISTORY_SIZE - 1;
            var builtString = new NativeText(512, Allocator.Temp);
            for (int i = 0; i < _visibleCounters.Count; i++)
            {
                CounterHistory counterHistory = _visibleCounters[i];
                
                // Build accumulator string
                builtString.Clear();
                builtString.Append(counterHistory.Label);
                builtString.Append(" ");
                if (counterHistory.UnitType == NanoUnit.Default)
                    FormatValue(ref builtString, counterHistory.Values[sampleFrame]);
                else
                    FormatTimeValue(ref builtString, counterHistory.Values[sampleFrame]);
                
                _hudFeature?.AddText(20, 10 + i * 20, builtString.AsReadOnly());
                _hudFeature?.AddSeries(320, 10 + i * 20 + 8, counterHistory.Values, sampleFrame, counterHistory.WarningLevel, counterHistory.CriticalLevel);
            }
            builtString.Dispose();
        }
        
        private void ShowCounter(string id)
        {
            var counter = GetCounterByName(id);
            if (counter == null)
                return;
            
            _visibleCounters.Add(counter);
        }

        private void HideAllCounters()
        {
            _visibleCounters.Clear();
        }

        private CounterHistory GetCounterByName(string id)
        {
            foreach (CounterHistory counter in _counters)
            {
                if (counter.Id == id)
                    return counter;
            }

            return null;
        }

        private void ReportDataStatistics()
        {
            _profiler.GetStatistics(out var transferredBytes, out var discardedBytes);
            _transferredBytesCounter.Values[_frameIndex] = transferredBytes - _transferredBytesLastValue;
            _discardedBytesCounter.Values[_frameIndex] = discardedBytes - _discardedBytesLastValue;
            _transferredBytesLastValue = transferredBytes;
            _discardedBytesLastValue = discardedBytes;
        }
        
        private static void FormatValue(ref NativeText builder, float value)
        {
            switch (value)
            {
                case >= 1_000_000_000:
                    FormatFloatNumber(ref builder, value / 1_000_000_000, 2, "G");
                    return;
                case >= 1_000_000:
                    FormatFloatNumber(ref builder, value / 1_000_000, 2, "M");
                    return;
                case >= 1000:
                    FormatFloatNumber(ref builder, value / 1000, 2, "k");
                    return;
                default:
                    FormatFloatNumber(ref builder, value);
                    return;
            }
        }
        
        private static void FormatTimeValue(ref NativeText builder, float value)
        {
            switch (value)
            {
                case >= 60 * 1000:
                    FormatFloatNumber(ref builder, value / (60 * 1000), 2, "m");
                    return;
                case >= 1000:
                    FormatFloatNumber(ref builder, value / 1000, 2, "s");
                    return;
                default:
                    FormatFloatNumber(ref builder, value, 2, "ms");
                    return;
            }
        }

        private static void FormatFloatNumber(ref NativeText builder, float value, int numbersAfterPoint = 2, string postfix = null)
        {
            var fixedPart = (int)value;
            builder.Append(fixedPart);

            if (numbersAfterPoint > 0)
                builder.Append('.');
            
            for (int i = 0; i < numbersAfterPoint; i++)
            {
                value *= 10;
                var digit = (int)value % 10;
                builder.Append(new Unicode.Rune('0' + digit));
            }
            
            if (postfix != null)
                builder.Append(postfix);
        }
        
        private static T GetNonPublicProperty<T>(object self, string fieldName)
        {
            var field = self.GetType().GetProperty(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            return (T)field?.GetValue(self);
        }
        
        private void LogCounterCallback(LogType type)
        {
            switch (type)
            {
                case LogType.Exception:
                case LogType.Error:
                    Interlocked.Increment(ref _errorsCount);
                    return;
                case LogType.Assert:
                case LogType.Warning:
                    Interlocked.Increment(ref _warningsCount);
                    return;
                default:
                    return;
            }
        }
    }
    
    public static class HudLogCounter
    {
        internal static event Action<LogType> OnLogEmitted;
        public static void ReportLog(LogType type) => OnLogEmitted?.Invoke(type);
    }
}
#endif
