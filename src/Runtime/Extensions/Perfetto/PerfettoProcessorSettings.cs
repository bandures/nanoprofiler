#nullable enable
using System;
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
    /// Threshold in milliseconds for triggering dynamic scope to be recorded
    /// in the output stream.
    /// </summary>
    public float DynamicScopesTriggerThresholdMs { get; init; } = 50.0f;
        
    /// <summary>
    /// Cooldown between dynamic scope recordings in milliseconds. Any frames
    /// recorded during the cooldown won't be recorded into the output stream.
    /// </summary>
    public float DynamicScopesCooldownMs { get; init; } = 2000.0f;
        
    /// <summary>
    /// Size of the buffer used to record dynamic scopes in bytes.
    /// </summary>
    public int DynamicScopesBufferSize { get; init; } = 1024 * 1024;
        
    /// <summary>
    /// Number of frames to be always written at the start of the capture,
    /// regardless of dynamic scopes settings. This is useful for startup profiling.
    /// </summary>
    public int StartupFramesToWrite { get; init; } = 20;
        
    public PerfettoProcessorSettings()
    {
    }
}