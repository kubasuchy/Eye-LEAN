# Eye_lean Documentation Hub

**EYE-LEAN** (Locomotion, Exploration, Action, and Navigation with Eye
Tracking): a behavioral-research toolkit for VR experiments.

## Audience

Anyone navigating the Eye_lean docs.

## Where to start

- New to Unity? [QUICKSTART.md](QUICKSTART.md) — install, open the
  project, hit Play, build an APK.
- Building an APK? [BUILD_GUIDE.md](BUILD_GUIDE.md).
- End-to-end walkthrough (install → calibrate → record → replay →
  analyze)?
  [Researcher Guide](../Eye_lean_Unity_Project/Eye_lean/docs/RESEARCHER_GUIDE.md).

---

## Choosing a starting point

The shipped APK contains one experiment: `SampleExperiment`. Read it
end-to-end and copy patterns into your own scene. The Skeleton
template is a separate developer-side scaffold materialized via
`VR Experiment > New Skeleton Scene` in the editor; it is not part of
the APK build flow.

| Starting point | What it gives you | When to pick |
|---|---|---|
| [SampleExperiment](SAMPLE_EXPERIMENT.md) (ships in APK) | 4-phase battery (FreeExploration / VisualSearch / CountingTask / ChangeDetection) with full source. | Worked example to adapt. |
| [Skeleton template](SKELETON.md) (editor-side) | Trial loop scaffold (ITI → Platform → Fixation → ExperimentalPhase). Implement `IExperimentPhaseHandler`. | Minimal scaffold for a custom experiment. |

Both run on the same recording, replay, and RIPA layers below.

---

## Component manuals

### Recording

| Component | Manual | What it does |
|---|---|---|
| `SessionRecorder` | [SESSION_RECORDER.md](SESSION_RECORDER.md) | Per-frame CSV + `RegisterMetric` API + custom metadata. |
| `HMDDataCollector` | [HMD_DATA.md](HMD_DATA.md) | Head pose, FPS, `# CoordinateOrigin` header. |
| `EyeTracker` | [EYE_TRACKER.md](EYE_TRACKER.md) | Gaze, vergence, pupil, gaze-target dispatch. |
| `RIPAMonitor` | [RIPA_MONITOR.md](RIPA_MONITOR.md) | Real-time RIPA2 cognitive-load index. |

### Replay

| Component | Manual | What it does |
|---|---|---|
| `SceneStateRecorder` + `Recordable` | [SCENE_STATE.md](SCENE_STATE.md) | Per-frame object position/rotation/active sidecar. |
| `SceneEventRecorder` | [SCENE_EVENTS.md](SCENE_EVENTS.md) | Discrete event sidecar (instructions, trial boundaries, custom events). |
| Deterministic replay | [REPLAY.md](REPLAY.md) | Re-runs the live experiment against recorded inputs. |

### Calibration

| Component | Manual | What it does |
|---|---|---|
| Calibrator + `EyeTrackingProfile` | [CALIBRATION.md](CALIBRATION.md) | 5-test calibrator + per-user profile JSON. |
| Post-hoc correction | [POST_HOC_CORRECTION.md](POST_HOC_CORRECTION.md) | Apply a profile to recorded CSVs in Python. |

### Experiment

| Component | Manual | What it does |
|---|---|---|
| `ExperimentUI` | [EXPERIMENT_UI.md](EXPERIMENT_UI.md) | World-space instruction + progress panels with auto-recording. |
| Skeleton overview | [SKELETON.md](SKELETON.md) | Wizard, scene flow, integration table. |
| Skeleton — trials | [SKELETON_TRIALS.md](SKELETON_TRIALS.md) | `TrialManager`, `TrialConfiguration`, `IExperimentPhaseHandler`. |
| Skeleton — agents | [SKELETON_AGENTS.md](SKELETON_AGENTS.md) | `AgentManager` + Rocketbox install. |
| Skeleton — environments | [SKELETON_ENVIRONMENT.md](SKELETON_ENVIRONMENT.md) | `EnvironmentBuilder`, procedural rooms. |
| Skeleton — phase contract | [SKELETON_HANDLER.md](SKELETON_HANDLER.md) | Implementing `IExperimentPhaseHandler`. |
| SampleExperiment | [SAMPLE_EXPERIMENT.md](SAMPLE_EXPERIMENT.md) | 4-phase battery shipping at build-2. |

### Operations

| Topic | Manual |
|---|---|
| Quick start (Unity novice) | [QUICKSTART.md](QUICKSTART.md) |
| APK build + scene flow | [BUILD_GUIDE.md](BUILD_GUIDE.md) |
| Troubleshooting | [TROUBLESHOOTING.md](TROUBLESHOOTING.md) |

### Python analysis

`eyelean_analysis/README.md` is the Python-side entry point. The
notebooks under `eyelean_analysis/notebooks/examples/` start with a
bootstrap cell that auto-locates a recorded session, so a fresh
clone runs end-to-end without an existing recording.

---

## Manual template

Component manuals under `docs/<COMPONENT>.md` follow:

1. **What it is** — one paragraph.
2. **When you'd use it** — user story.
3. **How to use it** — minimal copy-paste snippet.
4. **API reference** — public methods with `file:line` citations.
5. **Integration** — feeds and consumers.
6. **Patterns and gotchas.**
7. **References.**

Add a row to the table above when you add a new manual.
