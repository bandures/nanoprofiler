# PerfettoProcessor — Budget-aware tracing, adaptive spike detection, and metadata

## Context

`PerfettoProcessor` (`src/Runtime/Extensions/Perfetto/PerfettoProcessor.cs`) is the only writer of
`.trace` files in NanoProfiler. AnimaClient (`D:\a2\AnimaClient\Assets\Client\Infrastructure\ClientProfiler.cs`)
wires it up per session and uploads the resulting file. The upload pipeline has a **hard 40 MB ceiling**
(larger files are rejected), but today the writer has no notion of file size and no way to triage what
to keep when the budget runs out.

## Evidence from `E:\__Traces`

Sampled real sessions with the Perfetto SQL tooling. `E:\__Traces` is the **pre-upload** set, so it
contains the oversized files that upload rejects.

| Trace | Size | Duration | Slices | Counters | Rate |
|---|---|---|---|---|---|
| samsungSMX210 | 385 KB | 4.6 min | 3,150 | 2,202 | ~5.0 MB/hr |
| iPhone171 (p50 size) | 1.18 MB | 16 min | 10,829 | 7,545 | ~4.4 MB/hr |
| iPhone172 | 1.88 MB | 2.5 hr | 17,000 | 17,312 | ~0.7 MB/hr |
| samsungSMX700 | 10.5 MB | 4.0 hr | 108,959 | 111,503 | ~2.6 MB/hr |
| Mac165 (healthy max) | 30.6 MB | 22.5 hr | 139,407 | 646,266 | ~1.4 MB/hr |
| **samsungSMG996B (runaway)** | **56 MB** | 22.2 hr | 605,513 | 617,813 | ~2.5 MB/hr |

Size distribution (partial scan, ~121K older files): p50 ≈ 196 KB, p90 ≈ 0.3 MB, p99 ≈ 6.8 MB,
p99.9 ≈ 20 MB, **max 74 MB**; 22 files ≥ 39 MB.

**Findings driving the design:**

1. **Sessions are bimodal** — median ~10–20 min, with a heavy tail of **20 h+ idle/background sessions**.
   Both the healthy 30 MB and the runaway 56 MB traces ran ~22 h. Duration, not activity, drives the
   worst cases.
2. **The steady-state floor is counters, not scopes.** 9 tracks at exactly 1/sec (`Used Heap`,
   `Total Memory`, `Draw Calls`, `GPU/CPU Frame Time`, …). Over 22 h that is ~15–20 MB even with zero
   spikes. Frame-time counters already **max-aggregate per second**, so spikes are already visible at 1 Hz.
3. **The runaway is a false-positive storm.** The absolute 50 ms threshold on a device that simply runs
   at ~60 ms makes *every* 2 s cooldown window end in a "spike": 32,319 scope-frames replayed vs a
   healthy 331 over an almost identical 22 h span (80,078 s ÷ 32,319 ≈ 2.48 s ≈ the cooldown). That is
   the entire 40 MB overage.

## Design decisions (confirmed)

- **Frame-time resolution:** keep the existing **1 Hz max-aggregated** frame-time counters. Do **not**
  add a denser/per-frame counter — at 30–60 fps it would be 3–7× the current counter volume and blow the
  budget on long sessions. Spikes remain visible via the 1 Hz max counter and fully detailed inside
  spike replays.
- **Loading suppression:** **short adaptive window, no cooldown**, no game-side signal. A sustained-slow
  region (loading/transition) stops triggering within ~half the window (default 8 frames) and re-arms
  just as fast, so the first spike after loading is caught. Residual: ~half-a-window of loading frames
  recorded and a ~half-a-window post-load blind spot (see §4). An explicit signal would remove both but
  was declined.
- **Hard cap:** plain **hard stop at 40 MB** — stop writing entirely so the file stays valid and
  uploadable. The tail of a 20 h+ session is sacrificed to guarantee acceptance.

## Design

### 1. Critical tagging via `MarkerFlags.Critical`

Add a bit to the enum and propagate it through the existing wire protocol — `RegisterMarker` and
`RegisterGroup` already carry `MarkerFlags` (`Protocol.cs:99,186`).

`src/Runtime/MarkerFlags.cs`:
```csharp
Critical = 1 << 3,   // Always kept in budget-restricted (critical-only) mode
```
(`1 << 1` is `RemoveFlag1`, so `1 << 3` is the next free bit.)

