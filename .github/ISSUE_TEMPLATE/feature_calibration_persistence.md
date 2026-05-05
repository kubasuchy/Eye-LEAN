---
name: Calibration Profile Persistence
about: Feature request for persisting calibration data
title: "[FEATURE] Calibration Profile Persistence"
labels: enhancement, calibration
assignees: ''
---

## Feature Request: Calibration Profile Persistence

### Current Behavior
The calibration system in Eye_lean v1.0 operates as a validation mode that:
- Measures eye tracking accuracy against known targets
- Reports accuracy metrics
- Exports ground truth CSV data
- Does NOT persist calibration data
- Does NOT apply gaze offset corrections

### Requested Feature
Implement calibration profile persistence to:
1. Save calibration offsets to a profile file
2. Load calibration profiles at session start
3. Apply gaze corrections based on calibration data
4. Support per-participant calibration profiles

### Proposed Implementation

#### CalibrationProfile.cs
```csharp
[System.Serializable]
public class CalibrationProfile
{
    public string participantID;
    public DateTime calibrationDate;
    public Vector3 gazeOffset;
    public float horizontalCorrection;
    public float verticalCorrection;
    public float accuracyBeforeCorrection;
    public float accuracyAfterCorrection;
}
```

#### Files to Modify
- `CalibrationSessionManager.cs` - Add profile save/load
- `GroundTruthValidator.cs` - Calculate correction offsets
- `EyeTrackingProfile.cs` / `ActiveProfile.cs` / `EyeTrackingProfileApi.cs` - Apply corrections to gaze data (post-RC, in `Assets/Scripts/EyeTracking/Configuration/`)
- Create `CalibrationProfileManager.cs` - Profile persistence

#### Storage Location
- `Application.persistentDataPath/Calibration/`
- Format: JSON or binary serialization
- Naming: `{participantID}_calibration.json`

### Acceptance Criteria
- [ ] Calibration offsets are calculated from validation data
- [ ] Profiles can be saved to persistent storage
- [ ] Profiles can be loaded at experiment start
- [ ] Gaze corrections are applied in real-time
- [ ] Per-eye calibration is supported (optional)
- [ ] Profile format is documented

### References
- Current calibration code: `Assets/Scripts/EyeTracking/Calibration/`
- Documentation: `docs/CALIBRATION.md`

### Priority
Post v1.0 feature - Not required for initial release.

### Related Issues
- FreeExploration test implementation
- Vergence-based depth validation
- Per-eye calibration support
