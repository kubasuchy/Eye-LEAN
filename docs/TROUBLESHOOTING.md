# Eye_lean Troubleshooting

## What it is

A symptom-first reference for diagnosing Eye_lean sessions that fail
or misbehave on the device or in the Unity Editor. Every entry names
the log line, file path, or Inspector field that confirms the cause,
so triage starts from concrete evidence rather than guesses.

## Audience

Anyone debugging an Eye_lean session that misbehaves on the device or
in the editor.

## Prerequisites

- Unity 6000.3.9f1, VIVE OpenXR 2.5.1 (or a compliant OpenXR runtime)
- A VIVE Focus Vision (or simulator) and a USB cable for `adb`
- Android platform tools on the host (`adb` on `PATH`)
- A working install of the project; see
  [`BUILD_GUIDE.md`](BUILD_GUIDE.md) for the device build flow

---

## Diagnostics first

Before opening a symptom section below, collect the four standard
artefacts. Most procedures below ask for at least one of them.

1. **Editor Console**. Filter by component tag: `[EyeTracker]`,
   `[HMDDataCollector]`, `[SessionRecorder]`, `[RIPAMonitor]`,
   `[CalibrationSessionManager]`, `[ReplayController]`.
2. **Device logcat**. Run `adb logcat -s Unity` while reproducing the
   bug. Same component tags appear in logcat.
3. **Output CSV** on device. The persistent path resolved by
   `Assets/Scripts/EyeTracking/Configuration/DataExportSettings.cs:91`
   maps to
   `/sdcard/Android/data/com.RutgersVCL.Eye_lean/files/EyeTrackingData/`.
4. **Calibration profile JSON**. Stored under
   `Application.persistentDataPath/EyeLeanProfiles/`, see
   `Assets/Scripts/EyeTracking/Configuration/EyeTrackingProfile.cs:46`.
   The default profile is `default.json`; per-participant archives
   sit beside it.

### Verify

The four sources cover the four subsystems:
gaze (`EyeTracker`), head pose (`HMDDataCollector`), CSV
(`SessionRecorder`), and replay (`ReplayController`). If any is
missing in your reproduction, the corresponding section below cannot
be answered.

---

## Symptom -> diagnostic procedure

### Eye tracking does not initialize on device

**Symptom**: Gaze rays are zero, vergence point sits at the camera,
or `EyeTracker` reports the `NullEyeTracker` provider.

1. Run `adb logcat -s Unity` and grep for `[EyeTracker]`. A working
   session prints `[EyeTracker] Initialized` followed by
   `[EyeTracker] ✅ Initialized — Device: <name>` from
   `Assets/Scripts/EyeTracking/Components/EyeTracker.cs:204` and
   `:242`.
2. If you see `[EyeTracker] ⚠️ Eye tracker not available — Device:
   None` (`EyeTracker.cs:250`), the OpenXR provider failed to
   resolve. Check for `[OpenXREyeTrackerProvider] Failed to access
   OpenXREyeTracker` from
   `Assets/Scripts/EyeTracking/Providers/OpenXREyeTrackerProvider.cs:51`.
3. Confirm the OpenXR feature set in **Project Settings > XR Plug-in
   Management > OpenXR > Android**. The **Eye Gaze Interaction**
   feature must be checked. Without it the provider returns
   `Available = false`.
4. Run the system-level eye tracker calibration on the headset (VIVE
   menu) and reboot the headset. A stale runtime calibration is the
   most common cause of provider unavailability.

#### Verify

