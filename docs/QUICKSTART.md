# Quick Start — for researchers new to Unity

If you've never opened Unity before, work through this page top to
bottom. It takes about 30 minutes the first time you do it. Once
you've done it, you can skip ahead to [`BUILD_GUIDE.md`](BUILD_GUIDE.md)
on subsequent runs.

## What you'll have at the end

- Unity installed.
- Eye_lean opened in the Unity Editor.
- `SampleExperiment.unity` running in the editor (no headset needed).
- The same APK installed on a VIVE Focus Vision (headset required for
  this last step).

## What you need

- A Mac, Windows, or Linux desktop with at least 16 GB of RAM and
  20 GB of free disk space.
- (For the headset step) A VIVE Focus Vision and a USB-C cable.
- (For the headset step) Permission from your IT department to enable
  Developer Mode on the headset.

You do **not** need to know C# or have used Unity before.

---

## Step 1 — Install Unity Hub

Unity Hub is a small launcher application. The actual Unity Editor
gets installed *through* Unity Hub. They are two different programs.

1. Go to <https://unity.com/download> and download Unity Hub.
2. Open the file you downloaded and follow the installer prompts.
   Accept the defaults.
3. Open Unity Hub. You'll be asked to create a Unity account or sign
   in. Either is fine — a free account works.

You should now see the Unity Hub window with empty **Projects** and
**Installs** tabs.

## Step 2 — Install the right Unity Editor version

Eye_lean is built with **Unity 6000.3.9f1**. Other versions will
either fail to open the project or break things subtly.

1. In Unity Hub, click the **Installs** tab on the left.
2. Click **Install Editor** in the top right.
3. Click **Archive** on the left of the dialog, then **Download
   archive** — this opens a webpage.
4. On the webpage, find **Unity 6000.3.9** in the list and click the
   **Unity Hub** button next to it. Your browser will hand the
   download back to Unity Hub.
5. Back in Unity Hub, the install dialog now opens with the right
   version selected. **Check these boxes** before continuing:
   - **Android Build Support**
     - **Android SDK & NDK Tools** (sub-option)
     - **OpenJDK** (sub-option)
   - **Documentation** (optional, but useful)
6. Click **Install**. This downloads about 4 GB and takes 10–20
   minutes depending on your connection.

When it's done, the **Installs** tab shows `6000.3.9f1` with a green
Android logo next to it.

## Step 3 — Clone Eye_lean

In a terminal (Mac/Linux: Terminal app; Windows: Git Bash or
PowerShell):

```bash
git clone https://github.com/<the-eye_lean-repo>.git
cd Eye_lean
```

If you don't have `git`, install it from <https://git-scm.com> first.

## Step 4 — Open the project in Unity

1. In Unity Hub, click the **Projects** tab.
2. Click the **Add** dropdown in the top right and choose **Add
   project from disk**.
3. Navigate to `Eye_lean/Eye_lean_Unity_Project/Eye_lean` (note: two
   nested folders named `Eye_lean`) and click **Add Project**.
4. The project appears in the list. Click it once.
5. Unity asks which editor version to use. Pick **6000.3.9f1**.
6. Click **Open**.

The first open takes 5–10 minutes. Unity is importing every asset.
You'll see a progress bar; wait it out. **Do not click anything
until it finishes.**

When it's done, you'll see the Unity Editor with three big areas:

- **Scene** view (top middle) — a 3D preview of the current scene.
- **Game** view (next to Scene, behind a tab) — what the user sees.
- **Project** window (bottom) — the file tree of `Assets/`.
- **Hierarchy** window (left) — the GameObjects in the current scene.
- **Inspector** window (right) — shows fields of whatever you have
  selected.

## Step 5 — Open SampleExperiment and press Play

1. In the **Project** window, navigate to
   `Assets/Scenes/`.
2. Double-click `SampleExperiment.unity`.
3. The **Scene** view changes to show a room with cubes.
4. Click the **Play** button at the top of the editor (▶ triangle).

The editor enters Play mode (the toolbar tints blue). The **Game**
view shows what a participant would see. Click into the Game view
and use the keyboard / mouse to look around. The on-screen "Press
Start" prompt is gaze-driven in the headset; in the editor, you can
click it to advance.

To exit Play mode, click the **Play** button again. Unity discards
any in-Play-mode changes — that's normal.

## Step 6 — Try the calibrator

1. Open `Assets/Scenes/CalibrationScene.unity`.
2. Press Play.
3. The five calibration tests (Fixation, Saccade, Smooth Pursuit,
   Tuning, Verification) run end-to-end. You won't see meaningful
   data without a headset, but everything compiles and runs.

If the calibrator runs without errors in the **Console** window
(bottom; toggle it via **Window > General > Console**), you're done
with the editor side.

## Step 7 — (Optional) Build for the VIVE Focus Vision

This step requires a headset. If you don't have one, stop here — the
editor is enough for development and most testing.

Follow [`BUILD_GUIDE.md`](BUILD_GUIDE.md) from the section **Build
configuration verification** down. The settings table tells you
exactly what to put in each Player Settings field. Build target is
Android.

The first APK build takes 15–30 minutes. Subsequent builds take 2–3.

---

## Where to go next

- **Want to record your first session?** Build the APK, install it on
  a Focus Vision, and follow
  [`RESEARCHER_GUIDE.md`](../Eye_lean_Unity_Project/Eye_lean/docs/RESEARCHER_GUIDE.md).
- **Want to write a custom experiment?** Read
  [`SAMPLE_EXPERIMENT.md`](SAMPLE_EXPERIMENT.md) end-to-end and copy
  patterns. Or use the [Skeleton template](SKELETON.md) for a
  minimal scaffold.
- **Want to analyze a recording?** Open the notebooks under
  `eyelean_analysis/notebooks/examples/`. They auto-locate a sample
  CSV — you can run them on a fresh clone before recording anything.

## Common stumbles

**"The project asks for a different Unity version when I open it."**
You installed a different `6000.3.x` patch. The lockfile pins to
`6000.3.9f1` exactly. Install that version and reopen.

**"There are red errors in the Console immediately."**
Most often a package didn't finish importing. Close Unity, reopen
the project, and let it finish. If errors persist, delete the
`Library/` folder inside the project and reopen — Unity will rebuild
its cache.

**"Play button is greyed out."**
A Console error is blocking compilation. Read the error in the
Console — it tells you which file and line. If it mentions an
`asmdef` reference, the project likely needs a `Reimport All`
(right-click `Assets/`, **Reimport All**).

**"The eye tracker reports as `NullEyeTracker` in the editor."**
Expected. Without a connected headset, Eye_lean falls back to a
no-op tracker so the rest of the toolkit keeps running. To get real
gaze data, build the APK and run on the Focus Vision.

**"The headset doesn't appear in `adb devices`."**
Developer Mode is not enabled on the headset, or the USB cable is
data-only (some are charge-only). See
[`BUILD_GUIDE.md`](BUILD_GUIDE.md#deployment-to-vive-focus-vision).

If you hit something this list doesn't cover, check
[`TROUBLESHOOTING.md`](TROUBLESHOOTING.md) before opening an issue.
