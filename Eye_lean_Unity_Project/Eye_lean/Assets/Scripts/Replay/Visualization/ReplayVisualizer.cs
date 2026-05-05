using UnityEngine;
using EyeLean.Replay.Analysis;

namespace EyeLean.Replay.Visualization
{
    /// <summary>
    /// Visualizes eye tracking replay data with analysis overlays.
    /// Shows eye movement classification and attention type through color coding.
    /// </summary>
    [RequireComponent(typeof(ReplayController))]
    public class ReplayVisualizer : MonoBehaviour
    {
        #region References

        [Header("References")]
        [Tooltip("Reference to the ReplayController")]
        public ReplayController replayController;

        [Tooltip("Reference to the EyeMovementClassifier (optional)")]
        public EyeMovementClassifier classifier;

        [Tooltip("Reference to GazeEntropyCalculator (optional)")]
        public GazeEntropyCalculator entropyCalculator;

        #endregion

        #region Settings

        [Header("Vergence Point Visualization")]
        [Tooltip("Color vergence point by attention type")]
        public bool colorByAttention = true;

        [Tooltip("Show vergence point confidence via size")]
        public bool showConfidence = true;

        [Tooltip("Base size of vergence point")]
        [Range(0.02f, 0.1f)]
        public float baseVergenceSize = 0.05f;

        [Tooltip("Maximum size multiplier based on confidence")]
        [Range(1f, 3f)]
        public float maxSizeMultiplier = 2f;

        [Header("Gaze Ray Visualization")]
        [Tooltip("Color rays by movement type")]
        public bool colorRaysByMovement = true;

        [Tooltip("Fixation ray color")]
        public Color fixationColor = Color.green;

        [Tooltip("Saccade ray color")]
        public Color saccadeColor = Color.red;

        [Header("Trail Visualization")]
        [Tooltip("Show gaze trail")]
        public bool showGazeTrail = false;

        [Tooltip("Trail length in seconds")]
        [Range(0.5f, 5f)]
        public float trailDuration = 2f;

        [Tooltip("Trail color")]
        public Color trailColor = new Color(1f, 1f, 0f, 0.5f);

        [Header("Heatmap Overlay")]
        [Tooltip("Show gaze heatmap overlay")]
        public bool showHeatmap = false;

        [Tooltip("Heatmap opacity")]
        [Range(0f, 1f)]
        public float heatmapOpacity = 0.5f;

        #endregion

        #region Private Fields

        private Material vergenceMaterial;
        private TrailRenderer gazeTrail;
        private GameObject vergencePointObject;

        private EyeMovementClassifier.ClassificationResult lastClassification;

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            if (replayController == null)
            {
                replayController = GetComponent<ReplayController>();
            }
        }

        private void Start()
        {
            // Subscribe to replay events
            if (replayController != null)
            {
                replayController.OnFrameDisplayed += OnFrameDisplayed;
                replayController.OnStateChanged += OnStateChanged;
            }

            SetupVisualization();
        }

        private void OnDestroy()
        {
            if (replayController != null)
            {
                replayController.OnFrameDisplayed -= OnFrameDisplayed;
                replayController.OnStateChanged -= OnStateChanged;
            }

            CleanupVisualization();
        }

        #endregion

        #region Visualization Setup

        private void SetupVisualization()
        {
            if (showGazeTrail)
            {
                CreateGazeTrail();
            }
        }

        private void CreateGazeTrail()
        {
            GameObject trailObj = new GameObject("GazeTrail");
            trailObj.transform.SetParent(transform);

            gazeTrail = trailObj.AddComponent<TrailRenderer>();
            gazeTrail.time = trailDuration;
            gazeTrail.startWidth = 0.02f;
            gazeTrail.endWidth = 0.005f;
            gazeTrail.material = new Material(Shader.Find("Sprites/Default"));
            gazeTrail.startColor = trailColor;
            gazeTrail.endColor = new Color(trailColor.r, trailColor.g, trailColor.b, 0f);
            gazeTrail.enabled = false;
        }

