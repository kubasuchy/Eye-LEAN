using System.Collections.Generic;
using UnityEngine;

namespace EyeLean.Experiment
{
    /// <summary>
    /// Manages the counting task where participant must count objects of a specific color.
    /// </summary>
    public class CountingTaskManager : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Transform cameraTransform;
        [SerializeField] private EnvironmentGenerator environmentGenerator;

        [Header("Colors")]
        [SerializeField] private Color targetColor = Color.red;
        [SerializeField] private Color distractor1Color = Color.blue;
        [SerializeField] private Color distractor2Color = Color.green;

        private CountingTaskConfig config;
        private List<GameObject> countingObjects = new List<GameObject>();
        private int targetCount;

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
        /// Configure the counting task parameters.
        /// </summary>
        public void Configure(CountingTaskConfig config)
        {
            this.config = config;
        }

        /// <summary>
        /// Generate the counting scene with randomized objects.
        /// </summary>
        /// <returns>The actual count of target-colored objects.</returns>
        public int GenerateScene()
        {
            // No per-spawn seed picking: Random.state is restored once at
            // session start from a recorded snapshot, and the live coroutines
            // re-run with deterministic Time + RNG, so the Random.Range calls
            // below reproduce the original recording's outputs.
            EyeLean.SceneState.SceneEventRecorder.Record("SpawnCountingScene");

            Cleanup();

            float eyeHeight = cameraTransform != null ? cameraTransform.position.y : 1.6f;
            List<Vector3> usedPositions = new List<Vector3>();

            // Generate target color objects (RED cubes)
            targetCount = UnityEngine.Random.Range(config.minTargetCount, config.maxTargetCount + 1);
            for (int i = 0; i < targetCount; i++)
            {
                Vector3 pos = GetRandomPosition(eyeHeight, usedPositions);
                usedPositions.Add(pos);

                var obj = CreateCountingObject(pos, targetColor, $"TargetCube_{i}", PrimitiveType.Cube);
                countingObjects.Add(obj);
            }

            // Generate distractor color 1 (BLUE cubes)
            int distractor1Count = UnityEngine.Random.Range(config.minDistractorCount, config.maxDistractorCount + 1);
            for (int i = 0; i < distractor1Count; i++)
            {
                Vector3 pos = GetRandomPosition(eyeHeight, usedPositions);
                usedPositions.Add(pos);

                var obj = CreateCountingObject(pos, distractor1Color, $"BlueCube_{i}", PrimitiveType.Cube);
                countingObjects.Add(obj);
            }

            // Generate distractor color 2 (GREEN cubes)
            int distractor2Count = UnityEngine.Random.Range(config.minDistractorCount, config.maxDistractorCount + 1);
            for (int i = 0; i < distractor2Count; i++)
            {
                Vector3 pos = GetRandomPosition(eyeHeight, usedPositions);
                usedPositions.Add(pos);

                var obj = CreateCountingObject(pos, distractor2Color, $"GreenCube_{i}", PrimitiveType.Cube);
                countingObjects.Add(obj);
            }

            Debug.Log($"[CountingTask] Generated scene with {targetCount} red, {distractor1Count} blue, {distractor2Count} green cubes");

            return targetCount;
        }

        private Vector3 GetRandomPosition(float eyeHeight, List<Vector3> usedPositions)
        {
            const int maxAttempts = 50;
            const float minSeparation = 0.4f;
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

            // Fallback: center of room
            float fallbackDepth = (minZ + maxZ) / 2f;
            return new Vector3(0, Mathf.Clamp(eyeHeight, minY, maxY), fallbackDepth);
        }

        private GameObject CreateCountingObject(Vector3 position, Color color, string name, PrimitiveType primitiveType)
        {
            var obj = GameObject.CreatePrimitive(primitiveType);
            obj.name = name;
            // Opt into the scene-state sidecar via the "Recordable" tag.
            try { obj.tag = "Recordable"; } catch (UnityException) { /* tag not defined */ }
            // Parent under the room transform so the room-local position
            // lands inside the rotated/offset room.
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

            // Axis-aligned local rotation keeps every cube parallel to the
            // walls regardless of the user's spawn yaw.
            obj.transform.localRotation = Quaternion.identity;

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
        /// Get the correct count of target objects.
        /// </summary>
        public int GetTargetCount()
        {
            return targetCount;
        }

        /// <summary>
        /// Clean up all counting objects.
        /// </summary>
        public void Cleanup()
        {
            foreach (var obj in countingObjects)
            {
                if (obj != null) Destroy(obj);
            }
            countingObjects.Clear();
            targetCount = 0;
        }

        private void OnDestroy()
        {
            Cleanup();
        }
    }
}
