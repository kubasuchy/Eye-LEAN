# Custom Metadata Tutorial

## What it is

Eye_lean's `SessionRecorder` writes one CSV row per frame with a fixed
set of built-in columns (timing, head pose, gaze, vergence, object
intersections, performance). Custom metadata is the API for adding
your own per-trial or per-session columns to that CSV — experimental
condition, block number, response correctness, stimulus ID, anything
your analysis needs joined to the gaze stream. Fields are declared
once at scene start and updated by name during the experiment; values
land in every subsequent data row.

## Audience

Researchers extending Eye_lean recordings with custom per-trial or
per-session columns.

## Prerequisites

- Unity 6000.3.9f1, VIVE OpenXR 2.5.1 (or compliant OpenXR runtime).
- A scene with `EyeTracker`, `HMDDataCollector`, and `SessionRecorder`
  on the rig (the CalibrationScene and SampleExperiment scenes already
  meet this; the Skeleton template auto-bootstraps them).
- Familiarity with the recorder lifecycle in
  [`SESSION_RECORDER.md`](../../docs/SESSION_RECORDER.md).

---

## How custom metadata works

The lifecycle has three stages:

1. **Declare** — `SessionRecorder.DeclareMetadataField(name, type)`
   reserves a column. Must run before the CSV header is written.
2. **Set** — `SessionRecorder.SetMetadata(name, value)` updates the
   current value. Subsequent CSV rows carry that value until the next
   `SetMetadata` call.
3. **Lock** — when the first sample lands and the coordinate-origin
   grace window resolves, `SessionRecorder` writes the header and locks
   the schema (`Assets/Scripts/EyeTracking/Components/SessionRecorder.cs:379`).
   New `DeclareMetadataField` calls after lock are rejected with an
   error log; new field names passed to `SetMetadata` log a warning
   and the column does not appear.

Columns appear in CSV in declaration order, between the session-context
block (`SessionConfig`, `IsDebugMode`) and the head-pose block
(`HeadPos_X`).

```csharp
using EyeTracking.Components;
using EyeLean.Data;

[SerializeField] private SessionRecorder recorder;

void Awake()
{
    recorder.DeclareMetadataField("Condition",   MetadataValueType.String);
    recorder.DeclareMetadataField("BlockNumber", MetadataValueType.Int);
    recorder.DeclareMetadataField("Difficulty",  MetadataValueType.Float);
    recorder.DeclareMetadataField("IsCorrect",   MetadataValueType.Bool);
}

void StartTrial(int block, string condition)
{
    recorder.SetMetadata("Condition",   condition);
    recorder.SetMetadata("BlockNumber", block);
}
```

---

## Quick start: add your first custom field

This procedure adds a single `Condition` column and verifies it lands
in the CSV.

1. Open the experiment scene (`CalibrationScene` or your own).
2. Select the GameObject carrying the `SessionRecorder` component.
   Confirm the Inspector shows the `SessionRecorder` script. Note the
   reference for code wiring.
3. In the experiment-controller script that runs `Awake`, declare the
   field before the recorder writes its header:

   ```csharp
   using EyeTracking.Components;
   using EyeLean.Data;

   public class MyController : MonoBehaviour
   {
       [SerializeField] private SessionRecorder recorder;

       void Awake()
       {
           if (recorder == null)
               recorder = FindFirstObjectByType<SessionRecorder>();
           recorder.DeclareMetadataField("Condition", MetadataValueType.String);
       }
   }
   ```
4. Set the value when the participant's condition is known (typically
   `Start` or trial-start):

   ```csharp
   void Start()
   {
       recorder.SetMetadata("Condition", "Experimental");
   }
   ```
5. Build and run the scene, or play in editor. The recorder writes its
   first row after the coordinate-origin grace window resolves
   (`SessionRecorder.cs:54`).

### Verify

- The Unity log contains
  `[ExperimentMetadata] Schema locked with N fields: ..., Condition, ...`
  emitted by `LockSchema` (`Assets/Scripts/EyeTracking/Data/ExperimentMetadata.cs:223`).
- Open the produced CSV from `Application.persistentDataPath` (or the
  Android external files dir on device). The header row contains
  `,Condition,` between `IsDebugMode` and `HeadPos_X`.
- Every data row carries the value `Experimental` in that column.

---

## Common patterns

### Per-trial structured fields

Declare every field up front in `Awake`, set them at trial-start, and
update outcome fields when the response lands.

