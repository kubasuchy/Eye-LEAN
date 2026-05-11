# Replay Scene State (v1.1, extended in v1.2)

The scene-state replay path lets you reproduce per-object pose alongside the
existing eye + head replay. v1.0's replay only drives a head-anchored avatar;
v1.1 adds a sidecar CSV that records every opted-in object's transform per
frame, and a `SceneStateReplayer` that plays it back over your live scene.

> **v1.2 update**: under deterministic replay (the default mode in v1.2),
> the live experiment runs again and spawns its own `Recordable` objects
> using **seeded ids** that match the recording's. `SceneStateReplayer`
> dynamically resolves these as they spawn (via `RecordableRegistry.Changed`)
> and drives their per-frame transforms. **Placeholder spawning is hard-gated
> off in deterministic mode** — the live spawns are guaranteed to provide a
> Recordable for every recorded id within a frame or two, so the v1.1
> placeholder fallback isn't needed. Set
> `ReplayController.deterministicReplay = false` to revert to placeholders
> for legacy non-seeded recordings.

This is the four-step researcher workflow.

## 1. Mark objects as recordable

Drop a `Recordable` component on every GameObject whose pose you want to
record. Two ways:

- **Per-object** — Inspector → `Add Component → Recordable`. The component
  serializes a stable GUID. Don't edit it manually; if you suspect a
  duplicate, click the **Regenerate Id** button on the inspector.
- **Bulk** — `Tools → Eye Tracking → Scene State → Bulk Add Recordable…`.
  Filter by tag / layer / has-collider, preview the matches, click "Add
  Recordable to N matched object(s)". Each add is a separate undo step.

Static walls/floors don't usually need Recordable — they don't move and
inflate the sidecar without yielding useful data. The bulk-add window
excludes common static names by default.

## 2. Configure a SceneRecordingProfile

`Project → Create → Eye Tracking → Scene Recording Profile`. Reference the
asset from your eye-tracking rig's `SceneStateRecorder`. Fields:

- **Discovery** — `RecordableComponentOnly` (default) records anything with a
  Recordable. `OrTag` / `OrLayer` widen the net. `ExplicitListOnly` ignores
  Recordable and records exactly the objects you list.
- **SampleEveryNthFrame** — 1 ≈ 90 Hz on a VIVE Focus Vision, 3 ≈ 30 Hz.
  Drop to 3 for non-VR-IK contexts to halve sidecar file size.
- **RecordParentId** — off by default. Adds a `ParentId` column for
  hierarchy-aware analysis. Ignored at replay time in v1.1.

## 3. Record

Add `SceneStateRecorder` next to your existing `SessionRecorder` /
`HMDDataCollector` / `EyeTracker` rig. On Play, it writes two new files
alongside the main eye CSV:

```
EyeTracking_<ts>.csv             ← unchanged, byte-compatible with v1.0
EyeTracking_<ts>_SceneState.csv  ← new sidecar, one row per (object × frame)
EyeTracking_<ts>_SceneEvents.csv ← only created if late-spawn / id-collision
                                   events occurred during the session
```

The sidecar's `# CoordinateOrigin` line is held until `SetCoordinateOrigin`
lands on the `HMDDataCollector` (typically from your env-builder, or use the
new `CoordinateOriginInitializer` for headless rigs). The first ~2 seconds of
rows are buffered in memory and re-normalized at flush — same grace-window
contract as the main CSV.

## 4. Replay

Open the **same scene** you used to record (or a copy with the same
Recordable ids preserved). Attach two components to a GameObject:

- `ReplayController` — owns the playback timeline; existing v1.0 component.
  Set `Data File Path` to your main CSV.
- `SceneStateReplayer` — new in v1.1. Discovers the sidecar automatically by
  appending `_SceneState.csv` to the main CSV path.

Press Play. The replayer subscribes to `OnFrameDisplayed` and drives every
matched Recordable's `transform.position` / `transform.rotation` / active
state per frame. Missing recorded ids (id was in the sidecar but no live
Recordable found) log a warning once per id then are ignored. Extra live
objects (Recordable in the scene but not in the sidecar) are left untouched —
replay is an overlay, not a takeover.

Diagnostics:

- `SceneStateDebugPanel` — drop on the same GameObject for an IMGUI HUD
  showing matched / missing / extras counts and per-frame applied counts.
- `[ReplayController] Recordable '<name>' has a non-kinematic Rigidbody …` —
  warning surfaced once per object at load time. Mark Rigidbodies kinematic
  and disable Animators on Recordables during replay; otherwise physics or
  animation will overwrite the replayed pose.

## Researcher script gating

