# Toolbox Tuning Convention

## The problem this convention solves

Eye-LEAN is a research **toolkit**: researchers clone the repo and embed
components like `NBackStimulusPanel`, `RIPAGauge`, `WorldInstructionPanel`,
`MainMenuPanel`, etc. into their own scenes. Many of these components have
parameters that need consistent, toolbox-wide defaults — values from the
research literature (Jayawardena 2025 RIPA2 cutoffs, paper-specified font
sizes, etc.) or values the toolbox authors tune across versions.

Unity's `[SerializeField]` model **fossilizes** these values into the
`.unity` scene file when a component is first added. Once that happens:

- Updating the C# default does NOT propagate to existing scenes
- A researcher pulling a newer Eye-LEAN release silently gets the old values
  unless they re-run the scene-setup wizard (which would lose their other
  scene customizations)
- CSV data can be contaminated by stale detector parameters with no
  visible warning

This is the toolbox version of the
[lever-vs-screw problem](https://en.wikipedia.org/wiki/Two-by-six_(architecture))
in API design: don't put a screw (per-instance scene value) where a lever
(toolbox-wide knob) belongs.

## The three buckets

For every `[SerializeField]` you write, ask: **"If a researcher pulls a
newer version of Eye-LEAN, should they automatically get my updated value?"**

### Bucket 1: ScriptableObject

The answer is **yes**. The value is part of the toolbox's identity — a
paper-spec parameter, a visual style, a default behavior we maintain
centrally.

**Examples:**

- RIPA2 filter cutoffs (Jayawardena 2025 §5.2)
- Stimulus font sizes (paper-spec ~2° of visual angle)
- Cognitive load HUD color scheme
- Dwell time for gaze interaction (UX consistency)
- Calibration target spacing (paper-spec)
- Default panel distances / sizes for VR-ergonomic reasons

**Pattern:**

```csharp
// In Assets/Scripts/EyeTracking/Metrics/RIPADetectorConfig.cs
[CreateAssetMenu(fileName = "RIPADetectorConfig", menuName = "Eye-LEAN/RIPA Detector Config")]
public class RIPADetectorConfig : ScriptableObject
{
    [Header("RIPA2 (Jayawardena 2025 §5.2)")]
    public double vlfCutoffHz = 0.29;
    public double lfCutoffHz = 4.0;
    public float bufferSeconds = 4f;
    public float smoothingSeconds = 1.5f;
    // ... 11 more paper-spec fields
}

// In Assets/Settings/RIPADetectorConfig_PaperDefault.asset
// (created via Assets > Create > Eye-LEAN > RIPA Detector Config menu)

// In the consumer component:
public class RIPAMonitor : MonoBehaviour
{
    [Tooltip("Filter parameters. Defaults reference Jayawardena 2025; clone the asset to override.")]
    [SerializeField] private RIPADetectorConfig detectorConfig;
    
    void Start()
    {
        if (detectorConfig == null)
        {
            Debug.LogError("[RIPAMonitor] No RIPADetectorConfig assigned. Assign the canonical asset from Assets/Settings/RIPADetectorConfig_PaperDefault.asset");
            return;
        }
        // ... use detectorConfig.vlfCutoffHz, detectorConfig.lfCutoffHz, etc.
    }
}
```

When the toolbox author tunes a paper parameter, they edit the
`.asset` file. The change is in the repo. All researchers who pull
inherit the update automatically — their scenes just reference the
asset and pick up the new values.

### Bucket 2: `[SerializeField]` on the component

The answer is **no** — this value is legitimately per-scene.

**Examples:**

- Inspector references to other GameObjects in the same scene
  (e.g., `[SerializeField] private NBackTaskManager taskManager`)
- Scene-specific corner offsets when the scene's geometry differs
- Participant ID defaults that researchers override per-session
- File paths researchers configure per-deployment
- Debug toggles that should default off but researchers might enable

**Pattern:**

```csharp
public class NBackExperimentController : MonoBehaviour
{
    // OK — this is a scene-level wiring decision, not a toolbox tunable
    [SerializeField] private NBackTaskManager taskManager;
    
    // OK — autoStart is a per-scene UX choice
    [SerializeField] private bool autoStart = false;
}
```

### Bucket 3: `const` / `static readonly`

The answer is **never** — this value is a mathematical or algorithmic
constant that researchers should not tune.

**Examples:**

- Polynomial orders in detector math (`vlfPolyOrder = 2` per the paper —
  changing it breaks the algorithm, not its tuning)
- Mathematical constants (`Mathf.PI` analogs)
- Hardcoded format strings that algorithms parse

**Pattern:**

```csharp
public class RIPA2Analyzer
{
    // No serialization — researchers should not change this
    private const int VlfPolyOrder = 2;
    private const int LfPolyOrder = 4;
}
```

## Decision flowchart

```
Researcher pulls new Eye-LEAN. Should this value update?
│
├── YES (paper-spec, style, default behavior)
│       → Bucket 1: ScriptableObject
│
├── NO  (researcher's per-scene choice)
│       → Bucket 2: [SerializeField] on component
│
└── It's not tunable at all (algorithm correctness)
        → Bucket 3: const / static readonly
```

## Bucket 1 implementation checklist

When you create a new ScriptableObject for tunables:

- [ ] **File location:** `Assets/Scripts/<Subsystem>/<Name>Config.cs`
- [ ] **Class:** `public class XConfig : ScriptableObject`
- [ ] **Menu attribute:** `[CreateAssetMenu(fileName = "XConfig", menuName = "Eye-LEAN/X Config")]`
- [ ] **Default asset:** `Assets/Settings/XConfig_PaperDefault.asset` created and populated with the values that were previously SerializedField defaults
- [ ] **Versioning hint:** if a future paper update changes defaults, the new asset can be `XConfig_PaperDefault_v2.asset` so researchers can pin a version
- [ ] **Field organization:** use `[Header]` groupings matching paper sections; `[Tooltip]` with the paper citation for each field
- [ ] **Consumer behavior:** component logs an error if its `XConfig` reference is null, pointing the researcher to the canonical asset path

## Bucket 1 implementation: how researchers customize

A researcher who wants to override a Bucket 1 default doesn't edit the
canonical asset (which would create a dirty diff against the repo). They:

1. **Duplicate** the canonical asset (Ctrl+D in the Project window)
2. **Rename** the copy (e.g., `RIPADetectorConfig_MyExperiment.asset`)
3. **Edit** the copy's values
4. **Assign** the copy on their scene component's Inspector field

This is the same pattern as Unity Render Pipeline assets, post-processing
profiles, and other production-grade asset-driven systems. Pulling new
toolbox updates leaves their customized asset alone; only the canonical
default updates.

## Audit results (2026-05-28)

A full audit identified **62 fields across 9 components** that currently
violate Bucket 1 and should migrate to ScriptableObjects:

| Proposed Asset | Component(s) | Field count |
|---|---|---|
| `RIPADetectorConfig` | `RIPAMonitor` | 15 |
| `RIPAGaugeVisualConfig` | `RIPAGauge`, `RIPAOverlay` | 7 |
| `InstructionPanelConfig` | `WorldInstructionPanel` | 8 |
| `NBackPanelConfig` | `NBackStimulusPanel`, `NBackHUDController` | 6 |
| `MazeRenderingConfig` | `MazeEnvironmentRenderer` | 11 |
| `MazeHUDConfig` | `MazeHUDController` | 1 |
| `MainMenuConfig` | `MainMenuPanel`, `MenuHeadAnchor` | 14 |
| `CalibrationUIConfig` | `CalibrationWorldUI`, `CalibrationSessionManager` | 16 |
| `ReplayValidationConfig` | `ReplayController` | 1 |

The full line-by-line audit is in the session's history. Migration of
these 62 fields is tracked as a separate task (Task #35).

## Bucket 2 sanity checks

Stay in Bucket 2 — do NOT promote to ScriptableObject — if any of these
are true:

- The field is an **inspector reference** to a GameObject/Component in
  the same scene (`Camera`, `TextMeshPro`, etc.). ScriptableObjects can't
  hold scene references.
- The field is **genuinely per-scene** (corner placement depends on the
  scene's geometry; participant ID depends on session, etc.).
- The field is a **debug toggle** that the researcher enables temporarily.
- The field would change so rarely that the cost of asset management
  outweighs the benefit (e.g., a one-off threshold used in exactly one
  place).

## Existing ScriptableObjects (already correct)

These six ScriptableObjects are already implemented and need no changes:

- `NBackConfig.cs` — N-back experiment design (block list, stimulus alphabet, target ratios)
- `MazeConfig.cs` — Navigation maze configuration (grid size, block sequences, landmark conditions)
- `DataExportSettings.cs` — CSV export tuning (sample-rate downsampling, file naming)
- `ExperimentMetadataSchema.cs` — Metadata schema definitions
- `SceneRecordingProfile.cs` — Recording setup defaults
- `TrialConfiguration.cs` — Skeleton trial templates

Use these as reference examples when creating new ScriptableObjects.

## When in doubt

Default to **Bucket 1** for anything that resembles a paper-spec or
visual-style value. The cost of an over-eager promotion to
ScriptableObject is minor (one extra asset to assign). The cost of an
under-promotion is silent data contamination across researcher repos.