        private void CleanupVisualization()
        {
            if (gazeTrail != null)
            {
                Destroy(gazeTrail.gameObject);
            }
        }

        #endregion

        #region Frame Update

        private void OnFrameDisplayed(ReplayFrame frame)
        {
            if (frame == null) return;

            // Classify the frame if classifier is available
            if (classifier != null)
            {
                lastClassification = classifier.ClassifyFrame(frame);
                UpdateVisualizationFromClassification(frame, lastClassification);
            }

            // Update entropy if calculator is available
            if (entropyCalculator != null)
            {
                entropyCalculator.AddSampleFromFrame(frame);
            }

            // Update trail
            if (showGazeTrail && gazeTrail != null && frame.hasValidVergence)
            {
                gazeTrail.enabled = true;
                gazeTrail.transform.position = frame.vergencePoint;
            }
        }

        private void OnStateChanged(ReplayState state)
        {
            if (state == ReplayState.Ready || state == ReplayState.Complete)
            {
                // Reset visualizations
                if (gazeTrail != null)
                {
                    gazeTrail.Clear();
                    gazeTrail.enabled = false;
                }

                if (classifier != null)
                {
                    classifier.Reset();
                }

                if (entropyCalculator != null)
                {
                    entropyCalculator.Reset();
                }
            }
        }

        private void UpdateVisualizationFromClassification(ReplayFrame frame, EyeMovementClassifier.ClassificationResult classification)
        {
            if (classification == null || !classification.isValid)
                return;

            // Update vergence point color based on attention type
            if (colorByAttention && frame.hasValidVergence)
            {
                Color attentionColor = classifier.GetKCoefficientColor(classification.kCoefficient);
                UpdateVergencePointColor(attentionColor);
            }

            // Update vergence point size based on confidence
            if (showConfidence && frame.hasValidVergence)
            {
                float confidence = Mathf.Clamp01(frame.vergenceQuality);
                float size = baseVergenceSize * Mathf.Lerp(1f, maxSizeMultiplier, confidence);
                UpdateVergencePointSize(size);
            }
        }

        private void UpdateVergencePointColor(Color color)
        {
            // Find vergence point in replay controller's children
            if (vergencePointObject == null)
            {
                vergencePointObject = transform.Find("VergencePoint")?.gameObject;
                if (vergencePointObject == null)
                {
                    var foundObj = GameObject.Find("VergencePoint");
                    if (foundObj != null && foundObj.transform.IsChildOf(transform))
                    {
                        vergencePointObject = foundObj;
                    }
                }
            }

            if (vergencePointObject != null)
            {
                var renderer = vergencePointObject.GetComponent<Renderer>();
                if (renderer != null)
                {
                    if (vergenceMaterial == null)
                    {
                        vergenceMaterial = renderer.material;
                    }
                    vergenceMaterial.color = color;
                }
            }
        }

        private void UpdateVergencePointSize(float size)
        {
            if (vergencePointObject != null)
            {
                vergencePointObject.transform.localScale = Vector3.one * size;
            }
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Get the current classification result
        /// </summary>
        public EyeMovementClassifier.ClassificationResult GetCurrentClassification()
        {
            return lastClassification;
        }

        /// <summary>
        /// Get current entropy result
        /// </summary>
        public GazeEntropyCalculator.EntropyResult GetCurrentEntropy()
        {
            if (entropyCalculator != null)
            {
                return entropyCalculator.CalculateEntropy();
            }
            return null;
        }

        /// <summary>
        /// Toggle gaze trail visibility
        /// </summary>
        public void SetTrailVisible(bool visible)
        {
            showGazeTrail = visible;
            if (gazeTrail != null)
            {
                gazeTrail.enabled = visible;
                if (!visible)
                {
                    gazeTrail.Clear();
                }
            }
        }

        /// <summary>
        /// Clear all visualizations
        /// </summary>
        public void ClearVisualizations()
        {
            if (gazeTrail != null)
            {
                gazeTrail.Clear();
            }
        }

        #endregion
    }
}
