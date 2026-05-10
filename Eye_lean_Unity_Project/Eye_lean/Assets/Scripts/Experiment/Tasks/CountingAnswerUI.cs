using System;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

namespace EyeLean.Experiment
{
    /// <summary>
    /// Gaze-based number selection UI for counting task answer input.
    /// Creates a row of number options that can be selected by looking at them.
    /// </summary>
    public class CountingAnswerUI : MonoBehaviour
    {
        [Header("Settings")]
        [SerializeField] private float selectionDwellTime = 1.5f;
        [SerializeField] private float optionSpacing = 0.50f;
        [SerializeField] private float optionSize = 0.22f;      // sized so the number label is readable in VR

        private List<GameObject> optionObjects = new List<GameObject>();
        private int selectedAnswer = -1;
        private bool answerSelected = false;
        private Action<int, float> onAnswerSelected;
        private float startTime;
        private Transform cachedRoomFrame;

        /// <summary>
        /// Horizontal layout axis for the row of answer spheres. Resolved
        /// from the active <see cref="EyeTracking.Core.IRoomFrameProvider"/>
        /// (cached after first lookup); falls back to world right when no
        /// provider is in scene. Independent of head heading by design — the
        /// row should sit parallel to the back wall, identical across trials.
        /// </summary>
        private Vector3 ResolveHorizontalRight()
        {
            if (cachedRoomFrame == null || !cachedRoomFrame)
            {
                var providers = FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
                for (int i = 0; i < providers.Length; i++)
                {
                    if (providers[i] is EyeTracking.Core.IRoomFrameProvider rfp && rfp.RoomTransform != null)
                    {
                        cachedRoomFrame = rfp.RoomTransform;
                        break;
                    }
                }
            }
            if (cachedRoomFrame != null && cachedRoomFrame)
            {
                Vector3 r = cachedRoomFrame.right;
                r.y = 0f;
                if (r.sqrMagnitude > 1e-4f) return r.normalized;
            }
            return Vector3.right;
        }

        /// <summary>
        /// Show answer options centered around expected count.
        /// </summary>
        /// <param name="centerValue">The actual count (options will be centered around this)</param>
        /// <param name="range">How many values above/below to show (e.g., 4 means actual +/- 4)</param>
        /// <param name="centerPosition">World position to center the options</param>
        /// <param name="callback">Called when an answer is selected with (answer, responseTime)</param>
        public void ShowOptions(int centerValue, int range, Vector3 centerPosition, float dwellTime, Action<int, float> callback)
        {
            // Record the show event so a replayer can recreate the same
            // answer-spheres layout. Payload fields (center, range, position
            // xyz, dwell) are parsed by the matching replay-side handler.
            EyeLean.SceneState.SceneEventRecorder.RecordKV("ShowCountingAnswerUI", "",
                ("center", centerValue.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                ("range", range.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                ("px", centerPosition.x.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)),
                ("py", centerPosition.y.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)),
                ("pz", centerPosition.z.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)),
                ("dwell", dwellTime.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)));

            Cleanup();
            selectionDwellTime = dwellTime;
            onAnswerSelected = callback;
            selectedAnswer = -1;
            answerSelected = false;
            startTime = Time.time;

            int minValue = Mathf.Max(0, centerValue - range);
            int maxValue = centerValue + range;
            int optionCount = maxValue - minValue + 1;

            float totalWidth = (optionCount - 1) * optionSpacing;
            float startX = -totalWidth / 2f;

            // Lay options along the room's right axis. The room frame is
            // world-axis-aligned (EnvironmentGenerator forces yaw=0 on the
            // live path), so the row is parallel to the back wall and
            // consistent across every trial in a session. Falls back to
            // world right when no IRoomFrameProvider is in scene.
            Vector3 right = ResolveHorizontalRight();

