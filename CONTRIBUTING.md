# Contributing to Eye_lean

Thanks for your interest in Eye_lean. This is a research toolkit maintained by Rutgers VCL (Visual Cognition Lab); we welcome bug reports, fixes, and well-scoped enhancements that fit the project's research focus.

## Reporting issues

Open an issue on GitHub with:
- A clear title describing the problem
- The Eye_lean version (Unity bundle version or Python `eyelean_analysis.__version__`)
- Hardware (HMD model, OpenXR runtime version) for Unity-side issues
- Steps to reproduce, expected vs. actual behavior
- Relevant logs (`adb logcat -s Unity` for the device, Python traceback for the package)

If you suspect bad data, attach a small CSV excerpt — but check it for any participant identifiers first.

## Pull requests

1. Fork the repo and create a topic branch (`fix/blink-threshold`, `feat/varjo-support`, etc.).
2. Keep changes focused — one logical change per PR. Mixing refactors with feature work makes review hard.
3. Match the existing code style:
   - C#: Unity conventions, `PascalCase` for public, `camelCase` for private fields.
   - Python: PEP 8, type hints on public functions, docstrings in the existing format.
4. Update relevant documentation (`docs/`, `eyelean_analysis/README.md`, or the Unity project's docs) when behavior or APIs change.
5. For algorithm changes (vergence math, fixation thresholds, LHIPA, K-coefficient), include a citation or rationale — these affect research validity.

## Documentation style

Eye_lean's docs are written for researchers with minimal Unity experience. Every doc follows the same rules:

- Use imperative verbs. Write "Open Unity Hub." — not "You should open Unity Hub." or "Let's open Unity Hub."
- No first-person plural. Avoid "we", "our", "let's".
- Banned adjectives: *comprehensive, powerful, robust, seamless, elegant, intuitive, cutting-edge, world-class, one-stop, rich, effortless, plug-and-play* (in marketing sense).
- No emojis in any Markdown file, including the changelog.
- Open every doc with three blocks: **What it is** (one paragraph), **Audience** (one line), **Prerequisites** (Unity version, OpenXR runtime, Python version, hardware, as applicable).
- Every how-to is a numbered procedure (`1.`, `2.`, ...) before any prose explanation.
- Every procedure ends with a **Verify** step that names the exact log line, file, or Inspector field that confirms success.
- Cite source files as `Assets/Scripts/.../File.cs:42`. Never "see the source".
- Use only post-RC API class names: `EyeTracker`, `HMDDataCollector`, `SessionRecorder`. `SimpleEyeTracker` is gone.
- One audience per doc. If a doc would span both newcomer and expert, split it.
- Link, do not duplicate. The hub at `docs/README.md` is the single index.
- Canonical values (Unity version, repo URL, app version) live in one source: `CITATION.cff` for version; `docs/BUILD_GUIDE.md` for the Unity / OpenXR pin.

## Local setup

### Unity side
- Unity 6000.3.9f1, VIVE OpenXR 2.5.1.
- Open `Eye_lean_Unity_Project/Eye_lean/` in Unity Hub.
- See `Eye_lean_Unity_Project/Eye_lean/docs/SETUP.md` for the full environment guide.

### Python side
```bash
pip install -e "./eyelean_analysis[all]"
```
A test suite is in development; please add tests alongside any new analysis function.

## Scope

In scope: VR eye tracking data collection (VIVE Focus Vision today, Varjo & HoloLens 2 planned), CSV-based analysis pipelines, signal processing, fixation/saccade/attention metrics, replay and visualization.

Out of scope: large new experiment paradigms unrelated to the core toolkit, dependencies that pull in heavy non-research stacks, breaking changes to the CSV schema without a migration path.

## Questions

Open a GitHub Discussion if you're unsure whether a change is in scope before investing significant time.