**Criticality is a Group property only.** `MarkerFlags.Critical` is honored solely on
`RegisterGroup`; markers, scopes, counters and activities **inherit** their group's criticality.
This keeps the hot path to a single cached bool (no per-event group resolution) and gives one
uniform rule for every primitive:
- `ScopeBegin/End`, `ActivityBegin/End`, `CounterSample` → critical iff their (marker's) group is critical.
- Track descriptors / `RegisterGroup` / `RegisterMarker` → always emitted (one-shot, tiny).

Consequence: to keep a primitive past the soft budget, put it in a group tagged
`MarkerFlags.Critical` (e.g. the essential counters group, state-transition scopes). Ungrouped work
is non-critical and is dropped in critical-only mode.

Extend the record structs (`PerfettoProcessor.cs`):
- `GroupInfo(ulong TrackId, bool IsCritical)` — `IsCritical` from `msg.Flags.HasFlag(MarkerFlags.Critical)`.
- `MarkerInfo(string Name, ulong GroupTrackId, bool IsCritical, ...)` — `IsCritical` inherited from group at register.
- `CounterInfo(ulong TrackId, bool IsCritical, ...)` — inherited from the marker's group at `RegisterCounter`.

The criticality read is folded into the lookup each path already performs (interning for scopes,
the marker lookup in `ActivityBegin`, the counter lookup in `CounterSample`) — no separate lookup.

### 2. Budget accounting in `PerfettoFileWriter`

Expose the flushed stream position directly:
```csharp
public long FileSize => _fileStream?.Position ?? 0;
```
No estimate of the buffered batch — the budget check may lag by at most the current frame's data,
so the file can overshoot a limit by one frame's worth. That's an accepted trade for a
zero-cost read.

### 3. Two budget regimes in `PerfettoProcessor`

Recompute once per frame at the top of `HandleMessage(Frame)`:
```csharp
var size = _fileWriter.FileSize;
_hardStop     = size >= _settings.MaxFileSizeBytes;      // 40 MB — drop everything
_criticalOnly = size >= _settings.CriticalBudgetBytes;   // 30 MB — drop non-critical
```
Single shared gate reused by every handler (DRY):
```csharp
private bool ShouldEmit(bool isCritical) => !_hardStop && (!_criticalOnly || isCritical);
```
Early-out in `ScopeBegin`, `ScopeEnd`, `CounterSample`, `ActivityBegin/End`. Two bool tests — hot-path
friendly. Dynamic-scope replay flows through the same emit path, so the per-message gate filters
replayed messages for free — no separate replay-budget logic.

**Well-formedness (scopes).** A thread track is a strict slice stack, so a scope can only be emitted if
its parent was. `PerfettoThreadScope` therefore drops **whole subtrees**: it records the depth of the
shallowest currently-dropped scope (`_dropDepth`) and, while inside that region, drops every scope
regardless of its own criticality — a critical child of a dropped parent is dropped too (it would
otherwise mis-nest). `Begin`/`End` track this with one integer (no per-scope mask, no depth cap), which
keeps begins/ends balanced at any nesting depth even as the budget mode flips. The processor reads
`threadScope.IsDropping` first and skips the marker lookup and interning for scopes that will be
dropped, so a dropped scope never creates a dangling interned name. `CloseScopes` at stop closes exactly
the emitted-open scopes.

**Well-formedness (activities).** Activities are async spans matched by `UniqueId`, not a stack. Begins
only enter `_openActivities` when emitted, so `ActivityEnd` emits iff its begin is still in the dict —
the presence acts as the balance signal (the analogue of the scope stack). Ends are **not** re-gated by
the current budget mode, so an activity opened before the soft limit still closes after crossing it, and
`ActivityEnd` mirrors scope-end at the hard cap (both close what they opened; new work is dropped at
begin).

New settings (`PerfettoProcessorSettings`):
```csharp
public long CriticalBudgetBytes { get; init; } = 30L * 1024 * 1024;
public long MaxFileSizeBytes    { get; init; } = 40L * 1024 * 1024;
```

### 4. Adaptive spike detection (new `SpikeDetector`)

Replace the absolute threshold (`PerfettoProcessor.cs:262`). A small, standalone, unit-testable type
holding a preallocated ring buffer — no per-frame allocation:
```csharp
internal sealed class SpikeDetector
{
    private readonly long[] _window;   // size = SpikeWindowFrames
    private readonly long _absoluteFloorTicks;
    private readonly float _ratio;
    private int _writeIdx, _filled;

    public bool IsSpike(long frameTicks);   // compares to max(floor, median * ratio)
    public void Push(long frameTicks);       // called AFTER IsSpike, so a spike can't poison its baseline
}
```
Baseline = **median** of the window (copy the filled slots to a preallocated scratch array,
`Array.Sort(array, 0, count)`, take the middle — allocation-free and netstandard2.1-safe; the window is
small so the per-frame sort is negligible). Median, not mean, so a single slow frame doesn't drag the
baseline up.

**The window is deliberately short (default 8)** so it reacts within ~half its length in *both*
directions — this is what makes it, not a cooldown, the mechanism that governs recording:
- Fast game (33 ms baseline) → spike ≥ 50 ms (absolute floor).
- Slow device / general FPS drop → baseline rises within ~4 frames, steady slow frames stop triggering
  (**kills the runaway**), while isolated spikes *above* that baseline are still caught.
- Loading (a few very long frames) → records ~half-a-window of frames then goes quiet; the loading frames
  age out within ~half a window, so the detector re-arms and the **first spike after loading is caught**.
- A single slow frame is 1/window of the samples, so it never shifts the median — every isolated spike is
  recorded.

Residual (inherent to a recent-frames baseline): ~half-a-window of loading frames are recorded, and there
is a ~half-a-window blind spot right after loading while the slow frames age out. Both shrink with the
window; a shorter window also makes the baseline noisier (raise `SpikeRatioMultiplier` to compensate).
The explicit `SuppressSpikes` signal (out of scope) is the only way to make this exact.

New settings:
```csharp
public int   SpikeWindowFrames    { get; init; } = 8;
public float SpikeRatioMultiplier { get; init; } = 1.5f;
```
**No cooldown.** The old `DynamicScopesCooldownMs` is removed: with the old absolute threshold the cooldown
was the only runaway guard, but a flat time cooldown re-armed through a loading screen and then masked the
first real spike after it. The short adaptive window now caps sustained slowness on its own, so every
spike relative to the current baseline is recorded. Startup-frames force-write is kept.

### 5. Metadata via `TraceAttributes`

**Channel choice (analysed against the full protocol in `D:\perfetto`):** the writable metadata channels
were traced through the real trace_processor importers. `ProcessDescriptor.process_labels` only lands in
`args` as `chrome.process_label[N]` (buried in the process Args tab, not on any info page — 0 references
in the bundled v53 UI). The `metadata` table is a fixed compile-time allowlist except for
`AppendDynamicMetadataLegacy`. The purpose-built channel is **`TraceAttributes`**
(`common/trace_attributes.proto`): arbitrary typed key/value, documented for *"properties of the host /
machine the trace was collected on"*, surfaced as **`trace_attribute.<key>`** in the metadata table and
the UI **Overview** section, queryable via
`SELECT * FROM metadata WHERE name GLOB 'trace_attribute.*'`. It is a standalone `TracePacket` field
(#126) — the smallest, cleanest binding to add.

**Version caveat:** `TraceAttributes` is *"Unreleased"* at Perfetto HEAD and absent from the pinned
embedded UI `src/PerfettoUI~/v53.0-c1bbc1652`. Using it requires bumping the bundled trace_processor/UI
to a post-v53 (HEAD) build. If the viewer must stay on v53, `ChromeEventBundle.metadata` (name/value →
`cr-<key>`, works today) is the drop-in fallback behind the identical caller API below — swapping it is a
single writer method.

**Proto bindings.** The repo carries a hand-trimmed binding subset; `TracePacket.cs`'s `Data` oneof only
exposes `track_event`/`track_descriptor`/`process_descriptor`, and there is no `TraceAttributes` binding.
Regenerate with protoc (same process that produced the existing `*.cs`) to add:
- `TraceAttributes` (message with nested `Attribute { key; oneof value { long_value; string_value } }`).
- `TracePacket.trace_attributes = 126` in the `Data` oneof.

**Settings** (`PerfettoProcessorSettings`) — caller-facing API is channel-independent:
```csharp
public IReadOnlyDictionary<string, string>? Metadata { get; init; } = null;
```

**Writer.** Add `PerfettoFileWriter.AddTraceAttributes(IReadOnlyDictionary<string,string> metadata)` that
emits **one** `TracePacket` carrying a `TraceAttributes` with one `Attribute { key = kv.Key,
string_value = kv.Value }` per entry. Called once from `HandleMessage(CaptureStart)` after the process
track. One-shot, negligible cost, no per-frame work — fully compatible with the budget/RAII design.
(String values keep the caller simple; a typed overload emitting `long_value` for numeric keys such as
`mem_mb`/`tier` is a trivial future addition.)

Caller supplies device tier/model, GPU, client version, session id, platform, memory. In the UI these
appear as `trace_attribute.device_tier`, `trace_attribute.gpu`, etc.

### 6. AnimaClient wiring (`ClientProfiler.cs`)

- Populate `PerfettoProcessorSettings.Metadata` from `SystemInfo`, `Telemetry`, `AppVersionInfo`,
  `Application.platform`.
- Tag genuinely-critical **groups** with `MarkerFlags.Critical` (e.g. state transitions, the essential
  counters group) so their markers/scopes/counters survive critical-only mode.

### 7. Hot-path CPU

The per-event work is kept minimal and allocation-free:
- **One marker lookup per scope begin.** Criticality and the interned name come from the same
  `_markers` lookup (`BeginScope`); interning only runs when the scope is actually emitted, so a
  dropped scope never creates a dangling interned name. Scope end needs no lookup.
- **Counters:** the sampling-rate throttle compares in ticks (`_countersSamplingRateTicks`) instead of
  converting to milliseconds each sample; the running max is written back only when it changes, not on
  every sample; criticality is read from `CounterInfo` (no marker lookup).
- **Spike detection** (median-of-window, one small sort) runs only when `DynamicScopes` is on.
- **Activity names:** `MakeUniqueId` appends to the shared `StringBuilder` directly instead of building
  interpolated substrings, removing the per-activity intermediate allocations.
- Scopes on the `DynamicScopes` path still buffer as raw bytes (no lookups) until a spike replays them.

## Files to modify

| File | Change |
|------|--------|
| `src/Runtime/MarkerFlags.cs` | `Critical = 1 << 3` |
| `src/Runtime/Extensions/Perfetto/PerfettoProcessorSettings.cs` | `CriticalBudgetBytes`, `MaxFileSizeBytes`, `SpikeWindowFrames`, `SpikeRatioMultiplier`, `Metadata` |
| `src/Runtime/Extensions/Perfetto/PerfettoFileWriter.cs` | `EstimatedFileSize`; `AddTraceAttributes(metadata)` |
| `src/Runtime/Extensions/Perfetto/SpikeDetector.cs` (new) | ring-buffer median spike detector |
| `src/Runtime/Extensions/Perfetto/PerfettoProcessor.cs` | criticality at register; `ShouldEmit` gate; `_criticalOnly`/`_hardStop`; `SpikeDetector` wiring; emit metadata at `CaptureStart` |
| `src/Runtime/Extensions/Perfetto/TraceAttributes.cs` (new, generated) | protoc binding for `TraceAttributes` |
| `src/Runtime/Extensions/Perfetto/TracePacket.cs` (regen) | add `trace_attributes = 126` to the `Data` oneof |
| `D:\a2\AnimaClient\...\ClientProfiler.cs` | populate `Metadata`; tag critical groups |

**Proto regeneration required** for metadata: add the `TraceAttributes` message binding and the
`TracePacket.trace_attributes` (#126) field via protoc (same toolchain as the existing generated `*.cs`).
Requires a post-v53 (HEAD) trace_processor/UI to render; else fall back to `ChromeEventBundle.metadata`
behind the same caller API (see §5).

## Verification

1. **Regression:** run `tests/NanoProfiler.Tests` — all protocol changes are additive.
2. **`SpikeDetector` unit tests:** median correctness; floor vs ratio; spike not poisoning baseline;
   ring wraparound.
3. **Slow-game runaway (the key case):** 60 ms frames × ~2000 with hundreds of nested scopes,
   DynamicScopes on → scope-frames captured drops from ~thousands to a handful; file stays < 30 MB.
4. **Fast-game + spike:** 33 ms frames with a 200 ms frame every ~3 s → spike fully replayed, file small.
5. **Budget:** feed > 40 MB worth of events → output ≤ 40 MB, valid, and shows only critical tracks past
   30 MB, nothing past 40 MB.
6. **Perfetto UI sanity (post-v53 build):** `trace_attribute.*` rows visible in the Overview section and
   via `SELECT * FROM metadata WHERE name GLOB 'trace_attribute.*'`; counters continuous; critical-only
   segment shows only critical tracks.

## Out of scope (follow-up)

- Per-thread `TrustedPacketSequenceId` + `TracePacketDefaults` (default `track_uuid` → ~5–10 % smaller
  non-budget traces).
- Delta timestamps on `TrackEvent`.
- Counter-rate throttling past the soft limit (extend long-session capture before the hard stop).
- Per-signature spike dedup (beyond the adaptive baseline).
- Explicit `SuppressSpikes` group flag / API to make loading suppression exact (no recorded loading
  frames, no post-load blind spot).
- Explicit game-driven loading suppression (deferred — adaptive baseline chosen).
