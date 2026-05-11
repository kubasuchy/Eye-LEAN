# Setup Guide

Step-by-step environment setup for the Eye_lean Unity project: hardware,
software prerequisites, Unity project configuration, device pairing, a first
device build, and data retrieval. For the build-only flow (after setup is
done), see [`../../../docs/BUILD_GUIDE.md`](../../../docs/BUILD_GUIDE.md).

## Prerequisites

- Unity 6000.3.9f1 (with Android Build Support module)
- VIVE OpenXR 2.5.1
- Android SDK API 32+ and ADB
- HTC VIVE Focus Vision (or another OpenXR eye-tracking headset)
- Python 3.10+ (only if running the Python analysis side)

---

## Table of contents

1. [Hardware requirements](#hardware-requirements)
2. [Software requirements](#software-requirements)
3. [Unity project setup](#unity-project-setup)
4. [Device configuration](#device-configuration)
5. [Building for device](#building-for-device)
6. [Retrieving data](#retrieving-data)
7. [Troubleshooting](#troubleshooting)

---

## Hardware requirements

### Supported devices

| Device | Eye Tracking Rate | Status |
|--------|------------------|--------|
| **HTC VIVE Focus Vision** | 120 Hz | Fully Supported |
| Meta Quest 3 | N/A | Not supported (no native eye tracking) |

Other OpenXR eye-tracking headsets should work via the
`IEyeTracker` + `EyeTrackerFactory` abstraction but have not been tested.

### Primary device: HTC VIVE Focus Vision

Specifications:

- Display: 2448 x 2448 per eye
- Refresh rate: 90 Hz
- Eye tracking: 120 Hz, binocular
- Platform: Android 12 (standalone)
- SDK: VIVE OpenXR

Required device state before building:

- Firmware updated to the latest version
- Eye tracking enabled in device settings
- Per-user eye calibration completed in the device's built-in
  calibrator

---

## Software requirements

### Development environment

| Software | Version | Purpose |
|----------|---------|---------|
| **Unity** | 6000.3.9f1 | Game engine |
| **Render Pipeline** | URP 17.3.0 | Universal Render Pipeline |
| **Android SDK** | API 32+ | Android build support |
| **ADB** | Latest | Device communication |

### Unity packages

| Package | Version | Installation |
|---------|---------|--------------|
| com.htc.upm.vive.openxr | 2.5.1 | GitHub (see below) |
| com.unity.xr.openxr | 1.16.1 | Package Manager |
| com.unity.inputsystem | 1.16.0 | Package Manager |
| com.unity.render-pipelines.universal | 17.3.0 | Package Manager |

---

## Unity project setup

This section assumes a fresh project. To use the existing Eye_lean
project as-is, clone the repository and open
`Eye_lean_Unity_Project/Eye_lean/` in Unity 6000.3.9f1; the project
already has every step below applied.

### Step 1: Create a new project

1. Open Unity Hub.
2. Create a new project with the **3D (URP)** template.
3. Select Unity 6000.3.9f1.

#### Verify

The new project opens without errors and the URP asset is present
under `Assets/Settings/`.

### Step 2: Install the VIVE OpenXR package

The VIVE OpenXR package must be installed from GitHub (it is not in
the Unity Package Manager registry).

1. Open `Packages/manifest.json`.
2. Add the package to `dependencies`:
   ```json
   "com.htc.upm.vive.openxr": "https://github.com/ViveSoftware/VIVE-OpenXR.git?path=com.htc.upm.vive.openxr#2.5.1"
   ```
3. Save the file and return to Unity. The package installs
   automatically.

#### Verify

**Window > Package Manager** lists `VIVE OpenXR Plugin` at version
2.5.1 under **In Project**.

### Step 3: Configure XR settings

1. Open **Edit > Project Settings > XR Plug-in Management**.
2. Click **Install XR Plugin Management** if it is not yet installed.
3. Under the **Android** tab, check **OpenXR**.
4. Click the gear icon next to OpenXR.
5. Under **Interaction Profiles**, add **VIVE Focus 3 Controller
   Interaction Profile**.
6. Under **OpenXR Feature Groups**, enable:
   - **VIVE XR Eye Tracker (Beta)**
   - **VIVE XR Facial Tracking** (optional)

#### Verify

The OpenXR runtime selector for Android shows VIVE OpenXR and
**VIVE XR Eye Tracker (Beta)** is checked.

### Step 4: Add scripting define symbol

1. Open **Edit > Project Settings > Player**.
2. Expand **Other Settings > Scripting Define Symbols**.
3. Add: `USE_OPENXR`.
4. Click **Apply**.

#### Verify

The Console reports a recompile completing without errors after the
define is applied.

### Step 5: Configure the Android build

1. Open **File > Build Settings**.
2. Select **Android**.
3. Click **Switch Platform**.
4. Open **Player Settings** and set:
   - **Company Name**: organization name
   - **Product Name**: app name
   - **Package Name**: `com.YourOrg.YourApp`
   - **Minimum API Level**: Android 10.0 (API 29)
   - **Target API Level**: Android 12 (API 32)

#### Verify

**File > Build Settings** shows **Android** as the active platform
(the Unity logo appears next to it).

### Step 6: Import toolkit scripts

Copy the `Assets/Scripts/EyeTracking/` folder from the Eye_lean
repository into the new project.

#### Verify

The Console reports no compile errors after the copy completes and
`Assets/Scripts/EyeTracking/Components/EyeTracker.cs` is visible.

---

## Device configuration

### VIVE Focus Vision setup

1. Power on the headset.
2. Complete initial setup (Wi-Fi, account, and so on).
3. Enable Developer Mode: **Settings > Advanced > Developer options >
   Enable**.
4. Run the device's eye tracking calibration:
   **Settings > Eye tracking > Calibrate** and follow the on-screen
   instructions.
5. Connect via ADB:
   ```bash
   adb devices
   ```

#### Verify

`adb devices` lists the headset's serial with status `device` (not
`unauthorized` or `offline`).

### Scene setup

Required hierarchy in any scene that records gaze:

```
Scene
- CameraRig (Empty GameObject — optional parent, identity rotation)
  - Main Camera (Tracked Pose Driver auto-attached at scene load by
                 HmdPoseDriverBootstrap; do not add one manually)
- EyeTrackingManager (Empty GameObject)
  - EyeTracker            (Assets/Scripts/EyeTracking/Components/EyeTracker.cs)
  - HMDDataCollector      (Assets/Scripts/EyeTracking/Components/HMDDataCollector.cs)
  - SessionRecorder       (Assets/Scripts/EyeTracking/Components/SessionRecorder.cs)
- EnvironmentGenerator (component, optional)
- Directional Light
```

`EyeTracker` settings (Inspector):

- **Debug Mode** — enable for in-editor visualization (gaze rays,
  vergence point).
- **Vergence Method** — pick the vergence-calculation algorithm
  (Simple, Paper, DepthExtension).

`SessionRecorder` settings (Inspector):

- **CSV Export Enabled** — ON to record CSV.

Data quality tracking is built into `SessionRecorder` via
`DataQualityMetrics` (`Assets/Scripts/EyeTracking/Data/`). Defaults:

- **Blink Threshold** — eye openness below this is a blink (0.2).
- **Stuck Ray Threshold** — direction change below this suggests a
  stuck ray (0.001).
- **Stuck Ray Frames** — frames without movement before flagging
  stuck (60).

### Calibration system setup (optional)

To add eye-tracking validation tests to a custom scene:

1. Right-click the Hierarchy and choose **Create Empty**. Name the
   GameObject `CalibrationManager`.
2. Add the `CalibrationSessionManager` component
   (`Assets/Scripts/EyeTracking/Calibration/CalibrationSessionManager.cs`).
3. Configure in the Inspector:
   - **Session Configuration**
     - Require Participant ID — ON (shows the ID input at start)
   - **Test Selection**
     - Include Fixation Test — ON (7 targets, 2 s each)
     - Include Smooth Pursuit Test — ON (figure-8 moving target)
     - Include Saccade Test — ON (rapid target switching)
     - Include Free Exploration — OFF (optional)
   - **References**
     - Eye Tracker — optional, auto-detected
4. Press Play (or build). The component auto-creates:
   - `CalibrationWorldUI` — VR floating panel for instructions
   - Test runners (Fixation, SmoothPursuit, Saccade)
   - Ground-truth recording

Session flow:

```
Setup (Participant ID) -> Instructions -> Tests -> Results
```

Data output:

- Ground-truth CSV:
  `{ParticipantID}_GroundTruth_S{Session}_{timestamp}.csv`
- Includes gaze-error measurements for each test type.

For the calibrator's full algorithm, see
[`../../../docs/CALIBRATION.md`](../../../docs/CALIBRATION.md).

#### Verify

After Play, the Console contains
`[CalibrationSessionManager] Session started` and the world-space
panel appears in front of the headset.

---

## Building for device

### Build and deploy

1. Open **File > Build Settings**.
2. Confirm the **Android** platform is selected.
3. Click **Build and Run**.
4. Choose an APK output location.
5. Wait for the build and automatic deployment to finish.

#### Verify

`adb shell pm list packages | grep <package>` returns the package
name; the app launches on the headset.

### Manual deployment

```bash
# Build APK first, then:
adb install -r YourApp.apk

# Launch app
adb shell am start -n com.YourOrg.YourApp/com.unity3d.player.UnityPlayerActivity
```

#### Verify

`adb logcat -s Unity` reports `[EyeTracker] Eye tracker initialized`
within five seconds of launch.

---

## Retrieving data

### Data location on device

```
/storage/emulated/0/Android/data/com.YourOrg.YourApp/files/
```

Files written there:

- `EyeTracking_YYYYMMDD_HHMMSS.csv` — main eye-tracking CSV
- `EyeTracking_YYYYMMDD_HHMMSS_SceneState.csv` — per-frame transforms
  sidecar
- `EyeTracking_YYYYMMDD_HHMMSS_SceneEvents.csv` — discrete-events
  sidecar
- `DebugLogs/eye_tracking_log_*.txt` — debug logs

### Pull data via ADB

```bash
# Pull all data files
adb pull /sdcard/Android/data/com.YourOrg.YourApp/files/ ./data/

# Pull a specific CSV
adb pull "/sdcard/Android/data/com.YourOrg.YourApp/files/EyeTracking_20241212_143052.csv" ./

# List files on device
adb shell ls /sdcard/Android/data/com.YourOrg.YourApp/files/
```

### View real-time logs

```bash
# Unity logs only
adb logcat -s Unity

# All logs (verbose)
adb logcat

# Filter for eye tracking
adb logcat | grep -i "eyetrack"
```

#### Verify

A pulled `EyeTracking_*.csv` opens in any text editor and contains
the standard `# Eye_lean Research Data Export` header followed by a
column line and at least one data row.

---

## Troubleshooting

### Eye tracking not working

Symptom: `IsAvailable = false`, no gaze data.

1. Verify eye-tracking calibration on the device.
2. Check that `USE_OPENXR` is in the Scripting Define Symbols.
3. Verify OpenXR features are enabled in XR settings (step 3 above).
4. Check the device log for errors:
   ```bash
   adb logcat -s Unity | grep -i "eye"
   ```

### Black screen on device

1. Check that URP is configured under
   **Edit > Project Settings > Graphics**.
2. Verify the Main Camera has a Tracked Pose Driver.
3. Check the XR Origin hierarchy.

### CSV not saving

Symptom: no CSV file created.

1. Verify `csvExportEnabled = true` on `SessionRecorder`.
2. Confirm the app has storage permissions (automatic for app-specific
   storage on Android 10+).
3. Inspect the debug log for write errors.

### Vergence point incorrect

Symptom: vergence visualization does not match gaze.

1. Re-run the device-side eye calibration.
2. Try a different vergence-calculation method on `EyeTracker`.
3. Confirm both eyes have valid data (`HasLeftOrigin` and
   `HasRightOrigin` both true).
4. Verify world-space vs. local-space coordinate handling matches the
   notes in `docs/ALGORITHMS.md`.

### Performance issues

1. Disable debug visualization in production builds.
2. Reduce CSV write frequency on `SessionRecorder`.
3. Profile via **Window > Analysis > Profiler** to find other
   bottlenecks.

---

## Python environment (analysis side)

For the recommended install (editable), see the root
[README](../../../README.md):

```bash
pip install -e ./eyelean_analysis            # core
pip install -e "./eyelean_analysis[all]"     # core + visualization + tests
```

For a minimal manual install:

```bash
pip install pandas matplotlib seaborn plotly scikit-learn
```

#### Verify

```python
import eyelean_analysis as ela
print(ela.__version__)
```

prints a non-empty version string.

---

## Quick-start checklist

- [ ] Unity 6000.3.9f1 installed
- [ ] VIVE OpenXR 2.5.1 package added
- [ ] `USE_OPENXR` define set
- [ ] OpenXR Eye Tracker feature enabled
- [ ] Android platform configured
- [ ] Device in Developer Mode
- [ ] Eye tracking calibrated on device
- [ ] ADB connected (`adb devices` shows the headset)
- [ ] Test build deployed
- [ ] Eye-tracking data visible in CSV after pulling via `adb pull`