            for (int i = 0; i < optionCount; i++)
            {
                int value = minValue + i;
                Vector3 pos = centerPosition + right * (startX + i * optionSpacing);
                CreateOptionSphere(value, pos);
            }
        }

        private void CreateOptionSphere(int value, Vector3 position)
        {
            // Create sphere
            GameObject option = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            option.name = $"CountOption_{value}";
            option.transform.position = position;
            option.transform.localScale = Vector3.one * optionSize;

            // REPLACE material for Android compatibility
            option.GetComponent<Renderer>().material = VRMaterialProvider.GetMaterial(Color.white);

            // Add gaze target
            var gazeTarget = option.AddComponent<GazeTarget>();

            // Add number label above sphere
            CreateNumberLabel(option.transform, value);

            // Add tracking component
            var countingOption = option.AddComponent<CountingOption>();
            countingOption.Initialize(value, selectionDwellTime, this);

            optionObjects.Add(option);
        }

        private void CreateNumberLabel(Transform parent, int number)
        {
            // Label is centered on the sphere (not above it) so a downward
            // camera angle doesn't hide it behind the sphere, and is sized
            // large enough to read at 2.5m on the VIVE Focus Vision.
            GameObject labelObj = new GameObject("NumberLabel");
            labelObj.transform.SetParent(parent);
            labelObj.transform.localPosition = Vector3.zero;

            // BillboardLabel orients the label to face the camera each frame.
            labelObj.AddComponent<BillboardLabel>();

            var canvas = labelObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 100;

            var rect = labelObj.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(220, 220);
            // World width 0.31m (slightly larger than the 0.22m sphere — labels
            // overhang ~4cm, but adjacent options at 0.50m spacing still don't
            // overlap). Dark-navy fill with a bright cream outline so the digit
            // reads clearly against the white sphere at any practical viewing
            // distance.
            rect.localScale = Vector3.one * 0.0014f;

            var textObj = new GameObject("Text");
            textObj.transform.SetParent(labelObj.transform);
            var text = textObj.AddComponent<TextMeshProUGUI>();
            text.text = number.ToString();
            text.fontSize = 220;                       // ~0.31m world height — fills the canvas
            text.alignment = TextAlignmentOptions.Center;
            text.color = new Color(0.08f, 0.10f, 0.20f, 1f); // deep navy
            text.fontStyle = FontStyles.Bold;
            text.outlineWidth = 0.30f;
            text.outlineColor = new Color(1f, 0.96f, 0.85f, 1f); // warm cream
            text.raycastTarget = false;

            var textRect = text.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
            textRect.localPosition = Vector3.zero;
            textRect.localScale = Vector3.one;
        }

        /// <summary>
        /// Called by CountingOption when an answer is selected.
        /// </summary>
        public void OnOptionSelected(int value)
        {
            if (answerSelected) return;

            answerSelected = true;
            selectedAnswer = value;
            float responseTime = Time.time - startTime;

            // Record the participant's answer + response time. Replay handlers
            // re-highlight the selected option from this row.
            EyeLean.SceneState.SceneEventRecorder.RecordKV("CountingAnswerSelected", "",
                ("value", value.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                ("responseTime", responseTime.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)));

            // Visual feedback - highlight selected, gray out others
            foreach (var opt in optionObjects)
            {
                var optComp = opt.GetComponent<CountingOption>();
                var renderer = opt.GetComponent<Renderer>();
                if (optComp != null && renderer != null)
                {
                    if (optComp.Value == value)
                    {
                        renderer.material.color = Color.green;
                    }
                    else
                    {
                        renderer.material.color = new Color(0.5f, 0.5f, 0.5f, 0.5f);
                    }
                }
            }

            Debug.Log($"[CountingAnswerUI] Answer selected: {value} (response time: {responseTime:F2}s)");
            onAnswerSelected?.Invoke(value, responseTime);
        }

        /// <summary>
        /// Clean up all option objects.
        /// </summary>
        public void Cleanup()
        {
            // Only emit the hide event when options were actually visible:
            // Cleanup is also called pre-Show as a defensive reset, and a
            // phantom Hide row in that case would corrupt replay.
            if (optionObjects.Count > 0)
            {
                EyeLean.SceneState.SceneEventRecorder.Record("HideCountingAnswerUI");
            }
            foreach (var obj in optionObjects)
            {
                if (obj != null) Destroy(obj);
            }
            optionObjects.Clear();
            answerSelected = false;
            selectedAnswer = -1;
        }

        public bool IsAnswerSelected => answerSelected;
        public int SelectedAnswer => selectedAnswer;

        private void OnDestroy()
        {
            Cleanup();
        }
    }

    /// <summary>
    /// Helper component for individual counting option spheres.
    /// Tracks gaze dwell time and triggers selection.
    /// </summary>
    public class CountingOption : MonoBehaviour
    {
        public int Value { get; private set; }
        private CountingAnswerUI parentUI;
        private float dwellTime = 1.5f;
        private float gazeTime = 0f;
        private Renderer sphereRenderer;
        private GazeTarget gazeTarget;  // Cached for performance
        private Color originalColor = Color.white;

        public void Initialize(int value, float requiredDwellTime, CountingAnswerUI ui)
        {
            Value = value;
            dwellTime = requiredDwellTime;
            parentUI = ui;
            sphereRenderer = GetComponent<Renderer>();
            gazeTarget = GetComponent<GazeTarget>();  // Cache at initialization
            if (sphereRenderer != null)
            {
                originalColor = sphereRenderer.material.color;
            }
        }

        void Update()
        {
            if (parentUI == null || parentUI.IsAnswerSelected) return;

            if (gazeTarget != null && gazeTarget.IsBeingGazedAt)
            {
                gazeTime += Time.deltaTime;

                // Visual progress feedback - lerp from white to yellow
                if (sphereRenderer != null)
                {
                    float progress = Mathf.Clamp01(gazeTime / dwellTime);
                    sphereRenderer.material.color = Color.Lerp(originalColor, Color.yellow, progress);
                }

                if (gazeTime >= dwellTime)
                {
                    parentUI?.OnOptionSelected(Value);
                }
            }
            else
            {
                // Reset if gaze leaves
                gazeTime = 0f;
                if (sphereRenderer != null && !parentUI.IsAnswerSelected)
                {
                    sphereRenderer.material.color = originalColor;
                }
            }
        }
    }

    /// <summary>
    /// Billboard behaviour: faces the camera and translates the label out
    /// toward the camera by <see cref="forwardOffset"/> so the canvas sits in
    /// front of the parent sphere instead of inside it (otherwise the near
    /// hemisphere occludes a centered canvas).
    /// </summary>
    public class BillboardLabel : MonoBehaviour
    {
        [Tooltip("Distance (meters) the label is pushed toward the camera from its parent's center each LateUpdate. ~0.15m is enough to clear a 0.22m-diameter sphere.")]
        public float forwardOffset = 0.15f;

        private Camera mainCamera;

        void Start()
        {
            mainCamera = Camera.main;
        }

        void LateUpdate()
        {
            if (mainCamera == null) mainCamera = Camera.main;
            if (mainCamera == null || transform.parent == null) return;

            Vector3 parentPos = transform.parent.position;
            Vector3 fromParentToCamera = mainCamera.transform.position - parentPos;
            float dist = fromParentToCamera.magnitude;
            if (dist < 1e-6f) return;
            Vector3 dirToCamera = fromParentToCamera / dist;

            // Sit just outside the parent toward the user.
            transform.position = parentPos + dirToCamera * forwardOffset;
            // Face the camera so the canvas plane is normal to the gaze ray.
            transform.rotation = Quaternion.LookRotation(-dirToCamera, Vector3.up);
        }
    }
}
