using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace EyeLean.Experiment
{
    /// <summary>
    /// Manages visual search trials where participant must find a red target among blue distractors.
    /// </summary>
    public class VisualSearchManager : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Transform cameraTransform;
        [SerializeField] private EnvironmentGenerator environmentGenerator;

        [Header("Colors")]
        [SerializeField] private Color targetColor = Color.red;
        [SerializeField] private Color distractorColor = Color.blue;
        [SerializeField] private Color targetFoundColor = Color.green;

        private VisualSearchConfig config;
        private List<GameObject> searchObjects = new List<GameObject>();
        private GameObject targetObject;
        private GazeTarget targetGazeComponent;
        private Renderer targetRenderer;
        private bool targetFound;
        private float gazeTimeOnTarget;
        private float trialStartTime;
        private Vector3 targetOriginalScale;

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
        /// Configure the visual search parameters.
        /// </summary>
        public void Configure(VisualSearchConfig config)
        {
            this.config = config;
        }

        /// <summary>
        /// Run a single visual search trial.
        /// </summary>
        /// <param name="trialIndex">Zero-based trial index</param>
        /// <param name="onComplete">Callback with (found, searchTime, targetPosition)</param>
        /// <summary>
        /// Spawn-only entry point. Used by replay-driven mode to populate
        /// the scene with the same Recordables that existed at record time
        /// without running the timed trial logic.
        /// </summary>
        public void GenerateScene(int trialIndex)
        {
            EyeLean.SceneState.SceneEventRecorder.RecordKV("SpawnVisualSearchScene", "",
                ("trial", trialIndex.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            Cleanup();
            GenerateSearchScene(trialIndex);
        }

        public IEnumerator RunTrial(int trialIndex, Action<bool, float, Vector3> onComplete)
        {
            // Cleanup any previous trial
            Cleanup();

            EyeLean.SceneState.SceneEventRecorder.RecordKV("SpawnVisualSearchScene", "",
                ("trial", trialIndex.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            GenerateSearchScene(trialIndex);

            // Reset state
            targetFound = false;
            gazeTimeOnTarget = 0f;
            trialStartTime = Time.time;

            // Wait for target acquisition or timeout. On gaze loss, decay the
            // accumulated dwell time instead of resetting it to zero — eye-
            // tracking noise + microsaccades can flicker IsBeingGazedAt false
            // for a frame or two even when the participant is still fixating,
            // and a hard reset makes the dwell impossible to complete.
            // Decay rate is 2× the accumulation rate, so a 100 ms blink off
            // the target costs 200 ms of progress but recovery is possible.
            const float GazeLossDecayMultiplier = 2.0f;
            while (!targetFound && (Time.time - trialStartTime) < config.maxTrialDuration)
            {
                bool gazingAtTarget = targetGazeComponent != null && targetGazeComponent.IsBeingGazedAt;
                if (gazingAtTarget)
                {
                    gazeTimeOnTarget += Time.deltaTime;
                }
                else
                {
                    gazeTimeOnTarget -= Time.deltaTime * GazeLossDecayMultiplier;
                    if (gazeTimeOnTarget < 0f) gazeTimeOnTarget = 0f;
                }

                // Visual feedback always follows the current progress so the
                // ramp-up and ramp-down are continuous rather than snapping.
                if (targetRenderer != null && targetObject != null)
                {
                    float progress = Mathf.Clamp01(gazeTimeOnTarget / config.targetAcquisitionTime);
                    Color feedbackColor = Color.Lerp(targetColor, targetFoundColor, progress);
                    targetRenderer.material.color = feedbackColor;
                    float scaleMultiplier = 1f + (0.3f * progress);
                    targetObject.transform.localScale = targetOriginalScale * scaleMultiplier;
                }

                if (gazeTimeOnTarget >= config.targetAcquisitionTime)
                {
                    targetFound = true;
                    if (targetRenderer != null)
                    {
                        targetRenderer.material.color = targetFoundColor;
                    }
                }

                yield return null;
            }

            float searchTime = Time.time - trialStartTime;
            Vector3 targetPos = targetObject != null ? targetObject.transform.position : Vector3.zero;

            onComplete?.Invoke(targetFound, searchTime, targetPos);
        }

        /// <summary>
        /// Resolve the difficulty spec for the given trial. Uses the
        /// per-trial schedule when populated; otherwise falls back to
        /// uniform conjunction search at the global distractorCount so
        /// existing scenes without a schedule still work.
        /// </summary>
        public VisualSearchTrialSpec GetTrialSpec(int trialIndex)
        {
            if (config.trials != null && config.trials.Length > 0)
            {
                int idx = Mathf.Clamp(trialIndex, 0, config.trials.Length - 1);
                return config.trials[idx];
            }
            return new VisualSearchTrialSpec
            {
                condition = VisualSearchCondition.Conjunction,
                distractorCount = config.distractorCount,
            };
        }

        /// <summary>
        /// Effective number of trials for the configured schedule. Length
        /// of the per-trial array when set; otherwise the global trialCount.
        /// </summary>
        public int EffectiveTrialCount =>
            (config.trials != null && config.trials.Length > 0)
                ? config.trials.Length
                : config.trialCount;

        private void GenerateSearchScene(int trialIndex)
        {
            // Use trial index as seed for reproducibility
            UnityEngine.Random.InitState(trialIndex * 12345 + 42);

            float eyeHeight = cameraTransform != null ? cameraTransform.position.y : 1.6f;
            List<Vector3> usedPositions = new List<Vector3>();

            VisualSearchTrialSpec spec = GetTrialSpec(trialIndex);
            int distractorCount = Mathf.Max(0, spec.distractorCount);

            // Distractor composition by condition (Treisman & Gelade 1980):
            //   ColorPopOut   — all distractors share target shape (spheres)
            //                   in distractor color; target color is unique
            //                   → pre-attentive, O(1) detection.
            //   ShapePopOut   — all distractors share target color (red)
            //                   in cube shape; target shape is unique
            //                   → pre-attentive, O(1) detection.
            //   Conjunction   — half color-distractors (spheres in distractor
            //                   color), half shape-distractors (cubes in
            //                   target color). Target's color+shape pair is
            //                   unique → serial search with measurable slope.
            int colorDistractorCount;
            switch (spec.condition)
            {
                case VisualSearchCondition.ColorPopOut:
                    colorDistractorCount = distractorCount;
                    break;
                case VisualSearchCondition.ShapePopOut:
                    colorDistractorCount = 0;
                    break;
                case VisualSearchCondition.Conjunction:
                default:
                    colorDistractorCount = distractorCount / 2;
                    break;
            }

            for (int i = 0; i < distractorCount; i++)
            {
                Vector3 pos = GetRandomPosition(eyeHeight, usedPositions);
                usedPositions.Add(pos);

                bool isColorDistractor = i < colorDistractorCount;
                Color color = isColorDistractor ? distractorColor : targetColor;
                PrimitiveType shape = isColorDistractor ? PrimitiveType.Sphere : PrimitiveType.Cube;
                var distractor = CreateSearchObject(pos, color, $"Distractor_{i}", shape);
                searchObjects.Add(distractor);
            }

            // Target is always the red sphere — the only red+sphere object
            // regardless of condition.
            Vector3 targetPos = GetRandomPosition(eyeHeight, usedPositions);
            targetObject = CreateSearchObject(targetPos, targetColor, "SearchTarget", PrimitiveType.Sphere);
            targetGazeComponent = targetObject.GetComponent<GazeTarget>();
            targetRenderer = targetObject.GetComponent<Renderer>();
            targetOriginalScale = targetObject.transform.localScale;
            searchObjects.Add(targetObject);
        }

        private Vector3 GetRandomPosition(float eyeHeight, List<Vector3> usedPositions)
        {
            const int maxAttempts = 50;
            const float minSeparation = 0.5f;
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

            // Also respect config depth limits
            minZ = Mathf.Max(minZ, config.minDepth);
            maxZ = Mathf.Min(maxZ, config.maxDepth);

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

                // Check separation from existing positions
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

            // Fallback: return position in center of room
            float fallbackDepth = (minZ + maxZ) / 2f;
            return new Vector3(0, Mathf.Clamp(eyeHeight, minY, maxY), fallbackDepth);
        }

        private GameObject CreateSearchObject(Vector3 position, Color color, string name, PrimitiveType primitiveType = PrimitiveType.Sphere)
        {
            var obj = GameObject.CreatePrimitive(primitiveType);
            obj.name = name;
            // Opt into the scene-state sidecar via the "Recordable" tag.
            try { obj.tag = "Recordable"; } catch (UnityException) { /* tag not defined */ }
            // Parent under the room transform so the local position lands
            // inside the rotated/offset room.
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
            obj.transform.localScale = Vector3.one * config.objectSize;

            // REPLACE material for Android compatibility
            obj.GetComponent<Renderer>().material = VRMaterialProvider.GetMaterial(color);

            // Add gaze target for tracking
            obj.AddComponent<GazeTarget>();

            // Deterministic Recordable seed so a recording's id matches a
            // future replay's id for the same spawn slot.
            EyeLean.SceneState.SceneStateRecorder.MarkRecordableSeeded(obj, name);

            return obj;
        }

        /// <summary>
        /// Clean up all search objects.
        /// </summary>
        public void Cleanup()
        {
            foreach (var obj in searchObjects)
            {
                if (obj != null) Destroy(obj);
            }
            searchObjects.Clear();
            targetObject = null;
            targetGazeComponent = null;
        }

        private void OnDestroy()
        {
            Cleanup();
        }
    }
}
