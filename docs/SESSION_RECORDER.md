# SessionRecorder

## What it is

`SessionRecorder` is the per-frame CSV writer at the heart of every
Eye_lean recording. It pulls an `EyeFrameSample` from `EyeTracker` and
an `HmdFrameSample` from `HMDDataCollector` in `LateUpdate`, joins
them with session metadata, and writes a single row to disk. It also
owns the descriptive header block (`# Eye_lean Research Data Export`,
`# CoordinateOrigin`, `# Profile`, etc.), the coord-origin grace
window, custom metadata fields, and (since v1.3) the
`RegisterMetric(name, getter)` extensibility API.

## Audience

Developers extending the per-frame CSV writer or its plug-in metric
API.

## Prerequisites

- An Eye_lean scene (`SessionRecorder` is auto-bootstrapped).
- For `RegisterMetric`: a component with a per-frame numeric signal
  to expose.

## When you'd use it

You don't, directly — every Eye_lean scene auto-bootstraps one. You
call into its public API to:

- Set per-trial context on phase boundaries.
- Declare custom metadata columns (Block, Condition, etc.) up front.
- Register pluggable metric columns from any component (RIPA monitor,
  custom signals).
- Read the live CSV path / frame counter to drive sidecars.

## How to use it

```csharp
using EyeTracking.Components;

var rec = FindFirstObjectByType<SessionRecorder>();

// Per-trial context — populates TrialNumber / CurrentPhase / SubTask
// columns on every row from this point forward.
rec.SetSessionContext(trialNumber: 5, phase: "VisualSearch", config: "Block_A");

// Participant identity — written to the header metadata block.
rec.SetParticipantID("P_007");

// Custom metadata column — declare BEFORE recording starts.
rec.DeclareMetadataField("Condition", EyeLean.Data.MetadataValueType.String);
rec.SetMetadata("Condition", "Control");

// Live metric column. Getter is called per row.
rec.RegisterMetric("MyScore", () => CustomLogic.Score(), "F3");
```

## API reference

File: `Assets/Scripts/EyeTracking/Components/SessionRecorder.cs`

| Method / property | Line | Purpose |
|---|---|---|
| `SetSessionContext(trial, phase, config, subTask)` | ~676 | Bind per-frame context columns. |
| `SetSubTask(string)` | ~685 | Update only the `SubTask` column. |
| `SetParticipantID(string)` | ~691 | Write to header metadata. |
| `SetMetadata(name, string/int/float/bool)` | ~699–720 | Set custom metadata field value. |
| `DeclareMetadataField(name, type)` | ~735 | Pre-declare a metadata column (must be before header). |
| `RegisterMetric(name, Func<string>)` | 122 | Plug-in CSV column with string getter. |
| `RegisterMetric(name, Func<float>, format)` | 143 | Plug-in CSV column with float getter (default `F4`). |
| `UnregisterMetric(name)` | — | Remove a registered metric (only before header). |
| `RegisteredMetrics` | — | Read-only list of currently registered metric columns. |
| `CsvFilePath` (prop) | 170 | Path to the active CSV. |
| `FrameNumber` (prop) | 171 | Frame counter shared with sidecars. |
| `OnHeaderWritten` (event) | 173 | Fires exactly once when the column header lands. |
| `RecordingEnabled` (prop) | — | True iff data collection + CSV export are active. |

CSV header lines (always-on for v1.3+):
```
# Eye_lean Research Data Export
# FileVersion: 1.1
# SessionID: yyyymmdd_hhmmss_xxxxxxxx
# Timestamp: <UTC ISO-8601>
# Device: <SystemInfo.deviceModel>
# CoordinateOrigin: x.xxxx,y.xxxx,z.xxxx
# CoordinateOriginSet: True | False
# Profile: <ProfileName | "none">
# ProfileCombinedYawDeg: ±x.xxxx
# ProfileCombinedPitchDeg: ±x.xxxx
```

## How it integrates with the rest of the toolkit

- **Eye / head data** comes from sibling `EyeTracker` +
  `HMDDataCollector` GameObjects (auto-found in `Awake`).
- **Coordinate origin** is set by `HMDDataCollector.SetCoordinateOrigin`
  (typically called by the environment generator). Until origin
  lands, the recorder buffers samples in memory; once it lands the
  buffer is flushed and re-normalized so the CSV's
  `CoordinateOriginSet:True` claim is honest for every row.
- **Custom metadata** populated via `SetMetadata` is inserted between
  session-context columns and head-pose columns.
- **Registered metrics** are inserted between `DataSampleCount` and
  `GazedObjectName`. Order follows registration call order. The
  bootstrap-installed `RIPACSVColumn` contributes `LiveLoadIndex`
  (alias of the displayed detector, back-compat with v1.0–v1.3) plus
  per-detector columns `LiveLoadIndex_RIPA2`, `LiveLoadIndex_BW`,
  `LiveLoadIndex_BW_Raw`, `LiveLoadIndex_FFT`, `LiveLoadIndex_DWT`
  (v1.0.1+).
- **`SceneStateRecorder` + `SceneEventRecorder`** subscribe to
  `OnHeaderWritten` to lock their own schemas in lockstep.
- **`ReplayMode.IsActive` short-circuit:** during deterministic
  replay, `SessionRecorder.Start` notices replay mode and disables
  itself so live samples don't overwrite the recorded CSV.

## Common patterns + gotchas

- **Declare metadata fields BEFORE recording starts.** Adding a
  `DeclareMetadataField` after the header is locked is rejected with
  an error. Same for `RegisterMetric`.
- **`SetMetadata` after recording starts is OK** — the column already
  exists; the value is updated for subsequent rows. But adding a
  *new* field via `SetMetadata` after lock causes column-count
  mismatches; you'll see a warning and the column won't appear.
- **The header is deferred.** `SessionRecorder.Start` opens the CSV
  but doesn't write the header until either (a) the first sample
  lands AND `HMDDataCollector.HasTrialStartPosition` is true, or (b)
  the 2-second grace window expires. Use `OnHeaderWritten` to know
  when the schema is locked.
- **Pre-flush for Android crashes.** `OnApplicationPause` flushes
  buffered rows so an OS kill doesn't lose data.
- **CSV format is a contract.** Don't change `FormatDataSampleAsCSV`
  without coordinated update to `eyelean_analysis` Python loader.
  Bump `# FileVersion` if you do.

## References

- Source: `Assets/Scripts/EyeTracking/Components/SessionRecorder.cs`
- Tests: `Assets/Editor/Tests/SessionRecorderTests.cs`
- Schema: `docs/DATA_SCHEMA.md` (in Unity project) lists every column.
- Per-frame loop diagram: `docs/ARCHITECTURE.md` (in Unity project).
