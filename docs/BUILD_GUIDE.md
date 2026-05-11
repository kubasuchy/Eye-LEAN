# Eye_lean Build Guide

Step-by-step instructions for building the Eye_lean APK for the VIVE Focus
Vision and deploying it to the device. For first-time environment setup (Unity
install, package config, scene scaffolding), see
[`SETUP.md`](../Eye_lean_Unity_Project/Eye_lean/docs/SETUP.md). For the
end-to-end researcher walkthrough, see
[`RESEARCHER_GUIDE.md`](../Eye_lean_Unity_Project/Eye_lean/docs/RESEARCHER_GUIDE.md).

## Prerequisites

- Unity 6000.3.9f1 with Android Build Support module installed via
  Unity Hub
- Android SDK API Level 25+ (Android 7.1+)
- Android NDK r23b or later (bundled with Unity)
- VIVE OpenXR 2.5.1
- HTC VIVE Focus Vision in Developer Mode
- ADB on the development machine
- Required Unity packages (declared in `Packages/manifest.json`):
  - Universal Render Pipeline (URP)
  - XR Plugin Management
  - OpenXR Plugin
  - XR Interaction Toolkit
  - Input System

## Build configuration verification

Before building, verify these settings.

### 1. Platform selection

1. Open **File > Build Settings**.
2. Select **Android**.
3. Click **Switch Platform** if Android is not already active.

#### Verify

The Unity logo appears next to **Android** in the **Build Settings**
platform list.

### 2. Player settings

Open **Edit > Project Settings > Player** and confirm the Android
tab matches:

| Setting | Required value |
|---------|----------------|
| Company Name | RutgersVCL |
| Product Name | Eye_lean |
| Package Name | com.RutgersVCL.Eye_lean |
| Minimum API Level | 25 (Android 7.1) |
| Target API Level | Automatic (highest) |
| Scripting Backend | IL2CPP |
| Target Architectures | ARM64 |
| Active Input Handling | Input System Package (New) |

#### Verify

Each row above matches the Inspector value byte-for-byte (the package
name in particular controls the data path used by `adb pull`).

### 3. XR Plugin Management

Open **Edit > Project Settings > XR Plug-in Management** and on the
Android tab:

- [x] OpenXR enabled

Under **XR Plug-in Management > OpenXR**:

- [x] Eye Gaze Interaction (required for eye tracking)
- [x] VIVE XR Support (recommended)
- [x] Hand Tracking (optional)

#### Verify

The OpenXR runtime selector for Android shows VIVE OpenXR and **Eye
Gaze Interaction** is checked.

### 4. Scripting define symbols

Open **Edit > Project Settings > Player > Android > Other Settings >
Scripting Define Symbols** and confirm:

```
USE_OPENXR;USE_INPUT_SYSTEM_POSE_CONTROL;USE_STICK_CONTROL_THUMBSTICKS
```

#### Verify

The Console reports a recompile completing without errors after any
change to the defines.

### 5. Graphics settings

1. Confirm URP is configured under **Edit > Project Settings >
   Graphics** and a URP Asset is assigned.
2. Use Single Pass Instanced rendering for VR performance (set on the
   URP Asset).

#### Verify

The Console contains no `URP requires...` warnings on entering Play
mode.

## Build process

### Step 1: Pre-build checklist

- [ ] Platform set to Android
- [ ] XR settings verified (steps 1-5 above)
- [ ] ReplayController disabled or absent in production scenes
- [ ] `SampleExperimentController` enabled in the experiment scene
- [ ] No Console errors in Unity

### Step 2: Build the APK

1. Open **File > Build Settings**.
2. Confirm **MainMenu**, **CalibrationScene**, and **SampleExperiment**
   are in the **Scenes In Build** list at indexes 0, 1, 2.
3. Click **Build** (or **Build And Run** when a device is connected).
4. Choose an output location (for example `Builds/Eye_lean.apk`).
5. Wait for the build to complete.

#### Verify

- The APK is smaller than 500 MB.
- The build completes with no errors and no Console warnings about
  missing references.

## Deployment to VIVE Focus Vision

### Using ADB

