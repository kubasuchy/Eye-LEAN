using UnityEngine;
using EyeTracking.Core;
using EyeTracking.Vergence;
using EyeTracking.Data;          // DataQualityMetrics
using EyeTracking.Configuration; // VergenceSettingsFile (used by editor menu paths)
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace EyeTracking.Components
{
    /// <summary>
    /// Per-frame eye-data acquisition + vergence + ray/vergence-point
    /// visualization + gazed-object dispatch. Per-scene MonoBehaviour;
    /// do not mark DontDestroyOnLoad (stale cameraTransform refs after
    /// scene transitions cause floods of NREs). Exposes
    /// <see cref="SampleSnapshot"/> for SessionRecorder's LateUpdate.
    ///
    /// Visualization-only origin offset: the LineRenderer ray origin is
    /// laterally offset ±8 cm from <c>cameraTransform.position</c>, NOT
    /// the tracker's eye origin. Required to avoid a "flat 2D ray
    /// ghost" billboard degeneracy under URP single-pass instanced
    /// rendering (Unity 6000.3.9f1). Affects ONLY the LineRenderer;
    /// snapshot, CSV and vergence math use the true tracker origins.
    /// </summary>
    public class EyeTracker : MonoBehaviour
    {
        [Header("Vergence Depth Mode")]
        [Tooltip("TrueConvergence: eye convergence math (~1.5-2.3m hardware cap on VIVE). DepthExtension: math vergence near, raycast extension to colliders far.")]
        [SerializeField] private VergenceDepthMode vergenceDepthMode = VergenceDepthMode.DepthExtension;

        [Header("Camera (auto-resolved if null)")]
        [SerializeField] private Transform cameraTransform;

        [Header("Vergence Configuration")]
        [Tooltip("Vergence calculation method, smoothing, and validation thresholds. Pick a preset (Conservative/Balanced/Sensitive) or hand-tune. See VergenceCalculationSettings for fields.")]
        [SerializeField] private VergenceCalculationSettings vergenceSettings;

        [Header("Debug Visualization")]
        [Tooltip("Show debug rays from each eye. Setting this true also creates the LineRenderer GameObjects on Start.")]
        [SerializeField] private bool showDebugRays = true;
        [Tooltip("Show vergence point indicator. Setting this true also creates the vergence point GameObject on Start.")]
        [SerializeField] private bool showVergencePoint = true;
        [Tooltip("Length of each per-eye debug ray, in meters.")]
        [SerializeField] private float rayLength = 10f;
        [Tooltip("Tint of the left-eye debug ray.")]
        [SerializeField] private Color leftRayColor = Color.red;
        [Tooltip("Tint of the right-eye debug ray.")]
        [SerializeField] private Color rightRayColor = Color.blue;
        [Tooltip("Tint of the vergence-point sphere.")]
        [SerializeField] private Color vergenceColor = Color.cyan;
        [Tooltip("Diameter of the vergence-point sphere, in meters.")]
        [SerializeField] private float vergencePointSize = 0.05f;

        [Header("Depth Extension (DepthExtension mode only)")]
        [Tooltip("Depth threshold where the VIVE Focus Vision's math vergence saturates. Beyond this, surface raycast takes over to recover far gaze. Default 2 m matches the device's hardware cap.")]
        [SerializeField] private float hardwareDepthLimit = 2f;
        [Tooltip("Maximum raycast distance for depth extension, in meters.")]
        [SerializeField] private float maxRaycastDistance = 20f;
        [Tooltip("Distance the gaze sphere lands at when no surface is hit and the rays are parallel/diverging, in meters.")]
        [SerializeField] private float fallbackFarDistance = 5f;

        // Visualization-only lateral offset (see class summary).
        private const float VizOriginLateralOffsetMeters = 0.08f;

        // LineRenderers + vergence point visualization
        private LineRenderer leftLineRenderer;
        private LineRenderer rightLineRenderer;
        private GameObject vergencePointVisual;

        // Eye tracking data (raw, world-space)
        private Vector3 leftOrigin, rightOrigin;
        private Vector3 leftDirection, rightDirection;
        private float leftOpenness, rightOpenness;
        private float leftPupilDiameter, rightPupilDiameter;
        private Vector2 leftPupilPosition, rightPupilPosition;
        private bool hasValidLeftData, hasValidRightData;
        private bool hasLeftOpenness, hasRightOpenness;
        private bool hasLeftPupilDiameter, hasRightPupilDiameter;
        private bool hasLeftPupilPosition, hasRightPupilPosition;

        // Vergence
        private VergenceSmoothingProcessor smoothingProcessor;
        private VergenceCalculationResult currentVergenceResult;

        // Gazed-object dispatch
        private GameObject currentGazedObject = null;
        private GameObject previousGazedObject = null;
        private GazeTarget currentGazeTarget = null;

        // Debug mode (locked once set)
        private bool _debugMode = false;
        private bool _debugModeWasSet = false;

        // Per-frame state
        private int frameCount = 0;
        private float sessionStartTime;

        // Eye tracker abstraction
        private IEyeTracker eyeTracker;

        // Quality metrics integration
        private DataQualityMetrics dataQualityMetrics;

        // Public accessors

        public bool HasValidGazeData() => hasValidLeftData || hasValidRightData;
        public Vector3 GetCombinedGazeOrigin() => CombinedOriginInternal();
        public Vector3 GetCombinedGazeDirection() => CombinedDirectionInternal();
        public VergenceCalculationResult GetCurrentVergenceResult() => currentVergenceResult;
        public GameObject GetCurrentGazedObject() => currentGazedObject;
        public GazeTarget GetCurrentGazeTarget() => currentGazeTarget;
        public bool IsDebugMode() => _debugMode;
        public string GetModeStatus() => _debugMode ? "DEBUG (Visual feedback active)" : "PRODUCTION (No visual feedback)";

        public VergenceDepthMode CurrentVergenceDepthMode
        {
            get => vergenceDepthMode;
            set
            {
                if (vergenceDepthMode != value)
                {
                    vergenceDepthMode = value;
                    Debug.Log($"[EyeTracker] Vergence depth mode changed to: {vergenceDepthMode}");
                }
            }
        }

        public float HardwareDepthLimit
        {
            get => hardwareDepthLimit;
            set
            {
                hardwareDepthLimit = Mathf.Max(0.5f, value);
                Debug.Log($"[EyeTracker] Hardware depth limit set to: {hardwareDepthLimit:F1}m");
            }
        }

        private void Awake()
        {
            // Diagnostic: prove this component was instantiated. Sibling
            // MonoBehaviours can be silently dropped when Unity's Library/
            // asset cache for the .unity file is stale, so a definitive
            // alive-log helps diagnose missing-component reports.
            Debug.Log($"[EyeTracker] Awake on '{gameObject.name}' (instance {GetInstanceID()})");

            // Self-heal: if EyeTracker exists on this GameObject but the
            // siblings don't, runtime-add them so the recording pipeline
            // isn't dead in the water. Covers the same stale-Library-cache
            // failure mode that drops siblings from .unity scene YAML.
            if (GetComponent<HMDDataCollector>() == null)
            {
                Debug.LogWarning($"[EyeTracker] HMDDataCollector missing on '{gameObject.name}' — runtime-adding it.");
                gameObject.AddComponent<HMDDataCollector>();
            }
            if (GetComponent<SessionRecorder>() == null)
            {
                Debug.LogWarning($"[EyeTracker] SessionRecorder missing on '{gameObject.name}' — runtime-adding it.");
                gameObject.AddComponent<SessionRecorder>();
            }
        }

        private void Start()
        {
            if (cameraTransform == null)
            {
                cameraTransform = Camera.main != null ? Camera.main.transform : transform;
                Debug.Log($"[EyeTracker] Auto-assigned camera: {cameraTransform.name}");
            }

            if (vergenceSettings == null) vergenceSettings = new VergenceCalculationSettings();
            if (vergenceSettings.smoothing.weightedEMA.bufferSize == 0) vergenceSettings.smoothing.weightedEMA.bufferSize = 5;

            sessionStartTime = Time.time;
            smoothingProcessor = new VergenceSmoothingProcessor(vergenceSettings.smoothing);

            Debug.Log($"[EyeTracker] Vergence Configuration: mode={vergenceDepthMode}, preset={vergenceSettings.preset}, method={vergenceSettings.method}");

            InitializeEyeTracking();

            dataQualityMetrics = GetComponent<DataQualityMetrics>();
            if (dataQualityMetrics != null) Debug.Log("[EyeTracker] DataQualityMetrics detected — quality tracking enabled");

            // Auto-enable debug mode whenever a visualization flag is on,
            // so the LineRenderers / vergence-point GameObjects are
            // actually created on Start (otherwise toggling showDebugRays
            // / showVergencePoint alone produces null renderers).
            if (!_debugModeWasSet && (showDebugRays || showVergencePoint))
            {
                Debug.Log($"[EyeTracker] Auto-enabling debug mode (showDebugRays={showDebugRays}, showVergencePoint={showVergencePoint})");
                SetDebugMode(true);
            }

            Debug.Log("[EyeTracker] Initialized");
        }

        private void Update()
        {
            frameCount++;

            bool hasValidData = CollectEyeTrackingData();

            if (dataQualityMetrics != null)
            {
                Vector3 gazeDir = HasValidGazeData() ? CombinedDirectionInternal() : Vector3.forward;
                dataQualityMetrics.RecordSample(hasValidData, gazeDir,
                    hasLeftOpenness ? leftOpenness : -1f,
                    hasRightOpenness ? rightOpenness : -1f);
            }

            if (hasValidData)
            {
                UpdateVergenceCalculation();
                if (_debugMode)
                {
                    UpdateRayVisualizations();
                    UpdateVergencePointVisual();
                }
            }
            else
            {
                UpdateHeadGazeFallback();
                if (_debugMode) HideAllVisualizations();
            }
        }

        private void InitializeEyeTracking()
        {
            eyeTracker = EyeTrackerFactory.GetEyeTracker();
            if (eyeTracker != null && eyeTracker.IsAvailable)
            {
                Debug.Log($"[EyeTracker] Initialized — Device: {eyeTracker.DeviceName}");
#if USE_OPENXR
                if (VRNavigation.Tracking.OpenXREyeTracker.Instance != null)
                    VRNavigation.Tracking.OpenXREyeTracker.Instance.EnableEyeTracking = true;
#endif
            }
            else
            {
                Debug.LogWarning($"[EyeTracker] Eye tracker not available — Device: {eyeTracker?.DeviceName ?? "None"}");
            }
        }

        /// <summary>Pull this frame's per-eye data via the IEyeTracker abstraction.</summary>
        private bool CollectEyeTrackingData()
        {
            // Lazy retry: if our cached tracker is unavailable, ask the factory
            // again — OpenXREyeTracker may have just initialized this frame.
            if (eyeTracker == null || !eyeTracker.IsAvailable)
            {
                if (eyeTracker != null && !eyeTracker.IsAvailable) EyeTrackerFactory.Reinitialize();
                eyeTracker = EyeTrackerFactory.GetEyeTracker();
            }
            if (eyeTracker == null || !eyeTracker.IsAvailable) return false;

            hasValidLeftData = eyeTracker.GetLeftEyeOrigin(out leftOrigin) && eyeTracker.GetLeftEyeDirection(out leftDirection);
            hasValidRightData = eyeTracker.GetRightEyeOrigin(out rightOrigin) && eyeTracker.GetRightEyeDirection(out rightDirection);

            hasLeftOpenness = eyeTracker.GetLeftEyeOpenness(out leftOpenness);
            hasRightOpenness = eyeTracker.GetRightEyeOpenness(out rightOpenness);
            hasLeftPupilDiameter = eyeTracker.GetLeftPupilDiameter(out leftPupilDiameter);
            hasRightPupilDiameter = eyeTracker.GetRightPupilDiameter(out rightPupilDiameter);
            hasLeftPupilPosition = eyeTracker.GetLeftPupilPosition(out leftPupilPosition);
            hasRightPupilPosition = eyeTracker.GetRightPupilPosition(out rightPupilPosition);

            return hasValidLeftData || hasValidRightData;
        }

        /// <summary>Build the per-frame snapshot for SessionRecorder.</summary>
        public EyeFrameSample SampleSnapshot()
        {
            bool hasCombined = hasValidLeftData || hasValidRightData;
            Vector3 combinedOrigin = CombinedOriginInternal();
            Vector3 combinedDirection = CombinedDirectionInternal();

            return new EyeFrameSample(
                hasLeftValid: hasValidLeftData, leftOrigin: leftOrigin, leftDirection: leftDirection,
                leftOpenness: leftOpenness, leftPupilDiameter: leftPupilDiameter, leftPupilPosition: leftPupilPosition,
                hasRightValid: hasValidRightData, rightOrigin: rightOrigin, rightDirection: rightDirection,
                rightOpenness: rightOpenness, rightPupilDiameter: rightPupilDiameter, rightPupilPosition: rightPupilPosition,
                hasCombinedValid: hasCombined, combinedOrigin: combinedOrigin, combinedDirection: combinedDirection,
                hasValidVergence: currentVergenceResult.isValid,
                vergencePoint: currentVergenceResult.finalVergencePoint,
                vergenceQuality: currentVergenceResult.quality,
                gazedObjectName: currentGazedObject != null ? currentGazedObject.name : string.Empty);
        }

        private Vector3 CombinedOriginInternal()
        {
            if (hasValidLeftData && hasValidRightData) return (leftOrigin + rightOrigin) * 0.5f;
            if (hasValidLeftData) return leftOrigin;
            if (hasValidRightData) return rightOrigin;
            return Vector3.zero;
        }

        private Vector3 CombinedDirectionInternal()
        {
            if (hasValidLeftData && hasValidRightData) return ((leftDirection + rightDirection) * 0.5f).normalized;
            if (hasValidLeftData) return leftDirection;
            if (hasValidRightData) return rightDirection;
            return Vector3.forward;
        }

        // --- Visualization ---

        private void UpdateRayVisualizations()
        {
            if (!showDebugRays)
            {
                if (leftLineRenderer != null) leftLineRenderer.enabled = false;
                if (rightLineRenderer != null) rightLineRenderer.enabled = false;
                return;
            }
            if (cameraTransform == null) return;

            // VISUALIZATION-ONLY origin offset (see class summary).
            if (leftLineRenderer != null && hasValidLeftData)
            {
                Vector3 vizOrigin = cameraTransform.position - cameraTransform.right * VizOriginLateralOffsetMeters;
                UpdateLineRenderer(leftLineRenderer, vizOrigin, leftDirection);
            }
            else if (leftLineRenderer != null) leftLineRenderer.enabled = false;

            if (rightLineRenderer != null && hasValidRightData)
            {
                Vector3 vizOrigin = cameraTransform.position + cameraTransform.right * VizOriginLateralOffsetMeters;
                UpdateLineRenderer(rightLineRenderer, vizOrigin, rightDirection);
            }
            else if (rightLineRenderer != null) rightLineRenderer.enabled = false;
        }

        private void UpdateLineRenderer(LineRenderer lr, Vector3 origin, Vector3 direction)
        {
            if (lr == null) return;
            Vector3 rayEnd = origin + direction * rayLength;
            lr.SetPosition(0, origin);
            lr.SetPosition(1, rayEnd);
            lr.enabled = true;
        }

        private void UpdateVergencePointVisual()
        {
            if (vergencePointVisual == null) return;
            if (showVergencePoint && currentVergenceResult.isValid)
            {
                vergencePointVisual.SetActive(true);
                vergencePointVisual.transform.position = currentVergenceResult.finalVergencePoint;
            }
            else
            {
                vergencePointVisual.SetActive(false);
            }
        }

        private void HideAllVisualizations()
        {
            if (leftLineRenderer != null) leftLineRenderer.enabled = false;
            if (rightLineRenderer != null) rightLineRenderer.enabled = false;
            if (vergencePointVisual != null) vergencePointVisual.SetActive(false);
        }

        private void CreateVisualizationObjects()
        {
            if (!_debugMode) return;
            leftLineRenderer = CreateLineRenderer("LeftEyeRay", leftRayColor);
            rightLineRenderer = CreateLineRenderer("RightEyeRay", rightRayColor);
            CreateVergencePointVisual();
        }

        private LineRenderer CreateLineRenderer(string name, Color color)
        {
            var rayObj = new GameObject(name);
            rayObj.transform.SetParent(this.transform);
            var lr = rayObj.AddComponent<LineRenderer>();
            lr.material = CreateLineMaterial(color);
            lr.startWidth = 0.005f;
            lr.endWidth = 0.005f;
            lr.startColor = color;
            lr.endColor = color;
            lr.useWorldSpace = true;
            lr.positionCount = 2;
            lr.alignment = LineAlignment.View;
            lr.textureMode = LineTextureMode.Tile;
            lr.sortingOrder = 100;
            lr.receiveShadows = false;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            Debug.Log($"[EyeTracker] Created LineRenderer: {name} ({color})");
            return lr;
        }

        private Material CreateLineMaterial(Color color)
        {
            Material m = VRMaterialProvider.GetMaterial(color, false);
            if (m == null)
            {
                Shader shader = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color");
                if (shader != null) { m = new Material(shader); m.color = color; }
            }
            return m;
        }

        private void CreateVergencePointVisual()
        {
            vergencePointVisual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            vergencePointVisual.name = "VergencePoint";
            vergencePointVisual.transform.localScale = Vector3.one * vergencePointSize;
            vergencePointVisual.transform.SetParent(this.transform);
            var renderer = vergencePointVisual.GetComponent<Renderer>();
            renderer.material = VRMaterialProvider.GetMaterial(vergenceColor, false) ?? new Material(Shader.Find("Unlit/Color")) { color = vergenceColor };
            // Strip ALL physics so the vergence sphere can't push agents around.
            var col = vergencePointVisual.GetComponent<Collider>();
            if (col != null) DestroyImmediate(col);
            var rb = vergencePointVisual.GetComponent<Rigidbody>();
            if (rb != null) DestroyImmediate(rb);
            int ignoreLayer = LayerMask.NameToLayer("Ignore Raycast");
            if (ignoreLayer >= 0) vergencePointVisual.layer = ignoreLayer;
            vergencePointVisual.SetActive(false);
            Debug.Log("[EyeTracker] Vergence point visual created (no collider, no rigidbody)");
        }

        // --- Vergence calculation (DepthExtension + TrueConvergence + Simple + PaperAlgorithm) ---

        private void UpdateVergenceCalculation()
        {
            currentVergenceResult = vergenceDepthMode == VergenceDepthMode.DepthExtension
                ? CalculateVergenceWithDepthExtension()
                : CalculateVergencePoint();

            // Once-per-second depth-extension diagnostic. Surfaces WHY the
            // vergence point landed where it did (math vs surface vs
            // extension vs fallback) for hardware-test debugging.
            if (frameCount % 60 == 0 && _debugMode && currentVergenceResult.isValid)
            {
                Debug.Log($"[EyeTracker] {(vergenceDepthMode == VergenceDepthMode.DepthExtension ? "DEPTH EXTENSION" : "TRUE CONVERGENCE")} MODE: {currentVergenceResult.debugInfo}");
            }
        }

        private VergenceCalculationResult CalculateVergenceWithDepthExtension()
        {
            var result = new VergenceCalculationResult();
            result.methodUsed = VergenceCalculationMethod.Simple;

            if (!hasValidLeftData && !hasValidRightData) { result.isValid = false; result.debugInfo = "No eye data"; return result; }

            // leftOrigin/rightOrigin are world-space (OpenXREyeTrackerProvider applies
            // the tracking-space → world-space transform upstream); do NOT re-transform.
            Vector3 centerOrigin = (leftOrigin + rightOrigin) * 0.5f;
            Vector3 leftRayDir = leftDirection.normalized;
            Vector3 rightRayDir = rightDirection.normalized;
            Vector3 centerDir = (leftRayDir + rightRayDir).normalized;

            bool foundIntersection = CalculateRayIntersection(leftOrigin, leftRayDir, rightOrigin, rightRayDir,
                out Vector3 intersectionPoint, out float convergenceDistance);

            RaycastHit surfaceHit;
            bool hitSurface = Physics.Raycast(centerOrigin, centerDir, out surfaceHit, maxRaycastDistance, -1);

            if (!foundIntersection || convergenceDistance < 0.1f)
            {
                if (hitSurface)
                {
                    result.rawVergencePoint = surfaceHit.point;
                    result.finalVergencePoint = surfaceHit.point;
                    result.isValid = true;
                    result.quality = 0.7f;
                    result.debugInfo = $"Parallel gaze, surface at {surfaceHit.distance:F1}m ({surfaceHit.collider.name})";
                    UpdateGazedObjectFromHit(surfaceHit);
                }
                else
                {
                    result.rawVergencePoint = centerOrigin + centerDir * fallbackFarDistance;
                    result.finalVergencePoint = result.rawVergencePoint;
                    result.isValid = true;
                    result.quality = 0.5f;
                    result.debugInfo = $"Parallel/diverging, fallback {fallbackFarDistance:F1}m";
                    SetGazedObjectNull();
                }
                return result;
            }

            // The point of depth-extension is to recover from the VIVE
            // Focus Vision's hardware-limited math vergence (~1.5-2.3m
            // cap; inter-eye angle barely changes for fixations beyond
            // ~2m). Always-take-the-closer is wrong because the math
            // always wins past the cap and the vergence point pins near
            // 2m regardless of intent.
            //
            // Correct behavior:
            //   1) Surface CLOSER than math convergence -> use surface
            //      (a near wall blocks an attempted far gaze).
            //   2) Math saturated near hardwareDepthLimit AND surface
            //      farther out -> use surface (extension).
            //   3) Otherwise -> trust the math (the angular intent is
            //      most accurate when both eyes give clean convergence).
            const float hardwareSaturationMargin = 0.3f; // within 0.3m of cap = saturated
            bool mathSaturated = convergenceDistance >= (hardwareDepthLimit - hardwareSaturationMargin);
            Vector3 finalPoint;
            string debugSource;
            if (hitSurface && surfaceHit.distance < convergenceDistance)
            {
                finalPoint = surfaceHit.point;
                debugSource = $"Surface (closer) ({surfaceHit.collider.name}) at {surfaceHit.distance:F2}m";
            }
            else if (hitSurface && mathSaturated)
            {
                finalPoint = surfaceHit.point;
                debugSource = $"Surface (extension; math saturated at {convergenceDistance:F2}m) ({surfaceHit.collider.name}) at {surfaceHit.distance:F2}m";
            }
            else
            {
                float clampedDistance = Mathf.Clamp(convergenceDistance, 0.3f, maxRaycastDistance);
                finalPoint = intersectionPoint;
                debugSource = $"Ray intersection at {clampedDistance:F2}m";
                if (hitSurface) debugSource += $" (surface at {surfaceHit.distance:F2}m)";
            }
            result.rawVergencePoint = finalPoint;
            result.isValid = true;
            result.quality = hitSurface ? 0.9f : 0.7f;
            result.debugInfo = debugSource;

            float distanceToPoint = Vector3.Distance(centerOrigin, finalPoint);
            if (vergenceSettings.smoothing.enableSmoothing)
            {
                result.finalVergencePoint = smoothingProcessor.ProcessPoint(result.rawVergencePoint, result.quality, distanceToPoint);
                result.debugInfo += " [smoothed]";
            }
            else result.finalVergencePoint = result.rawVergencePoint;

            if (hitSurface) UpdateGazedObjectFromHit(surfaceHit);
            else SetGazedObjectNull();
            return result;
        }

        private VergenceCalculationResult CalculateVergencePoint()
        {
            var result = new VergenceCalculationResult();
            if (vergenceSettings.validation.requireBothEyes && (!hasValidLeftData || !hasValidRightData))
            { result.isValid = false; result.debugInfo = "Both eyes required"; return result; }
            if (!hasValidLeftData && !hasValidRightData) { result.isValid = false; result.debugInfo = "No eye data"; return result; }

            result.methodUsed = vergenceSettings.method;
            switch (vergenceSettings.method)
            {
                case VergenceCalculationMethod.PaperAlgorithm:
                    result = CalculateUsingPaperAlgorithm(leftOrigin, rightOrigin, leftDirection, rightDirection); break;
                case VergenceCalculationMethod.Simple:
                default:
                    result = CalculateUsingSimpleMethod(leftOrigin, rightOrigin, leftDirection, rightDirection); break;
            }
            if (!result.isValid)
            {
                var fallback = vergenceSettings.method == VergenceCalculationMethod.Simple
                    ? CalculateUsingPaperAlgorithm(leftOrigin, rightOrigin, leftDirection, rightDirection)
                    : CalculateUsingSimpleMethod(leftOrigin, rightOrigin, leftDirection, rightDirection);
                if (fallback.isValid) { result = fallback; result.debugInfo += " (fallback)"; }
            }
            if (result.isValid && vergenceSettings.smoothing.enableSmoothing)
            {
                Vector3 headCenter = (leftOrigin + rightOrigin) * 0.5f;
                float distance = Vector3.Distance(headCenter, result.rawVergencePoint);
                result.finalVergencePoint = smoothingProcessor.ProcessPoint(result.rawVergencePoint, result.quality, distance);
            }
            else result.finalVergencePoint = result.rawVergencePoint;
            return result;
        }

        private VergenceCalculationResult CalculateUsingSimpleMethod(Vector3 p1, Vector3 p2, Vector3 d1, Vector3 d2)
        {
            var result = new VergenceCalculationResult { methodUsed = VergenceCalculationMethod.Simple };
            Vector3 w0 = p1 - p2;
            float a = Vector3.Dot(d1, d1), b = Vector3.Dot(d1, d2), c = Vector3.Dot(d2, d2);
            float d = Vector3.Dot(d1, w0), e = Vector3.Dot(d2, w0);
            float denom = a * c - b * b;
            if (Mathf.Abs(denom) < 0.0001f) { result.isValid = false; result.debugInfo = "Rays parallel"; return result; }
            float sc = (b * e - c * d) / denom, tc = (a * e - b * d) / denom;
            Vector3 point1 = p1 + sc * d1, point2 = p2 + tc * d2;
            result.rawVergencePoint = (point1 + point2) * 0.5f;
            result.quality = Vector3.Distance(point1, point2);
            Vector3 headCenter = (p1 + p2) * 0.5f;
            float distance = Vector3.Distance(headCenter, result.rawVergencePoint);
            result.isValid = distance >= vergenceSettings.distanceRange.minDistance && distance <= vergenceSettings.distanceRange.maxDistance;
            result.debugInfo = $"Simple: dist={distance:F2}, q={result.quality:F3}";
            return result;
        }

        private VergenceCalculationResult CalculateUsingPaperAlgorithm(Vector3 lo, Vector3 ro, Vector3 ld, Vector3 rd)
        {
            var result = new VergenceCalculationResult { methodUsed = VergenceCalculationMethod.PaperAlgorithm };
            if (!hasValidLeftData || !hasValidRightData) { result.isValid = false; result.debugInfo = "Paper algorithm needs both eyes"; return result; }
            bool ok = CalculateVectorVectorIntersection(lo, ro, ld, rd, out float t1, out float t2);
            if (!ok || t1 < 0f || t2 < 0f) { result.isValid = false; result.debugInfo = "No valid intersection"; return result; }
            Vector3 li = lo + t1 * ld, ri = ro + t2 * rd;
            result.quality = Vector3.Distance(li, ri);
            result.rawVergencePoint = (li + ri) * 0.5f;
            result.isValid = ValidateVergenceResult(result, lo, ro, ld, rd);
            result.debugInfo = $"Paper: t1={t1:F3}, t2={t2:F3}, q={result.quality:F3}";
            return result;
        }

        private bool ValidateVergenceResult(VergenceCalculationResult r, Vector3 lo, Vector3 ro, Vector3 ld, Vector3 rd)
        {
            var v = vergenceSettings.validation;
            var dr = vergenceSettings.distanceRange;
            if (r.quality > v.maxVergenceDistance) return false;
            Vector3 headCenter = (lo + ro) * 0.5f;
            float distance = Vector3.Distance(headCenter, r.rawVergencePoint);
            if (distance < dr.minDistance || distance > dr.maxDistance) return false;
            float convAngle = Vector3.Angle(ld, rd);
            if (convAngle > v.maxConvergenceAngle || convAngle < v.minConvergenceAngle) return false;
            Vector3 headForward = (ld + rd).normalized;
            Vector3 toGazePoint = (r.rawVergencePoint - headCenter).normalized;
            return Vector3.Dot(headForward, toGazePoint) >= 0f;
        }

        public bool CalculateVectorVectorIntersection(Vector3 P1, Vector3 P3, Vector3 R2, Vector3 R4, out float t1, out float t2)
        {
            Vector3 p13 = P1 - P3;
            t1 = t2 = 0f;
            float r4dotr4 = Vector3.Dot(R4, R4);
            float r2dotr2 = Vector3.Dot(R2, R2);
            float r2dotr4 = Vector3.Dot(R2, R4);
            float denom = Mathf.Pow(r2dotr4, 2) - (r2dotr2 * r4dotr4);
            if (Mathf.Abs(r2dotr4) < Mathf.Epsilon || Mathf.Abs(denom) < Mathf.Epsilon) return false;
            t2 = ((Vector3.Dot(p13, R2) * r4dotr4) - (Vector3.Dot(p13, R4) * r2dotr4)) / denom;
            t1 = (Vector3.Dot(p13, R2) + t2 * r2dotr2) / r2dotr4;
            return true;
        }

        private bool CalculateRayIntersection(Vector3 p1, Vector3 d1, Vector3 p2, Vector3 d2, out Vector3 intersection, out float distance)
        {
            intersection = Vector3.zero;
            distance = 0f;
            Vector3 w0 = p1 - p2;
            float a = Vector3.Dot(d1, d1), b = Vector3.Dot(d1, d2), c = Vector3.Dot(d2, d2);
            float d = Vector3.Dot(d1, w0), e = Vector3.Dot(d2, w0);
            float denom = a * c - b * b;
            if (Mathf.Abs(denom) < 0.0001f) return false;
            float t1 = (b * e - c * d) / denom, t2 = (a * e - b * d) / denom;
            if (t1 < 0 || t2 < 0) return false;
            Vector3 closest1 = p1 + d1 * t1, closest2 = p2 + d2 * t2;
            intersection = (closest1 + closest2) * 0.5f;
            distance = (t1 + t2) * 0.5f;
            // Reject NaN / Inf / unreasonable distances.
            if (float.IsNaN(intersection.x) || float.IsNaN(intersection.y) || float.IsNaN(intersection.z) ||
                float.IsInfinity(intersection.x) || float.IsInfinity(intersection.y) || float.IsInfinity(intersection.z) ||
                float.IsNaN(distance) || float.IsInfinity(distance) || distance > 1000f)
            {
                intersection = Vector3.zero;
                distance = 0f;
                return false;
            }
            return true;
        }

        // --- Gaze target dispatch ---

        private void UpdateGazedObjectFromHit(RaycastHit hit)
        {
            if (!hit.collider.isTrigger && !hit.collider.name.Contains("VisionCone") && !hit.collider.name.Contains("Trigger"))
            {
                currentGazedObject = hit.collider.gameObject;
                UpdateGazeTargetNotifications(hit.point, hit.distance);
            }
            else SetGazedObjectNull();
        }

        private void SetGazedObjectNull()
        {
            currentGazedObject = null;
            UpdateGazeTargetNotifications(Vector3.zero, 0f);
        }

        private void UpdateGazeTargetNotifications(Vector3 hitPoint, float gazeDistance)
        {
            if (currentGazedObject != previousGazedObject)
            {
                if (previousGazedObject != null)
                {
                    var prev = previousGazedObject.GetComponent<GazeTarget>();
                    if (prev != null) prev.OnGazeExit();
                }
                if (currentGazedObject != null)
                {
                    currentGazeTarget = currentGazedObject.GetComponent<GazeTarget>();
                    if (currentGazeTarget != null) currentGazeTarget.OnGazeEnter(hitPoint, gazeDistance);
                }
                else currentGazeTarget = null;
                Debug.Log($"[EyeTracker] GAZE TRANSITION: {(previousGazedObject != null ? previousGazedObject.name : "null")} -> {(currentGazedObject != null ? currentGazedObject.name : "null")}");
                previousGazedObject = currentGazedObject;
            }
            else if (currentGazeTarget != null && currentGazedObject != null)
            {
                currentGazeTarget.OnGazeStay(hitPoint, gazeDistance);
            }
        }

        public void ResetGazedObject()
        {
            currentGazedObject = null;
            previousGazedObject = null;
            currentGazeTarget = null;
            Debug.Log("[EyeTracker] Gazed object reset");
        }

        // --- Head-gaze fallback (when no eye data) ---

        private void UpdateHeadGazeFallback()
        {
            Camera mainCamera = Camera.main;
            if (mainCamera == null) return;
            Vector3 origin = mainCamera.transform.position;
            Vector3 direction = mainCamera.transform.forward;
            float maxDistance = vergenceSettings != null ? vergenceSettings.distanceRange.maxDistance : 50f;
            RaycastHit[] hits = Physics.RaycastAll(origin, direction, maxDistance);

            RaycastHit best = default;
            bool found = false;
            float closest = float.MaxValue;
            foreach (var h in hits)
            {
                if (h.collider.isTrigger) continue;
                if (h.collider.name.Contains("VisionCone") || h.collider.name.Contains("Trigger") || h.collider.name.Contains("Exit")) continue;
                if (h.distance < closest) { closest = h.distance; best = h; found = true; }
            }

            if (found) { currentGazedObject = best.collider.gameObject; UpdateGazeTargetNotifications(best.point, best.distance); }
            else SetGazedObjectNull();
        }

        // --- Debug mode (locked once set) ---

        public void SetDebugMode(bool enabled)
        {
            if (_debugModeWasSet && enabled != _debugMode)
            {
                Debug.LogWarning($"[EyeTracker] SetDebugMode called with {enabled} after lock; ignoring.");
                return;
            }
            _debugMode = enabled;
            _debugModeWasSet = true;
            Debug.Log($"[EyeTracker] Debug mode locked to: {enabled}");
            if (enabled && leftLineRenderer == null && rightLineRenderer == null)
            {
                CreateVisualizationObjects();
            }
        }

        // --- Vergence runtime tuning ---

        public void UpdateVergenceSettings(VergenceCalculationSettings newSettings)
        {
            vergenceSettings = newSettings;
            if (smoothingProcessor != null) smoothingProcessor.UpdateSettings(newSettings.smoothing);
            Debug.Log($"[EyeTracker] Vergence settings updated: preset={newSettings.preset}");
        }

        public void SetVergencePreset(VergencePreset preset) => UpdateVergenceSettings(VergenceCalculationSettings.GetPreset(preset));

        public void ResetVergenceSmoothing()
        {
            if (smoothingProcessor != null) smoothingProcessor.Reset();
        }

        public DataQualityMetrics GetQualityMetrics() => dataQualityMetrics;

        public void LogPerformanceStats()
        {
            float duration = Time.time - sessionStartTime;
            Debug.Log($"[EyeTracker] Performance — FPS: {(frameCount / Mathf.Max(0.001f, duration)):F1}, Frames: {frameCount}, Duration: {duration:F1}s");
        }

#if UNITY_EDITOR
        // --- Vergence settings JSON import/export (editor only, context menu) ---

        [ContextMenu("Export Vergence Settings")]
        public void ExportVergenceSettings()
        {
            string path = EditorUtility.SaveFilePanel("Export Vergence Settings", Application.dataPath, "VergenceSettings.json", "json");
            if (string.IsNullOrEmpty(path)) return;
            var file = new VergenceSettingsFile
            {
                fileVersion = "1.1",
                savedAt = System.DateTime.UtcNow.ToString("o"),
                vergenceDepthMode = vergenceDepthMode.ToString(),
                vergenceSettings = vergenceSettings,
            };
            System.IO.File.WriteAllText(path, JsonUtility.ToJson(file, true));
            Debug.Log($"[EyeTracker] Vergence settings exported to: {path}");
        }

        [ContextMenu("Import Vergence Settings")]
        public void ImportVergenceSettings()
        {
            string path = EditorUtility.OpenFilePanel("Import Vergence Settings", Application.dataPath, "json");
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                var loaded = JsonUtility.FromJson<VergenceSettingsFile>(System.IO.File.ReadAllText(path));
                if (loaded == null) return;
                if (System.Enum.TryParse<VergenceDepthMode>(loaded.vergenceDepthMode, out var dm)) vergenceDepthMode = dm;
                vergenceSettings = loaded.vergenceSettings;
                EditorUtility.SetDirty(this);
                Debug.Log($"[EyeTracker] Vergence settings imported: preset={vergenceSettings.preset}, mode={vergenceDepthMode}");
            }
            catch (System.Exception e) { Debug.LogError($"[EyeTracker] Import failed: {e.Message}"); }
        }
#endif
    }
}
