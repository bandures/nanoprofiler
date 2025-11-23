# NanoProfiler
NanoProfiler - profiler for Unity games that works in release builds. It is designed to be lightweight, has minimal runtime overhead and not affect Unity Main thread. It also has ready integrations with Unity and Perfetto so that 


## Use Cases
ANRs investigation
Getting insights from wide variety of devices
Detailed stats from real users
Profiling async activity

## API

```
// Build settings
NanoProfilerSettingsBuilder settingsBuilder = new NanoProfilerSettingsBuilder()
    .WithAutoStart(true)
    .AddUnityCounters()
    .AddUnityPlayerLoop()
    .AddProcessor(_ => new NanoProfilerTelemetry())
    .EnableGroups({"Main"});

// Initialize
NanoProfiler profiler = new NanoProfiler(settingsBuilder.Build());

// For synchronous functions
NanoScope marker = profiler.CreateScope("YourScopeName");
using var _ = marker.Auto();

// For async functions
NanoActivity activity = profiler.CreateActivity("YourActivityName");
using var _ = activity.Auto();
using var _ = activity.Auto(1234, "StringParam");

// For counter values
NanoCounter counter = profiler.CreateCounter("YourCounterName");
counter.Sample(12345);

// For larger projects with many markers for activation/deactivation
NanoGroup group = profiler.CreateGroup("YourGroupName");
var scope = group.CreateScope("YourScopeInAGroup");
var activity = group.CreateActivity("YourActivityInAGroup");
var counter = group.CreateCounter("YourCounterInAGroup");
```

- NanoScope - should be use when the measured section is local and synchronous. Scope is hierarchical and will show nested scope markers within hierarchy. However, it also mean that any child scopes should end before parent scope ends.
- NanoActivity - could be used to measure sections which is longer than a frame or asynchronous. It doesn’t provide hierarchy, but that also means you can nest activities even if nested activity will end after any activity higher in the call chain.
- NanoCounter -  is for numeric gauge values, which can go up and down.
- NanoGroup - allows disabling/enabling a group of scopes, activities and counters.

## Implementation details

Each thread that uses the profiler has a per-thread object - `NanoThreadLocalProfiler`. `NanoThreadLocalProfiler` is responsible for collecting all information on its thread. That is needed for two reasons:
- Minimize overheads by offloading any processing to another thread.
- Thread safety. All user-faced objects (Scope, Counter, etc.) should be safe on any thread without synchronization primitives. Synchronization kills performance, so all interactions are thread-local.

![Implementation diagram.](/docs/scheme.png)

`NanoThreadLocalProfiler` collects all events, stores them in a ring buffer, and sends a signal to the Collector that there is more data. There are two types of messages:
- Regular - for standard data messages, which can be discarded if ring buffer is full
- Guaranteed - for system messages that can’t be dropped

When `NanoThreadLocalProfiler` is running out of ring buffer space, it signals the Collector to free space ASAP. If the ring buffer runs out of space, regular messages are dropped, and blocking messages stop execution until the Collector frees more space.

`NanoCollector` sits on a separate thread and collects data from ring buffers from all threads. Collected data is sent to all registered `NanoProcessors` for final processing. As an example, processor might serialize data to a file or show HUD graphs.

`NanoThreadLocalProfiler`, `NanoCollector`, and `NanoProcessors` exchange data using internal events protocol, which allows saving events inplace in the ring buffer without allocations. Typical events are:
- Scope begin event
- Scope end event
- Frame end event
- New marker
- etc