> **v1.2 update**: under deterministic replay, your experiment scripts
> SHOULD run during replay — the toolbox feeds them recorded inputs
> (HMD pose, gaze, RNG state) and re-executes the same code paths.
> The gating pattern below is for **non-deterministic** replay, where
> the live experiment is suppressed and only gaze rays + scene state
> animate.

For non-deterministic replay (legacy or researcher opt-out), your
experiment scripts can opt out of running during replay so they don't
fight the replayer. The canonical pattern:

```csharp
private void Update()
{
    if (EyeLean.Replay.SceneState.ReplayMode.IsActive) return;
    // ... your normal experiment logic ...
}
```

`ReplayMode.IsActive` flips automatically when `ReplayController` enters
Loading / Processing / Playing / Paused state, and clears on Ready / Complete
/ destroy. No need to subscribe to events for the simple gate — the static
flag is sufficient.

## What the sidecar does NOT replay

- Spawned / despawned objects across the recording — the live scene's set of
  Recordables at replay time is authoritative. Dynamic spawns appear in
  `_SceneEvents.csv` for forensic analysis but the replayer does not
  Instantiate prefabs in v1.1.
- Hierarchy reparenting — the replayer writes world-space pose, not local.
- Animator-driven sub-rigs — disable the Animator on Recordable parents
  during replay. (Future v1.2 may include a sub-rig recording mode.)

## Migration from v1.0

- v1.0 main CSVs continue to play back exactly as before. `SceneStateReplayer`
  reports "No sidecar found; skipping object replay" and stays inert.
- The `eyelean_analysis` Python pipeline is unchanged — main CSV is byte-
  identical with v1.0. New analysis tools that consume the sidecar will land
  in v1.2.

## Round-17 hardware-iteration additions (2026-05-02)

### Anchor the replay environment to the recording's coordinate frame

Recorded eye-direction vectors are in **hardware-world-space**. The scene must
land its environment in that same world frame on replay, otherwise gaze rays
point at where targets WERE during recording and not where the scene spawned
them on this run. `DemoReplayBootstrapper` does this automatically via the
`anchorToRecording` toggle (default ON):

1. Reads the sidecar's `CoordinateOrigin` (or first-frame `HeadPosition` if
   the origin wasn't set during recording) for position.
2. Computes yaw from the first frame's `HeadRotation` projected to horizontal
   via `atan2(forward.x, forward.z)`.
3. Calls `EnvironmentGenerator.SetUserSpawnOverride(pos, yaw)` BEFORE
   `GenerateBasicRoom`. The room generates around the recorded user-spawn
   pose instead of the editor's default `Camera.main`.

Look for `[DemoReplayBootstrapper] Anchored env to recording: pos=(x,y,z), yaw=N°`
in the console as confirmation.

### Placeholder spawning for missing recorded objects

Recorded objects in the demo scene are spawned at runtime by the experiment
state machine, with fresh GUIDs each session. Replay starts fresh — those
GUIDs don't exist on the receiving side, so `SceneStateReplayer` can't drive
them by id-match. New behavior:

- `SceneStateReplayer.spawnPlaceholdersForMissingIds` (default ON) instantiates
  a sphere primitive for each recorded id without a live `Recordable` match,
  drives its transform from the sidecar, and parents it under a
  `ReplayPlaceholders` GameObject for clean cleanup.
- `placeholderSize` (default 0.22m), `placeholderColor` (default light gray),
  and `placeholderStaleFrameWindow` (default 90 frames) tune the visual.
- After each `OnFrameDisplayed`, placeholders whose last-applied frame is
  more than `placeholderStaleFrameWindow` behind the current playback frame
  are destroyed — honors recorded despawn boundaries without parsing
  `_SceneEvents.csv`.
- Colliders are stripped from placeholders so any researcher-side gaze
  raycasts running in parallel don't grab them.

### Recommended Replay GameObject Inspector setup

For the bundled SampleExperiment Replay scene:

| Component | Field | Setting |
|---|---|---|
| `ReplayController` | `Data File Path` | absolute path to main CSV |
| | `Auto Load On Start` | ☑ |
| | `Auto Play On Load` | ☑ |
| | `First Person Camera` | ☑ (default) |
| | `Use Head Anchored Ray Origin` | ☑ (default) |
| `SceneStateReplayer` | `Spawn Placeholders For Missing Ids` | ☑ (default) |
| | `Denormalize On Apply` | ☑ (default) |
| | `Interpolate Between Samples` | ☑ (default) |
| `DemoReplayBootstrapper` | `Anchor To Recording` | ☑ (default) |
| | `Static Object Count` | **0** (override default; otherwise demo distractors pollute the placeholder view) |
| | `Dynamic Object Count` | **0** (override default; same reason) |
| `SceneStateDebugPanel` | (no required config) | toggle key `J` |