Logcat shows `[EyeTracker] ✅ Initialized — Device: ViveSR` (or your
device's name) and gaze values vary as you move your eyes.

---

### CSV file is empty or missing

**Symptom**: A session ran to completion, but `adb pull` returns no
file, or the file exists with header only.

1. In logcat or Console, look for `[SessionRecorder] CSV
   initialized:` from
   `Assets/Scripts/EyeTracking/Components/SessionRecorder.cs:275`.
   Absence means the writer never opened — either
   `enableDataCollection` is off (`SessionRecorder.cs:47`) or
   `enableCSVExport` is off (`SessionRecorder.cs:48`).
2. If you see `[SessionRecorder] Data collection DISABLED in
   inspector` (`SessionRecorder.cs:192`), open the **SessionRecorder**
   sibling component on the EyeTracking GameObject and tick **Enable
   Data Collection**.
3. If you see `[SessionRecorder] ReplayMode active — recording
   suppressed for this session.` (`SessionRecorder.cs:201`), the
   scene is in replay mode. Recording is intentionally disabled while
   `ReplayController` is active. Disable the `ReplayController` or
   load a recording-only scene.
4. If you see `[SessionRecorder] Failed to initialize CSV:`
   (`SessionRecorder.cs:279`), inspect the message. The most common
   cause is the app lacking write access to the persistent path; on
   Android 11+ verify the app has the `MANAGE_EXTERNAL_STORAGE` or
   per-app scoped storage permission in **Settings > Apps**.

#### Verify

`adb shell ls
/sdcard/Android/data/com.RutgersVCL.Eye_lean/files/EyeTrackingData/`
lists a `*.csv` whose size grows during a recording, and logcat
prints `[SessionRecorder] CSV file saved:` from
`SessionRecorder.cs:658` on stop.

---

### CSV is missing per-object Gaze\_ columns

**Symptom**: The header has core columns but no `Gaze_<object>`
columns for the experiment props.

1. In logcat search for `[SessionRecorder] ObjectTrackingRegistry
   empty at header-write` from
   `Assets/Scripts/EyeTracking/Components/SessionRecorder.cs:366`.
   This means the recorder wrote its header before any
   `GazeTarget`-tagged object was registered.
2. If the next line is `[SessionRecorder] Reinit still found 0
   tracked objects — Gaze_<obj> columns will be absent.`
   (`SessionRecorder.cs:370`), the scene has no `GazeTarget`
   colliders at all. Add the `GazeTarget` component to each prop you
   want a column for.
3. The header is written on the first sample after
   `SetCoordinateOrigin`. Spawn props before that call, or call
   `SessionRecorder.RegisterObject` from your spawn code.

#### Verify

Logcat shows `[SessionRecorder] Reinit registered N tracked objects.`
(`SessionRecorder.cs:369`) with `N > 0`, and the CSV header contains
one `Gaze_<name>` column per registered object.

---

### Custom metric column is missing or zeros

**Symptom**: A custom metric registered with `SessionRecorder.RegisterMetric`
does not appear in the CSV header, or appears but is always zero.

1. Search logcat for `[SessionRecorder] Cannot register metric '...'
   — CSV header already written.` from
   `Assets/Scripts/EyeTracking/Components/SessionRecorder.cs:128`.
   Registration must happen before the first sample is written
   (i.e. before `SetCoordinateOrigin`).
2. Move the `RegisterMetric` call to `Awake`, or to your scene
   bootstrap before any trial begins.
3. If the column is present but zero, your getter is returning zero.
   Add a one-line `Debug.Log` from inside the getter to confirm it is
   being invoked at sample rate.

#### Verify

The CSV header contains your metric name, and per-row values vary as
expected.

---

### Calibration profile does not persist across sessions

**Symptom**: Calibration completes, but the next launch falls back to
the uncorrected pipeline.

1. In logcat after calibration, look for `[EyeTrackingProfileApi]
   Saved profile to <path>` from
   `Assets/Scripts/EyeTracking/Configuration/EyeTrackingProfileApi.cs:71`.
   Absence means the verification stage rejected the profile (see
   next symptom).
2. Confirm `[EyeTrackingProfileApi] Also wrote default profile:
   <path>` from `EyeTrackingProfileApi.cs:79`. Without that line, the
   profile was archived but never promoted to the default.
3. On launch, look for `[EyeTrackingProfileApi] No default profile at
   <path>; pipeline runs without correction.` from
   `EyeTrackingProfileApi.cs:49`. That means
   `EyeLeanProfiles/default.json` is missing or unreadable.
4. Pull the file: `adb shell ls
   /sdcard/Android/data/com.RutgersVCL.Eye_lean/files/EyeLeanProfiles/`.
   Confirm `default.json` exists and is non-empty.

#### Verify

`default.json` is present in the persistent profile directory, and
the next session's logcat shows the profile being applied via
`EyeTrackingProfileApi.LoadAndApplyDefault`.

---

### Calibration verification keeps rejecting the profile

**Symptom**: Calibration ends with a "verification regressed" prompt
and the previous profile remains active.

1. In Console or logcat, find the line `[CalibrationSessionManager]
   Post-fit verification regressed:` from
   `Assets/Scripts/EyeTracking/Calibration/CalibrationSessionManager.cs:881`.
   The reason string reports the pre-fit and post-fit median errors.
2. Check the `[ActiveProfile] Applied profile '<name>': yawOffset=...,
   pitchOffset=..., yawGain=×..., pitchGain=×...` line at session
   start. A non-unit gain (`×0.7`, `×1.3`, ...) on a session with a
   pre-loaded profile is a sign the prior fit was over-aggressive;
   clear the profile and refit.
3. If pre-fit accuracy is unusually low on session start, the saved
   `_default.json` may be stale — its correction was fit against an
   earlier headset-level eye-calibration that has since been re-run.
   Delete `Application.persistentDataPath/EyeLeanProfiles/_default.json`
   on the device (over `adb`) and re-launch to fall back to identity
   correction; the next calibration fits from scratch.
4. Reseat the headset, re-run the system-level VIVE eye calibration
   on the device, and re-attempt the Eye_lean session. A regression
   also surfaces when the headset slips between pre-fit and post-fit
   phases.
5. If rejection persists with a tightly-seated headset and a fresh
   profile, raise `VerificationMedianRegressionThresholdDeg`
   (`CalibrationSessionManager.cs:784`, default 0.30°) — but inspect
   the per-test medians first, since a persistent regression usually
   means the fit itself is bad, not the gate.

#### Verify

A subsequent calibration logs `[CalibrationSessionManager]
Verification passed; promoted profile to default:` from
`CalibrationSessionManager.cs:899`, and `_default.json` is rewritten.

---

### RIPA LiveLoadIndex column is all zeros

**Symptom**: The CSV `LiveLoadIndex` column is present but every value
is `0` or `NaN`.

1. Confirm the monitor was constructed: logcat should show
   `[RIPAMonitor] Active. fs=<rate> Hz, ...` from
   `Assets/Scripts/EyeTracking/Metrics/RIPAMonitor.cs:161`. Absence
   means the analyzer threw at construction; look for
   `[RIPAMonitor] Failed to construct analyzer:`
   (`RIPAMonitor.cs:165`).
2. Confirm valid pupil data is reaching the monitor. The first valid
   output prints `[RIPAMonitor] First valid output: raw=<v>
   smoothed=<v> (after <N> pushed samples).`
   (`RIPAMonitor.cs:221`). If you instead see `[RIPAMonitor] Diag:
   pushed=<N> skippedNaN=<M> ...` (`RIPAMonitor.cs:211`) with high
   `skippedNaN`, the eye tracker is returning unreliable pupil
   diameters; recalibrate the device.
3. If two `RIPAMonitor` instances exist, the second logs
   `[RIPAMonitor] Duplicate instance on '<name>'; destroying
   duplicate.` (`RIPAMonitor.cs:136`). The
   `RIPAMonitorBootstrap` (`Assets/Scripts/EyeTracking/Metrics/RIPAMonitorBootstrap.cs:24`)
   already auto-spawns one per scene; remove any manually placed
   monitor unless you need custom Inspector settings.
4. During replay, `RIPAMonitorBootstrap` deliberately skips spawning so the
   recorded `LiveLoadIndex` is preserved. This is correct behaviour, not a
   bug.

#### Verify

Console shows the `Active.` line, and live values flow into the
RIPAGauge (or the raw column varies away from zero in the CSV).

---

### Vergence point looks wrong (jumps, lags, or sticks at the camera)

**Symptom**: The visualized vergence sphere does not track gaze.

1. In the Inspector on `EyeTracker`, confirm `vergenceDepthMode` and
   `vergenceSettings.preset` are non-default. The startup log
   `[EyeTracker] Vergence Configuration: mode=<mode>, preset=<preset>,
   method=<method>` from
   `Assets/Scripts/EyeTracking/Components/EyeTracker.cs:186` records
   what the runtime saw.
2. If the sphere sits at the camera origin, the camera is not
   resolved. Look for `[EyeTracker] Auto-assigned camera:` from
   `EyeTracker.cs:177`. Absence means the scene has no camera tagged
   `MainCamera`; tag the XR camera and reload.
3. If the sphere lags, increase `vergenceSettings.smoothing` in the
   profile JSON. If it jumps, reduce smoothing or switch
   `vergenceDepthMode` to `DepthExtension`. The detailed math lives in
   [`../Eye_lean_Unity_Project/Eye_lean/docs/ALGORITHMS.md`](../Eye_lean_Unity_Project/Eye_lean/docs/ALGORITHMS.md).
4. If the sphere has no parent or follows the head exactly, you are
   looking at the round-9 detached-vergence-point bug. Confirm the
   `EyeTracker` GameObject is not a child of a `DontDestroyOnLoad`
   root.

#### Verify

Console shows `[EyeTracker] Vergence point visual created` from
`EyeTracker.cs:428` once per scene load, and the sphere position
varies with gaze.

---

### Replay shows no movement or wrong duration

**Symptom**: `ReplayController` loads a CSV but playback is frozen,
empty, or finishes immediately.

1. Verify the CSV loaded: Console should show `[ReplayController]
   Loaded <N> frames, duration: <s>s` from
   `Assets/Scripts/Replay/Core/ReplayController.cs:787`. If `N` is 0,
   the parser rejected every row; check
   `[ReplayController] Parse error:` (`ReplayController.cs:725`).
2. If logs show `[ReplayController] Cannot play - data not loaded`
   (`ReplayController.cs:401`), the load failed silently. Re-check
   the file path on the `ReplayController` Inspector.
3. If the room geometry is missing, look for
   `[DemoReplayBootstrapper] No EnvironmentGenerator in scene — nothing to
   anchor.` from `Assets/Scripts/Experiment/DemoReplayBootstrapper.cs:62`. The
   scene needs an `EnvironmentGenerator` to rebuild props at the recorded
   coordinate origin.
4. If the recorded gaze does not drive live experiment logic, confirm
   `[ReplayController] Deterministic replay: installed
   ReplayingEyeTracker as factory override; recorded gaze will drive
   live experiment's gaze checks.` from
   `ReplayController.cs:776`. The opposite log
   (`ReplayController.cs:780`) means `deterministicReplay` is off in
   the Inspector.

#### Verify

`[ReplayController] Playing from frame <N>`
(`ReplayController.cs:418`) appears, and the headset / camera moves
through the recorded trajectory.

---

### Replay shows ray origin far from head

**Symptom**: Replay log warns about a recorded eye origin sitting
several metres from the head pose.

1. The warning `[ReplayController] Frame <N>: recorded <left|right>
   eye origin <v> is <m>m from head pos <v> — likely coord-frame
   race. Head-anchored mode <ON|OFF> ...` is emitted at
   `Assets/Scripts/Replay/Core/ReplayController.cs:986`. This is the
   VIVE coord-frame race.
2. Tick **Use Head-Anchored Ray Origin** on the `ReplayController`
   Inspector. The warning will continue to print, but the ray origin
   is reconstructed from head pose, hiding the race for visualization.
3. The underlying recording is not corrupted; the gaze direction is
   correct in tracking-space. To re-record cleanly, ensure the
   `HMDDataCollector` calls `SetCoordinateOrigin` after the OpenXR
   tracking origin has stabilized (after the first
   `Application.onBeforeRender` tick on device).

#### Verify

With head-anchored mode on, the replay rays start at the camera in
each frame and the warning still appears in logs but no longer
affects visualization.

---

### Panel placement floats, sits at the floor, or drifts between phases

**Symptom**: Instruction panels sit at floor level, or jump vertically
between trials.

1. The `ExperimentUI` panel locks its Y at the first frame the camera
   is at least 1m above the floor. Console line
   `[ExperimentUI] Panel positioned at <pos> (anchorY latched=<bool>,
   value=<y>, cameraY=<y>)` from
   `Assets/Scripts/Experiment/UI/ExperimentUI.cs:464` confirms the
   latch.
2. If `cameraY < 1.0`, the latch is skipped that frame and the panel
   tracks the camera. Stand up before scene load, or override
   `cachedAnchorY` in code.
3. If the panel re-anchors mid-experiment, a scene reload reset the
   latch. Inter-phase transitions in `SampleExperimentController` do
   not reload the scene; if you wrote a custom phase that does, the
   panel will re-latch.

#### Verify

Logcat shows `anchorY latched=True` exactly once per scene entry, and
the `value` is in the expected eye-height range (~1.6m).

---

### App crashes on launch

**Symptom**: The app exits before the splash, or the splash freezes.

1. Run `adb logcat -s Unity` while launching. The Unity stack trace
   appears prefixed with `Unity   FATAL` or `AndroidRuntime`.
2. The most common cause is a missing OpenXR runtime: look for
   `XR Plug-in Management` errors. Confirm the device's VIVE OpenXR
   runtime is installed and up to date.
3. If the trace mentions a shader compilation failure, rebuild after
   adding the project's URP shaders to
   **Edit > Project Settings > Graphics > Always Included Shaders**.
4. If the trace mentions
   `Application.persistentDataPath` write errors, reinstall:
   `adb uninstall com.RutgersVCL.Eye_lean` then push the APK again.

#### Verify

Logcat shows the splash screen and then `[EyeTracker] Awake on '<go>'`
from
`Assets/Scripts/EyeTracking/Components/EyeTracker.cs:154`.

---

### `adb pull` retrieves no files

**Symptom**: `adb pull` exits with `0 files pulled`, or pulls only
empty directories.

1. Confirm the on-device path exists: `adb shell ls
   /sdcard/Android/data/com.RutgersVCL.Eye_lean/files/EyeTrackingData/`.
   If the folder is missing, no session has flushed yet.
2. The recorder flushes on `OnApplicationPause` and `OnDestroy`.
   Console / logcat should show
   `[SessionRecorder] CSV file saved: <path>` from
   `Assets/Scripts/EyeTracking/Components/SessionRecorder.cs:658`.
   Force a flush by removing the headset (triggers pause) before
   pulling.
3. On Android 11+, scoped storage may block external pulls. Use the
   exact path under `/sdcard/Android/data/<package>/files/...` rather
   than the legacy `/sdcard/EyeLean/` path.
4. If `adb` reports `Permission denied`, run
   `adb shell run-as com.RutgersVCL.Eye_lean ls files/EyeTrackingData`
   (debug builds only). For release builds, copy via
   `adb shell content`.

#### Verify

`adb pull /sdcard/Android/data/com.RutgersVCL.Eye_lean/files
./pulled` exits with `1 file pulled` or more, and the local
`pulled/EyeTrackingData/` directory contains the session CSV.

---

### "Namespace not found" build errors

**Symptom**: The Editor logs `error CS0234` for namespaces such as
`EyeTracking.Vergence`, `EyeLean.Replay`, or `EyeLean.Replay.Analysis`.

1. Inspect the Assembly Definitions under `Assets/Scripts/`. Each
   subsystem has its own `.asmdef`: `EyeLean.Core`, `EyeLean.Replay`,
   `EyeLean.SampleDemo`, `EyeLean.MainMenu`, `EyeLean.Editor`.
2. Open the failing script and look at its `using` lines. The hosting
   `.asmdef` must reference every namespace it imports.
3. Reimport: **Assets > Reimport All**. Stale `Library/ScriptAssemblies`
   builds occasionally lose references after a Unity update.

#### Verify

The Editor recompiles cleanly and the affected scene's components no
longer show "Missing script".

---

### Python loader rejects boolean columns

**Symptom**: `pd.read_csv` raises `could not convert string to float`
for `True` / `False` columns.

1. Use the bundled loader: `eyelean_analysis.load_eyetracking('file.csv')`
   normalizes booleans internally.
2. If you must use raw Pandas, map the columns:
   `df[col] = df[col].map({'True': True, 'False': False})` for each
   string-typed column.

#### Verify

`df.dtypes` shows boolean for the affected columns and downstream
analysis no longer raises.

---

### Python K-coefficient does not match C# results

**Symptom**: K differs by more than 0.1 between live and offline
analysis.

1. Compare thresholds: C# `K_COEFFICIENT_FOCAL_THRESHOLD` in
   `Assets/Scripts/EyeTracking/Analysis/AnalysisConstants.cs` against
   `eyelean_analysis/k_coefficient.py`. Both should be `0.5`.
2. Compare window sizes; the default is 30 samples on both sides.
3. NaN rows: live C# skips NaN-pupil samples implicitly, Python does
   not unless you call `dropna()` first.

#### Verify

After harmonizing thresholds and dropping NaN samples in Python, K
agrees within 0.1 on the same input CSV.

---

## Where to file an issue

Open issues at
[https://github.com/kubasuchy/EYE-LEAN/issues](https://github.com/kubasuchy/EYE-LEAN/issues).

Include in every report:

- Eye_lean version (Unity bundle version, or `eyelean_analysis.__version__`)
- HMD model and firmware (e.g. VIVE Focus Vision, runtime 2.5.1)
- Unity version (must be 6000.3.9f1 for the supported pin)
- Steps to reproduce — the minimum scene + Inspector settings + input
- The relevant log block (Console for Editor, `adb logcat -s Unity`
  for device), trimmed to the seconds around the failure
- The first 5 rows of the offending CSV, with participant identifiers
  redacted

For algorithm or analysis bugs, attach a small CSV (≤ 10 MB) and the
exact `eyelean_analysis` call that produces wrong output.

---

## References

- [`BUILD_GUIDE.md`](BUILD_GUIDE.md) — building, signing, and pulling
  data
- [`EYE_TRACKER.md`](EYE_TRACKER.md) — `EyeTracker` Inspector fields
  and vergence config
- [`SESSION_RECORDER.md`](SESSION_RECORDER.md) — CSV schema, custom
  metric API, persistence layout
- [`RIPA_MONITOR.md`](RIPA_MONITOR.md) — RIPA pipeline configuration
  and CSV column
- [`../Eye_lean_Unity_Project/Eye_lean/docs/RESEARCHER_GUIDE.md`](../Eye_lean_Unity_Project/Eye_lean/docs/RESEARCHER_GUIDE.md)
  — end-to-end recording + replay walk-through
- [`../Eye_lean_Unity_Project/Eye_lean/docs/ALGORITHMS.md`](../Eye_lean_Unity_Project/Eye_lean/docs/ALGORITHMS.md)
  — vergence math and metric derivations
