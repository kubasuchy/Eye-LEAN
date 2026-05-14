# ExperimentUI — instructions and progress panels

## What it is

`ExperimentUI` is a world-space UI rig for displaying instructions,
progress text, and a phase indicator to the participant. Every public
show/hide call auto-records to the `SceneEventRecorder` sidecar so
the same UI state replays at the same frame during deterministic
replay. SampleExperiment uses it as the canonical UI; custom scenes
can use it as-is or substitute another class that follows the same
auto-record pattern.

## Audience

Developers building experiments that need world-space instruction or
progress panels.

## Prerequisites

- A scene with a `SceneEventRecorder` (auto-spawned by the recording
  bootstrap or attached manually).
- A `Camera.main` available in the scene; `ExperimentUI` resolves the
  camera transform in `Awake` if no override is set.
- Optional: an `IRoomFrameProvider` (such as `EnvironmentGenerator`)
  for wall-parallel panel orientation.

---

## How to use it

```csharp
using EyeLean.Experiment;

var ui = FindFirstObjectByType<ExperimentUI>();

ui.ShowInstruction("Look at the red sphere when it appears");
yield return new WaitForSeconds(3f);

// Update text without repositioning the panel (useful for countdowns):
ui.SetInstructionTextOnly("Starting in 3...");

ui.HideInstruction();
ui.ShowProgress("Trial 5 / 12");
ui.HideProgress();
```

### Verify

In `adb logcat -s Unity` after `ShowInstruction` fires, look for:

```
[ExperimentUI] ShowInstruction called with: '<text>'
[ExperimentUI] Panel positioned at <pos> (anchorY latched=True, value=<y>, cameraY=<y>)
```

The corresponding row also appears in the events sidecar
(`<csv-stem>_events.csv`) with `EventType=ShowInstruction`.

---

## API reference

File: `Assets/Scripts/Experiment/UI/ExperimentUI.cs`

| Method | Line | Purpose | Auto-records as |
|---|---|---|---|
| `ShowInstruction(string text)` | `:442` | Show + reposition the instruction panel. | `ShowInstruction` |
| `SetInstructionTextOnly(string text)` | `:474` | Update text in place. Use for countdowns. | `SetInstructionTextOnly` |
| `HideInstruction()` | `:484` | Hide the panel. | `HideInstruction` |
| `ShowProgress(string text)` | `:496` | Show the smaller progress label. De-duped on text change. | `ShowProgress` (only on change) |
| `HideProgress()` | `:522` | Hide the progress panel. | `HideProgress` |
| `SetPinToCorner(bool, UICorner)` | `:534` | Toggle corner pinning. | not recorded |
| `SetPanelDistance(float)` | `:723` | Override the in-world panel distance. | not recorded |

Toggle auto-recording per-instance via the Inspector field
`autoRecordEvents` (`ExperimentUI.cs:58`).

The corner phase indicator (top-RIGHT) listens to
`SampleExperimentController.OnPhaseChanged` if one is present;
otherwise it stays hidden. The cognitive-load HUD (top-LEFT)
auto-binds to `RIPAMonitor.Instance.OnLoadChanged` and tints
green → amber → red across the shared `[0, 1.5]` smoothed range. The
inspector field `hudMethod` (v1.0.1+) selects which detector
(RIPA2 / Butterworth / FFT / DWT) drives the HUD value; the monitor
still computes and records every enabled detector each frame.

---

## Panel-anchor model

Round-23 (2026-05-04 hardware report) replaced the previous
camera-anchored panel with a room-frame XZ + camera-Y latched
anchor. `UpdateCenteredPosition` (`ExperimentUI.cs:348`) computes:

- **XZ origin** — the `IRoomFrameProvider.RoomTransform.position` if
  a provider exists, else `cameraTransform.position`. The panel sits
  at a fixed location relative to the back wall and does not slide
  when the user steps sideways.
- **Forward** — the room transform's horizontal forward (panel
  parallel to the back wall) when a provider exists; the camera's
  horizontal forward otherwise.
- **Y** — `cameraTransform.position.y + panelHeight` latched on the
  first stable HMD pose, then frozen for the lifetime of the scene.
  Latching prevents the panel from jumping when the user crouches or
  stands between phases. The stability gate (v1.0.1+) requires camera
  Y in `[1.0, 2.2] m` and stationary within ±0.15 m for 0.3 s before
  it commits, so transient XR-rig poses during a scene transition
  cannot lock the panel above the ceiling.

### Inspector fields that affect anchoring

| Field | Default | Effect |
|---|---|---|
| `panelDistance` | 1.8 m | Distance from anchor along the forward axis. Increase if the panel feels too close. |
| `panelHeight` | 0.3 m | Y offset added to the latched camera height. |
| `pinToCorner` | false | If true, anchor switches to head-relative corner offset (`UpdatePinnedPosition`); centered is more reliable across HMDs. |
| `pinnedCorner` | `TopRight` | Which corner to pin to when `pinToCorner` is true. |
| `cornerOffsetHorizontal` | 0.6 m | Horizontal offset from camera center when corner-pinned. |
| `cornerOffsetVertical` | 0.35 m | Vertical offset from eye level when corner-pinned. |
| `followCamera` | false | If true, panel re-positions every frame. Leave false for VR — UI should stay in place. |
| `followSpeed` | 2.0 | Lerp speed when `followCamera` is true. |
| `autoRecordEvents` | true | Master switch for the auto-record behavior described above. |
| `autoCreateUI` | true | Build the panels procedurally if `instructionPanel` is null. |

---

## How it integrates with the rest of the toolkit

- **Replay.** Every show/hide call records to `SceneEventRecorder`.
  During deterministic replay the live experiment re-issues the same
  calls at the same frame, so instruction panels reappear at the
  recorded moment.
- **Cognitive-load monitor.** Auto-binds to `RIPAMonitor.Instance`
  if one is in scene (the bootstrap auto-spawns one). The HUD bar
  fills `0..1` of `load / 1.5` and tints green → amber → red. The
  `hudMethod` inspector field selects which detector drives the bar
  (RIPA2 / Butterworth / FFT / DWT — see
  [`RIPA_MONITOR.md`](RIPA_MONITOR.md)).
- **Phase indicator.** Subscribes to
  `SampleExperimentController.OnPhaseChanged` to render
  "Phase X / Y" plus dot progression. In a non-SampleExperiment
  scene without that controller, the indicator stays hidden;
  instructions and progress still work.

---

## Common patterns and gotchas

- **Reposition only on first show.** `ShowInstruction` repositions
  the panel; `SetInstructionTextOnly` does not. Use the latter for
  countdowns to avoid the snap between digits.
- **De-duped progress.** `ShowProgress` is typically called every
  frame in timed phases. The wrapper de-dupes on text change so the
  events sidecar does not bloat.
- **Room-frame orientation.** The panel orients to the room's
  forward axis when an `IRoomFrameProvider` (typically
  `EnvironmentGenerator`) is in scene, so it sits parallel to the
  back wall regardless of where the user is facing.
- **Corner pinning is opt-in.** Centered panels are more reliable
  across HMDs; corner pinning is off by default.

---

## References

- Source: `Assets/Scripts/Experiment/UI/ExperimentUI.cs`.
- Replay sidecar schema: [SCENE_EVENTS.md](SCENE_EVENTS.md).
- RIPA HUD binding: [RIPA_MONITOR.md](RIPA_MONITOR.md).
