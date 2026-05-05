using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace EyeLean.Experiment
{
    /// <summary>
    /// Manages change detection trials where participant must find what changed after a brief blank.
    /// </summary>
    public class ChangeDetectionManager : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Transform cameraTransform;
        [SerializeField] private EnvironmentGenerator environmentGenerator;

        [Header("Object Colors")]
        // Excludes red and green: those are reserved for the incorrect /
        // correct feedback tints, and any trial object sharing them would
        // leave the participant unable to tell feedback from original color.
        // The remaining hues are also chosen to be well-separated from each
        // other so GetDifferentColor's pick is always perceptually distinct.
        [SerializeField] private Color[] objectColors = new Color[]
        {
            Color.blue,
            Color.yellow,
            Color.cyan,
            Color.magenta,
            new Color(1.00f, 0.50f, 0.00f), // orange
            new Color(0.50f, 0.00f, 0.50f), // purple
            new Color(0.85f, 0.85f, 0.85f), // light gray
            new Color(0.40f, 0.20f, 0.05f)  // brown
        };

        [Header("Object Shapes")]
        [SerializeField] private PrimitiveType[] objectShapes = new PrimitiveType[]
        {
            PrimitiveType.Cube,
            PrimitiveType.Sphere,
            PrimitiveType.Cylinder
        };

        private ChangeDetectionConfig config;
        private List<GameObject> sceneObjects = new List<GameObject>();
        private List<ObjectState> originalStates = new List<ObjectState>();

        private int changedObjectIndex;
        private string changeType;
        private GazeTarget changedObjectGaze;
        private float gazeTimeOnChanged;
        private GameObject feedbackObject;
        private Color feedbackOriginalColor;
        // Second feedback target: when the user picks wrong, we tint their
        // pick red AND reveal the actual changed object in green so the
        // participant learns the correct answer, not just that they were
        // wrong.
        private GameObject revealObject;
        private Color revealOriginalColor;
        // The feedback tint is also pulsed (cycles brightness) so a static
        // tint isn't lost against the scene background in VR.
        private Coroutine pulseCoroutine;
        private const float FeedbackPulseHz = 3.5f;
        private const float FeedbackPulseAmplitude = 0.40f;

        private struct ObjectState
        {
            public Vector3 position;
            public Color color;
            public PrimitiveType shape;
            public string name;
        }

        private void Awake()
        {
            if (cameraTransform == null)
            {
                var cam = Camera.main;
                if (cam != null) cameraTransform = cam.transform;
            }

            if (environmentGenerator == null)
            {
                environmentGenerator = FindFirstObjectByType<EnvironmentGenerator>();
            }
        }

        /// <summary>
        /// Configure the change detection parameters.
        /// </summary>
        public void Configure(ChangeDetectionConfig config)
        {
            this.config = config;
        }

        /// <summary>
        /// Generate the initial scene for a trial.
        /// </summary>
        public void GenerateScene(int trialIndex)
        {
            EyeLean.SceneState.SceneEventRecorder.RecordKV("SpawnChangeDetectionScene", "",
                ("trial", trialIndex.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            Cleanup();
            // Per-trial deterministic seed: keeps each trial's RNG isolated
            // from the others even outside replay.
            UnityEngine.Random.InitState(trialIndex * 54321 + 99);

            float eyeHeight = cameraTransform != null ? cameraTransform.position.y : 1.6f;
            List<Vector3> usedPositions = new List<Vector3>();

            // Generate objects
            for (int i = 0; i < config.objectCount; i++)
            {
                Vector3 pos = GetRandomPosition(eyeHeight, usedPositions);
                usedPositions.Add(pos);

                Color color = objectColors[UnityEngine.Random.Range(0, objectColors.Length)];
                PrimitiveType shape = objectShapes[UnityEngine.Random.Range(0, objectShapes.Length)];

                var obj = CreateSceneObject(pos, color, shape, $"ChangeObject_t{trialIndex}_{i}");
                sceneObjects.Add(obj);

                // Store original state
                originalStates.Add(new ObjectState
                {
                    position = pos,
                    color = color,
                    shape = shape,
                    name = obj.name
                });
            }

            // Decide which object will change and how
            changedObjectIndex = UnityEngine.Random.Range(0, sceneObjects.Count);
            changeType = UnityEngine.Random.value > 0.5f ? "color" : "position";

            Debug.Log($"[ChangeDetection] Trial {trialIndex}: Object {changedObjectIndex} will change ({changeType})");
        }

        /// <summary>
        /// Hide all scene objects (blank screen phase).
        /// </summary>
        public void HideScene()
        {
            EyeLean.SceneState.SceneEventRecorder.Record("ChangeDetectionHideScene");
            foreach (var obj in sceneObjects)
            {
                if (obj != null) obj.SetActive(false);
            }
        }

        /// <summary>
        /// Show scene with the change applied.
        /// </summary>
        public void ShowChangedScene()
        {
            EyeLean.SceneState.SceneEventRecorder.Record("ChangeDetectionShowChangedScene");
            // First, show all objects
            foreach (var obj in sceneObjects)
            {
                if (obj != null) obj.SetActive(true);
            }

            // Apply the change
            if (changedObjectIndex >= 0 && changedObjectIndex < sceneObjects.Count)
            {
                var changedObject = sceneObjects[changedObjectIndex];
                var originalState = originalStates[changedObjectIndex];

                if (changeType == "color")
                {
                    // Log before/after RGB so post-hoc audits can confirm the
                    // color change actually took.
                    Color newColor = GetDifferentColor(originalState.color);
                    var renderer = changedObject.GetComponent<Renderer>();
                    if (renderer != null)
                    {
                        renderer.material.color = newColor;
                        Debug.Log($"[ChangeDetection] Color change applied to '{originalState.name}': " +
                                  $"orig=({originalState.color.r:F2},{originalState.color.g:F2},{originalState.color.b:F2}) " +
                                  $"new=({newColor.r:F2},{newColor.g:F2},{newColor.b:F2})");
                    }
                    else
                    {
                        Debug.LogError($"[ChangeDetection] Color change FAILED — no Renderer on '{originalState.name}'.");
                    }
                }
                else // position
                {
                    // originalState.position is room-local and the object is
                    // parented under the room transform — apply the shift in
                    // localPosition so the shifted target stays inside the room.
                    Vector3 shift = new Vector3(
                        UnityEngine.Random.Range(-1f, 1f),
                        UnityEngine.Random.Range(-0.3f, 0.3f),
                        UnityEngine.Random.Range(-1f, 1f)
                    ).normalized * config.positionShiftAmount;

                    changedObject.transform.localPosition = originalState.position + shift;
                }

                // Get gaze component for detection
                changedObjectGaze = changedObject.GetComponent<GazeTarget>();
            }
        }

        // Greedy farthest-color selection: pick the perceptually-most-distant
        // palette entry so a color change always crosses the largest visual
        // gap available. Picking randomly from non-similar entries can land
        // on an adjacent hue that reads as "no change" on a VR display.
        private Color GetDifferentColor(Color originalColor)
        {
            float bestDistSq = -1f;
            Color best = Color.white;
            foreach (var c in objectColors)
            {
                if (ColorsSimilar(c, originalColor)) continue;
                float dr = c.r - originalColor.r;
                float dg = c.g - originalColor.g;
                float db = c.b - originalColor.b;
                float distSq = dr * dr + dg * dg + db * db;
                if (distSq > bestDistSq)
                {
                    bestDistSq = distSq;
                    best = c;
                }
            }
            return bestDistSq < 0f ? Color.white : best;
        }

        private bool ColorsSimilar(Color a, Color b)
        {
            return Mathf.Abs(a.r - b.r) < 0.15f &&
                   Mathf.Abs(a.g - b.g) < 0.15f &&
                   Mathf.Abs(a.b - b.b) < 0.15f;
        }

        /// <summary>
        /// Run the test phase where participant searches for the change.
        /// Tracks gaze on all objects - correct detection = green feedback, wrong = red feedback + trial ends.
        /// </summary>
        public IEnumerator RunTestPhase(int trialIndex, Action<bool, float, string, string> onComplete)
        {
            ShowChangedScene();

            float startTime = Time.time;
            bool detected = false;
            bool wrongSelection = false;
            GameObject selectedObject = null;

            // Build arrays for gaze tracking (more cache-friendly than dictionaries)
            int objectCount = sceneObjects.Count;
            GazeTarget[] gazeTargets = new GazeTarget[objectCount];
            float[] gazeTimers = new float[objectCount];

            for (int i = 0; i < objectCount; i++)
            {
                gazeTargets[i] = sceneObjects[i].GetComponent<GazeTarget>();
                gazeTimers[i] = 0f;
            }

            while (!detected && !wrongSelection && (Time.time - startTime) < config.maxSearchDuration)
            {
                // Check gaze on all objects
                for (int i = 0; i < objectCount; i++)
                {
                    var gazeTarget = gazeTargets[i];
                    if (gazeTarget == null) continue;

                    if (gazeTarget.IsBeingGazedAt)
                    {
                        gazeTimers[i] += Time.deltaTime;

                        // Check if dwell time threshold reached
                        if (gazeTimers[i] >= config.detectionAcquisitionTime)
                        {
                            selectedObject = sceneObjects[i];

                            if (i == changedObjectIndex)
                            {
                                // Correct detection!
                                detected = true;
                            }
                            else
                            {
                                // Wrong object selected - trial ends
                                wrongSelection = true;
                            }
                            break;
                        }
                    }
                    else
                    {
                        // Reset timer if gaze leaves object
                        gazeTimers[i] = 0f;
                    }
                }

                yield return null;
            }

            float detectionTime = Time.time - startTime;

            // When the user picks wrong OR times out, also reveal the actual
            // changed object (green) so they learn the right answer.
            if (config.showFeedback)
            {
                GameObject correctObject = (changedObjectIndex >= 0 && changedObjectIndex < sceneObjects.Count)
                    ? sceneObjects[changedObjectIndex]
                    : null;
                ShowDetectionFeedback(detected, selectedObject, correctObject);
                yield return new WaitForSeconds(config.feedbackDuration);
                HideFeedback();
            }

            string objectName = changedObjectIndex >= 0 && changedObjectIndex < originalStates.Count
                ? originalStates[changedObjectIndex].name
                : "unknown";

            // Return result: detected is true only if correct object was found
            onComplete?.Invoke(detected, detectionTime, changeType, objectName);
        }

        private Vector3 GetRandomPosition(float eyeHeight, List<Vector3> usedPositions)
        {
            const int maxAttempts = 50;
            const float minSeparation = 0.6f;
            const float wallMargin = 0.5f;  // Keep objects 0.5m from walls
            const float objectRadius = 0.15f; // Account for object size

            // Get room bounds with safety margins
            float roomWidth = environmentGenerator != null ? environmentGenerator.RoomWidth : 6f;
            float roomLength = environmentGenerator != null ? environmentGenerator.RoomLength : 6f;
            float floorHeight = 0f;
            float ceilingHeight = environmentGenerator != null ? environmentGenerator.RoomHeight : 3f;

            // Calculate safe bounds (inset from walls by margin + object radius)
            float safeMargin = wallMargin + objectRadius;
            float minX = -roomWidth / 2f + safeMargin;
            float maxX = roomWidth / 2f - safeMargin;
            float minY = floorHeight + safeMargin;
            float maxY = ceilingHeight - safeMargin;
            float minZ = 1.5f;  // Keep objects at least 1.5m from camera start position
            float maxZ = roomLength - safeMargin;

            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                // Generate random position within safe bounds
                float x = UnityEngine.Random.Range(minX, maxX);
                float z = UnityEngine.Random.Range(minZ, maxZ);
                float y = UnityEngine.Random.Range(minY, maxY);

                // CLAMP to ensure absolutely within bounds
                x = Mathf.Clamp(x, minX, maxX);
                y = Mathf.Clamp(y, minY, maxY);
                z = Mathf.Clamp(z, minZ, maxZ);

                Vector3 pos = new Vector3(x, y, z);

                bool tooClose = false;
                foreach (var usedPos in usedPositions)
                {
                    if (Vector3.Distance(pos, usedPos) < minSeparation)
                    {
                        tooClose = true;
                        break;
                    }
                }

                if (!tooClose)
                {
                    return pos;
                }
            }

            // Fallback: center of room
            float fallbackDepth = (minZ + maxZ) / 2f;
            return new Vector3(0, Mathf.Clamp(eyeHeight, minY, maxY), fallbackDepth);
        }

        /// <summary>
        /// Show visual feedback. The user's selection (if any) is colored
        /// green (correct) or red (wrong). On wrong / timed-out trials the
        /// actual changed object is also revealed in green so the participant
        /// learns the right answer.
        /// </summary>
        private void ShowDetectionFeedback(bool isCorrect, GameObject selectedObject, GameObject correctObject)
        {
            if (!config.showFeedback) return;
            // Record the feedback moment so a replay scene can re-tint the
            // same selected/correct objects via their Recordable ids.
            string selId = ResolveRecordableId(selectedObject);
            string corId = ResolveRecordableId(correctObject);
            EyeLean.SceneState.SceneEventRecorder.RecordKV("ChangeDetectionFeedback", "",
                ("isCorrect", isCorrect ? "1" : "0"),
                ("selectedId", selId),
                ("correctId", corId));
            HideFeedback();

            // Tint the user's pick (if they made one).
            Renderer pickRenderer = null;
            Color pickFeedback = Color.white;
            if (selectedObject != null)
            {
                feedbackObject = selectedObject;
                pickRenderer = selectedObject.GetComponent<Renderer>();
                if (pickRenderer != null)
                {
                    feedbackOriginalColor = pickRenderer.material.color;
                    pickFeedback = isCorrect ? config.correctFeedbackColor : config.incorrectFeedbackColor;
                    pickRenderer.material.color = pickFeedback;
                }
            }

            // When the pick is wrong (or there was no pick), also reveal the
            // actual changed object in green.
            Renderer revealRenderer = null;
            if (!isCorrect && correctObject != null && correctObject != selectedObject)
            {
                revealObject = correctObject;
                revealRenderer = correctObject.GetComponent<Renderer>();
                if (revealRenderer != null)
                {
                    revealOriginalColor = revealRenderer.material.color;
                    revealRenderer.material.color = config.correctFeedbackColor;
                }
            }

            // Pulse the tinted renderers' brightness so the feedback signal
            // stands out against the scene. HideFeedback stops the coroutine
            // and restores originals.
            if (pulseCoroutine != null) StopCoroutine(pulseCoroutine);
            pulseCoroutine = StartCoroutine(PulseFeedback(pickRenderer, pickFeedback, revealRenderer, config.correctFeedbackColor));
        }

        private IEnumerator PulseFeedback(Renderer pick, Color pickBase, Renderer reveal, Color revealBase)
        {
            float started = Time.time;
            while (true)
            {
                float t = Time.time - started;
                // Sine wave in [-1, 1]; map to [1 - amp, 1 + amp] brightness multiplier.
                float wave = Mathf.Sin(t * Mathf.PI * 2f * FeedbackPulseHz);
                float mult = 1f + wave * FeedbackPulseAmplitude;
                if (pick != null)
                {
                    pick.material.color = ScaleRgbClamped(pickBase, mult);
                }
                if (reveal != null)
                {
                    reveal.material.color = ScaleRgbClamped(revealBase, mult);
                }
                yield return null;
            }
        }

        private static Color ScaleRgbClamped(Color c, float mult)
        {
            return new Color(
                Mathf.Clamp01(c.r * mult),
                Mathf.Clamp01(c.g * mult),
                Mathf.Clamp01(c.b * mult),
                c.a);
        }

        /// <summary>
        /// Restore both feedback objects' original colors. Stops the pulse
        /// coroutine so the next trial's renderers don't keep oscillating.
        /// </summary>
        private void HideFeedback()
        {
            if (pulseCoroutine != null)
            {
                StopCoroutine(pulseCoroutine);
                pulseCoroutine = null;
            }
            if (feedbackObject != null)
            {
                var renderer = feedbackObject.GetComponent<Renderer>();
                if (renderer != null)
                {
                    renderer.material.color = feedbackOriginalColor;
                }
                feedbackObject = null;
            }
            if (revealObject != null)
            {
                var renderer = revealObject.GetComponent<Renderer>();
                if (renderer != null)
                {
                    renderer.material.color = revealOriginalColor;
                }
                revealObject = null;
            }
        }

        private GameObject CreateSceneObject(Vector3 position, Color color, PrimitiveType shape, string name)
        {
            var obj = GameObject.CreatePrimitive(shape);
            obj.name = name;
            // Opt into the scene-state sidecar via the "Recordable" tag.
            try { obj.tag = "Recordable"; } catch (UnityException) { /* tag not defined */ }
            // GetRandomPosition produces room-local coordinates (X centered at
            // 0, Z = 0..roomLength). Parent under the room and use
            // localPosition so the child inherits the room's rotation+offset
            // and stays inside it.
            var roomT = environmentGenerator != null ? environmentGenerator.RoomTransform : null;
            if (roomT != null)
            {
                obj.transform.SetParent(roomT, worldPositionStays: false);
                obj.transform.localPosition = position;
            }
            else
            {
                obj.transform.position = position;
            }
            obj.transform.localScale = Vector3.one * 0.2f;
            // Axis-aligned local rotation keeps every cube/cylinder face
            // parallel to the room's walls regardless of the user's spawn yaw.
            obj.transform.localRotation = Quaternion.identity;

            // REPLACE material for Android compatibility
            obj.GetComponent<Renderer>().material = VRMaterialProvider.GetMaterial(color);

            // Replace the mesh-fit collider with an inflated SphereCollider so
            // far objects (3-5m back) are easier to acquire with gaze. Gaze
            // hitbox ~0.6m world-space (~3x the visual mesh's angular
            // subtense); the visible mesh is unchanged.
            var defaultCol = obj.GetComponent<Collider>();
            if (defaultCol != null) DestroyImmediate(defaultCol);
            var sc = obj.AddComponent<SphereCollider>();
            sc.radius = 1.5f;       // local; world radius = 1.5 * 0.2 = 0.3m
            sc.isTrigger = false;   // EyeTracker raycasts through triggers; non-trigger is required for hits

            obj.AddComponent<GazeTarget>();

            // Deterministic Recordable seed so a recording's id matches a
            // future replay's id for the same spawn slot.
            EyeLean.SceneState.SceneStateRecorder.MarkRecordableSeeded(obj, name);

            return obj;
        }

        /// <summary>
        /// Clean up all scene objects and feedback indicator.
        /// </summary>
        public void Cleanup()
        {
            HideFeedback();

            foreach (var obj in sceneObjects)
            {
                if (obj != null) Destroy(obj);
            }
            sceneObjects.Clear();
            originalStates.Clear();
            changedObjectIndex = -1;
            changedObjectGaze = null;
        }

        private void OnDestroy()
        {
            Cleanup();
        }

        // Used by the feedback event row so a replay handler can find the
        // same objects by their seeded Recordable id.
        private static string ResolveRecordableId(GameObject go)
        {
            if (go == null) return string.Empty;
            var rec = go.GetComponent<EyeLean.SceneState.Recordable>();
            return rec != null ? rec.UniqueId : string.Empty;
        }
    }
}