```csharp
void Awake()
{
    recorder.DeclareMetadataField("BlockNumber",   MetadataValueType.Int);
    recorder.DeclareMetadataField("TrialInBlock",  MetadataValueType.Int);
    recorder.DeclareMetadataField("SetSize",       MetadataValueType.Int);
    recorder.DeclareMetadataField("TargetPresent", MetadataValueType.Bool);
    recorder.DeclareMetadataField("ResponseTime",  MetadataValueType.Float);
    recorder.DeclareMetadataField("WasCorrect",    MetadataValueType.Bool);
}

void StartTrial(int block, int trial, TrialConfig cfg)
{
    recorder.SetMetadata("BlockNumber",   block);
    recorder.SetMetadata("TrialInBlock",  trial);
    recorder.SetMetadata("SetSize",       cfg.distractorCount);
    recorder.SetMetadata("TargetPresent", cfg.hasTarget);
}

void OnResponse(bool correct, float rt)
{
    recorder.SetMetadata("ResponseTime", rt);
    recorder.SetMetadata("WasCorrect",   correct);
}
```

`SessionRecorder` also maintains the built-in `TrialNumber`,
`CurrentPhase`, `SubTask`, and `SessionConfig` columns through
`SetSessionContext` and `SetSubTask` (`SessionRecorder.cs:676`,
`SessionRecorder.cs:685`). Use those for trial counters and phase
labels rather than reinventing them as metadata fields.

### Per-session experiment-config snapshot

Constants known at session start (experiment version, between-subjects
condition, demographic group) only need to be set once. Their value
propagates to every row.

```csharp
void Awake()
{
    recorder.DeclareMetadataField("ExperimentName",    MetadataValueType.String);
    recorder.DeclareMetadataField("ExperimentVersion", MetadataValueType.String);
    recorder.DeclareMetadataField("Condition",         MetadataValueType.String);
}

void Start()
{
    recorder.SetMetadata("ExperimentName",    "VisualSearch_v2");
    recorder.SetMetadata("ExperimentVersion", "1.0.3");
    recorder.SetMetadata("Condition",         assignedCondition);
    recorder.SetParticipantID("P_007");
}
```

`SetParticipantID` (`SessionRecorder.cs:691`) is a separate API — it
writes to the header metadata block, not a custom column.

### Inspector-defined fields via ExperimentMetadataSchema

For experiments where the field set is fixed and configured by a
non-programmer, define an `ExperimentMetadataSchema` ScriptableObject
and assign it to `SessionRecorder.metadataSchema`.

1. In the Project window: **Create > Eye Tracking > Experiment
   Metadata Schema**. Name it (e.g. `VisualSearchSchema`).
2. In the Inspector, add a row per field. Each row has `FieldName`,
   `Type` (String/Int/Float/Bool), `DefaultValue` (parsed against
   `Type`), and a `Description` for documentation
   (`Assets/Scripts/EyeTracking/Configuration/ExperimentMetadataSchema.cs:11`).
3. Select the GameObject carrying `SessionRecorder`. Drag the schema
   asset into the **Custom Experiment Metadata > Metadata Schema**
   slot.
4. On `SessionRecorder.Start`, the schema's `InitializeMetadata` runs
   (`SessionRecorder.cs:237`, `ExperimentMetadataSchema.cs:58`). Each
   schema field becomes a declared metadata field with its default
   value already set.
5. Update values at runtime via `SetMetadata` exactly as for
   code-declared fields.

The schema's `OnValidate` rejects empty names, duplicate names,
reserved built-in column names, and field names containing comma /
quote / newline / space (`ExperimentMetadataSchema.cs:109`). Watch the
Unity console for `[ExperimentMetadataSchema] Validation warning:`
lines while editing.

### Plug-in metric columns (RegisterMetric)

For per-frame numeric signals computed by another component (a custom
fixation index, a controller state, anything sampled at frame rate),
prefer `RegisterMetric` over `SetMetadata`. The recorder calls the
getter once per row instead of you forwarding a value into a metadata
field every frame.

```csharp
void Awake()
{
    recorder.RegisterMetric("MyScore", () => CustomLogic.Score(), "F3");
}
```

`RegisterMetric` columns appear between `DataSampleCount` and
`GazedObjectName`, in registration order
(`SessionRecorder.cs:122`, `SessionRecorder.cs:143`). This is the same
mechanism `RIPACSVColumn` uses to add the `LiveLoadIndex` column. See
[`SESSION_RECORDER.md`](../../docs/SESSION_RECORDER.md) for the full
metric API.

---

## API reference

All methods live on
`Assets/Scripts/EyeTracking/Components/SessionRecorder.cs`.

