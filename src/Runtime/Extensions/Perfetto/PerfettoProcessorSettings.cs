#nullable enable
using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace NanoProfiler;

public struct PerfettoProcessorSettings
{
    /// <summary>
    /// File name the trace will saved to. Should have .trace extension.
    /// </summary>
    public string FileName { get; init; } = string.Empty;
        
    /// <summary>
    /// Optional process ID to be recorded in the trace.
    /// </summary>
    public int ProcessId { get; init; } = 0;
        
    /// <summary>
    /// Optional process name to be recorded in the trace.
    /// </summary>
    public string ProcessName { get; init; } = "Process";
        
    /// <summary>
    /// Optimization to reduce the number of samples recorded and the final file size.
    /// If set, samples will be recorded no more frequently than the specified rate.
    /// </summary>
    public TimeSpan CountersSamplingRate { get; init; } = TimeSpan.FromSeconds(1);
        
    /// <summary>
    /// Logger the processor can use to log warnings and errors.
    /// </summary>
    public ILogger? Logger { get; init; } = null;
        
    /// <summary>
    /// Scope events aren't logged unless certain criterial is reached.
    /// This is useful to avoid logging a lot of events unless they are important.
    /// F.e. Only log if the frame rate drops below a certain threshold.
    /// </summary>
    public bool DynamicScopes { get; init; } = false;
        
    /// <summary>
    /// Absolute floor in milliseconds below which a frame is never a spike, regardless
    /// of the adaptive baseline. The spike threshold is max(this, baseline median * ratio).
    /// </summary>
    public float SpikeMinimumMs { get; init; } = 50.0f;
        
    /// <summary>
    /// Size of the buffer used to record dynamic scopes in bytes.
    /// </summary>
    public int DynamicScopesBufferSize { get; init; } = 1024 * 1024;
        
    /// <summary>
    /// Number of frames to be always written at the start of the capture,
    /// regardless of dynamic scopes settings. This is useful for startup profiling.
    /// </summary>
    public int StartupFramesToWrite { get; init; } = 20;

    /// <summary>
    /// Soft budget in bytes: above it only events whose group is flagged
    /// <see cref="MarkerFlags.Critical"/> are kept. Must be below <see cref="MaxFileSizeBytes"/>.
    /// </summary>
    public long CriticalBudgetBytes { get; init; } = 30L * 1024 * 1024;

    /// <summary>
    /// Hard budget in bytes: above it nothing is written, so the file stays valid and
    /// uploadable. The tail of very long sessions is lost in exchange.
    /// </summary>
    public long MaxFileSizeBytes { get; init; } = 40L * 1024 * 1024;

    /// <summary>
    /// Frames of rolling baseline for adaptive spike detection. Kept short so the baseline
    /// reacts within ~half its length; larger is steadier but slower to adapt.
    /// </summary>
    public int SpikeWindowFrames { get; init; } = 8;

    /// <summary>
    /// Multiplier on the baseline median frame time for the spike threshold, so naturally
    /// slow devices don't flag every frame.
    /// </summary>
    public float SpikeRatioMultiplier { get; init; } = 1.5f;

    /// <summary>
    /// Optional free-form capture metadata (device, GPU, version, ...).
    /// </summary>
    public IReadOnlyDictionary<string, string>? Metadata { get; init; } = null;

    public PerfettoProcessorSettings()
    {
    }
}