1. Enable Developer Mode on the VIVE Focus Vision:
   - **Settings > Developer > Enable Developer Mode**.
   - Enable USB Debugging.
2. Connect the device:
   ```bash
   adb devices
   ```
3. Install the APK:
   ```bash
   adb install -r Eye_lean.apk
   ```
4. Launch the app:
   ```bash
   adb shell am start -n com.RutgersVCL.Eye_lean/com.unity3d.player.UnityPlayerActivity
   ```

#### Verify

`adb devices` lists the headset with status `device`. After launch,
the headset shows the `MainMenu` scene and
`adb logcat -s Unity` reports
`[EyeTracker] Eye tracker initialized` within five seconds.

### Retrieving data

Eye tracking data is saved to the device's persistent data path:

```bash
# List files
adb shell ls /sdcard/Android/data/com.RutgersVCL.Eye_lean/files/

# Pull all data
adb pull /sdcard/Android/data/com.RutgersVCL.Eye_lean/files/ ./collected_data/

# Pull specific file
adb pull /sdcard/Android/data/com.RutgersVCL.Eye_lean/files/EyeTracking_2024-01-15_14-30-00.csv ./
```

## Troubleshooting build issues

### "XR Plugin not initialized"

1. Verify OpenXR is enabled in XR Plugin Management.
2. Check that the Eye Gaze Interaction feature is enabled.
3. Confirm the device has eye-tracking hardware.

### Build fails with IL2CPP errors

1. Delete the `Library/` folder and rebuild.
2. Update the Android NDK via Unity Hub.

### APK too large (> 500 MB)

1. Inspect the build for uncompressed assets.
2. Verify texture compression is enabled in the Inspector for each
   imported texture.
3. Remove unused packages and assets.

### Eye tracking not working on device

1. Verify the `USE_OPENXR` scripting define is present.
2. Confirm device-side eye-tracking calibration has been completed.
3. Confirm the app has eye-tracking permissions.

### Debug build

For development and debugging:

1. Enable **Development Build** in **Build Settings**.
2. Enable **Script Debugging** for breakpoints.
3. Enable **Autoconnect Profiler** for performance monitoring.

#### Verify

The APK installs and launches with the **Development Build** banner
visible in the editor on connect, and breakpoints hit when attached
via Visual Studio or Rider.

## Scene configuration for builds

### Production build (recording)

Enabled:

- `SampleExperimentController`
- `EyeTracker` + `HMDDataCollector` + `SessionRecorder`
- `EnvironmentGenerator`

Disabled (or absent from production scenes):

- `ReplayController` (editor-only)
- Any replay UI

### Replay mode (editor only)

Enabled:

- `ReplayController`
- `DemoReplayBootstrapper`
- Replay visualizer

Disabled:

- `SampleExperimentController` is left in the scene but is driven by
  recorded inputs; do not remove it. See
  [`RESEARCHER_GUIDE.md`](../Eye_lean_Unity_Project/Eye_lean/docs/RESEARCHER_GUIDE.md)
  for the full deterministic-replay contract.

## Version information

Current build configuration:

- Unity Version: 6000.3.9f1
- App Version: 1.4.0
- Android Bundle Version Code: 1
- Target: VIVE Focus Vision

## Post-build verification

After installing on the device, confirm each of the following:

| Test | Expected result |
|------|-----------------|
| App launches | No crash, splash screen visible |
| Eye tracking initializes | `[EyeTracker] Eye tracker initialized` in `adb logcat -s Unity` |
| Calibration session | All five calibration tests complete (Fixation, Smooth Pursuit, Saccade, Tuning, Verification) |
| Sample experiment | Four phases run sequentially (FreeExploration, VisualSearch, CountingTask, ChangeDetection) |
| CSV export | A new `EyeTracking_*.csv` appears in the persistent data folder |
| Data retrieval | `adb pull /sdcard/Android/data/com.RutgersVCL.Eye_lean/files/ ./` succeeds |

## Related documentation

- [SETUP.md](../Eye_lean_Unity_Project/Eye_lean/docs/SETUP.md) — initial project setup
- [CALIBRATION.md](CALIBRATION.md) — calibration system
- [TROUBLESHOOTING.md](TROUBLESHOOTING.md) — common issues and solutions