| Method | Line | Behavior |
|---|---|---|
| `DeclareMetadataField(string name, MetadataValueType type)` | 735 | Reserve a CSV column. Logs error and returns if called after `csvInitialized`. |
| `SetMetadata(string, string)` | 699 | Set a string value. Escapes commas/quotes via `MetadataValue.ToCSVString`. |
| `SetMetadata(string, int)` | 706 | Set an integer value. |
| `SetMetadata(string, float)` | 713 | Set a float value (CSV-formatted as `F6`). |
| `SetMetadata(string, bool)` | 720 | Set a boolean value (CSV-formatted as `TRUE`/`FALSE`). |
| `RemoveMetadata(string)` | 746 | Remove a field. Has no effect after schema lock. |
| `ClearMetadata()` | 753 | Remove all custom fields. |
| `GetMetadata()` | 101 | Return the underlying `ExperimentMetadata` for advanced inspection. |
| `IsMetadataSchemaLocked` | 99 | True once the CSV header has been written. |
| `InitializeMetadataFromSchema(ExperimentMetadataSchema)` | 759 | Load fields + defaults from a schema asset. Auto-called on `Start` if the Inspector slot is set. |

CSV value formatting is implemented in
`Assets/Scripts/EyeTracking/Data/ExperimentMetadata.cs:96`:

| Type | Example value | CSV cell |
|---|---|---|
| String | `Experimental` | `Experimental` |
| String with `,` or `"` | `A, B, C` | `"A, B, C"` (quoted, internal `"` doubled) |
| Int | `42` | `42` |
| Float | `1.234567f` | `1.234567` (`F6`, invariant culture) |
| Bool | `true` / `false` | `TRUE` / `FALSE` |

Reserved field names — these are built-in columns and conflict if
reused as metadata names (`ExperimentMetadataSchema.cs:129`):

```
UnityTimestamp, RealTimeSinceStartup, SystemTimestamp, FrameNumber, DeltaTime
ParticipantID, TrialNumber, CurrentPhase, SubTask, SessionConfig, IsDebugMode
HeadPos_X, HeadPos_Y, HeadPos_Z, CurrentFPS, FrameTimeMs, DataSampleCount
```

Field names must not contain comma, quote, newline, or space
(rejected by `ExperimentMetadataSchema.OnValidate`; `SetMetadata` does
not enforce this — keep your names alphanumeric).

---

## Common mistakes

**Declaring a field after recording starts.** The schema is locked
when the CSV header is written. The error log line is:

```
[SessionRecorder] Cannot declare field 'X' — CSV already initialized.
```

emitted by `DeclareMetadataField` at `SessionRecorder.cs:739`. Move
the declaration into `Awake` (or `Start`, before the first frame
sample lands) of a component on the same scene.

**Setting a field name that was never declared, after lock.** The
warning is:

```
[SessionRecorder] WARNING: Adding new metadata field 'X' after CSV export started will cause column count mismatches. Call DeclareMetadataField BEFORE recording.
```

emitted at `SessionRecorder.cs:731`. The value is stored internally
but never reaches the CSV — the column was not in the header.

**Type-confusing a field across calls.** `SetString` followed by
`SetInt` on the same field replaces the entire `MetadataValue`,
including its `Type`. The CSV cell will be formatted by the most
recent type. If you need both numeric and label representations,
declare two fields (e.g. `ConditionLabel` and `ConditionCode`).

**Updating high-frequency metadata in `Update`.** `SetMetadata` takes
a per-call lock on the metadata container
(`ExperimentMetadata.cs:317`). Skip the call when the value hasn't
changed:

```csharp
if (currentPhase != lastPhase)
{
    recorder.SetMetadata("Phase", currentPhase);
    lastPhase = currentPhase;
}
```

**Conflating `SetSessionContext` and `SetMetadata`.** `TrialNumber`,
`CurrentPhase`, `SubTask`, `SessionConfig`, and `ParticipantID` are
built-in columns set via `SetSessionContext` / `SetSubTask` /
`SetParticipantID`. Don't redeclare them as metadata — schema
validation will reject them.

---

## References

- Source: `Assets/Scripts/EyeTracking/Components/SessionRecorder.cs`
- Backing data structure: `Assets/Scripts/EyeTracking/Data/ExperimentMetadata.cs`
- Schema asset: `Assets/Scripts/EyeTracking/Configuration/ExperimentMetadataSchema.cs`
- Per-frame recorder manual: [`SESSION_RECORDER.md`](../../docs/SESSION_RECORDER.md)
- CSV column layout: `docs/DATA_SCHEMA.md`
- Working examples in the codebase:
  - `Assets/Scripts/Experiment/SampleExperimentController.cs:441` — declares `SessionType` + `ExperimentVersion`.
  - `Assets/Scripts/Skeleton/Managers/TrialManager.cs:119` — declares per-trial block/index/failure fields and dispatches typed `SetMetadata` calls from a value-of-`object` helper.
