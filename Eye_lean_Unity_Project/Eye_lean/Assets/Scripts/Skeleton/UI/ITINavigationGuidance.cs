// SPDX-License-Identifier: MIT
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;

/// <summary>
/// Helps participants return to and orient on the StartingPlatform during the
/// Inter-Trial Interval via a turn-around sign, hovering arrow, and floor arrow.
/// </summary>

namespace EyeLean.Skeleton
{
    public class ITINavigationGuidance : MonoBehaviour
    {
        [Header("Turn Around Sign Settings")]
        [Tooltip("Enable the Turn Around sign in front of player")]
        [SerializeField] private bool enableTurnAroundSign = true;

        [Tooltip("Distance in front of player to show sign (meters)")]
        [Range(1.0f, 4.0f)]
        [SerializeField] private float signDisplayDistance = 2.0f;

        [Tooltip("Size of the sign (meters)")]
        [Range(0.3f, 1.5f)]
        [SerializeField] private float signSize = 0.8f;

        [Tooltip("Background color of the sign")]
        [SerializeField] private Color signBackgroundColor = new Color(1f, 0.85f, 0.2f, 0.95f); // Yellow

        [Tooltip("Arrow/text color on the sign")]
        [SerializeField] private Color signForegroundColor = Color.black;

        [Header("Hovering Arrow Settings")]
        [Tooltip("Enable the hovering arrow above platform")]
        [SerializeField] private bool enableHoveringArrow = true;

        [Tooltip("Height above platform (meters)")]
        [Range(1.5f, 4.0f)]
        [SerializeField] private float hoveringArrowHeight = 2.5f;

        [Tooltip("Size of the hovering arrow")]
        [Range(0.3f, 1.5f)]
        [SerializeField] private float hoveringArrowSize = 0.6f;

        [Tooltip("Color of the hovering arrow")]
        [SerializeField] private Color hoveringArrowColor = new Color(0.2f, 0.9f, 0.3f, 1f); // Green

        [Tooltip("Bobbing amplitude (meters)")]
        [Range(0.0f, 0.3f)]
        [SerializeField] private float bobAmplitude = 0.15f;

        [Tooltip("Bobbing frequency (cycles per second)")]
        [Range(0.2f, 2.0f)]
        [SerializeField] private float bobFrequency = 0.8f;

        [Header("Floor Arrow Settings")]
        [Tooltip("Enable the floor direction arrow on platform")]
        [SerializeField] private bool enableFloorArrow = true;

        [Tooltip("Length of the floor arrow (meters)")]
        [Range(0.4f, 1.5f)]
        [SerializeField] private float floorArrowLength = 0.8f;

        [Tooltip("Width of the floor arrow (meters)")]
        [Range(0.2f, 0.8f)]
        [SerializeField] private float floorArrowWidth = 0.4f;

        [Tooltip("Color of the floor arrow")]
        [SerializeField] private Color floorArrowColor = new Color(0f, 0.9f, 0.9f, 1f); // Cyan

        [Tooltip("Forward offset from platform center (meters) - places arrow in front of platform")]
        [Range(0.0f, 2.0f)]
        [SerializeField] private float floorArrowForwardOffset = 0.8f;

        [Header("Animation Settings")]
        [Tooltip("Fade in duration (seconds)")]
        [Range(0.1f, 1.0f)]
        [SerializeField] private float fadeInDuration = 0.3f;

        [Tooltip("Fade out duration for Turn Around sign (seconds)")]
        [Range(0.1f, 1.0f)]
        [SerializeField] private float fadeOutDuration = 0.3f;

        [Header("Turn Around Sign Visibility")]
        [Tooltip("Dot product threshold for hiding sign (0.3 ≈ 72° cone, higher = narrower)")]
        [Range(0.0f, 0.7f)]
        [SerializeField] private float platformVisibilityThreshold = 0.3f;

        [Header("Debug")]
        [SerializeField] private bool showDebugLogs = false;

        // Singleton
        public static ITINavigationGuidance Instance { get; private set; }

        // References
        private Camera playerCamera;
        private TrialManager trialManager;
        private StartingPlatform startingPlatform;

        // Visual elements
        private GameObject turnAroundCanvas;
        private CanvasGroup turnAroundCanvasGroup;
        private GameObject hoveringArrow;
        private GameObject floorArrow;


        // State
        private bool isGuidanceActive = false;
        private Coroutine fadeCoroutine;
        private bool turnAroundSignHidden = false;  // Hidden due to player facing platform

        void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
            }
            else
            {
                Destroy(gameObject);
                return;
            }
        }

        void Start()
        {
            FindReferences();
            CreateVisualElements();

            if (trialManager != null)
            {
                trialManager.OnPhaseChanged += OnPhaseChanged;
                if (showDebugLogs) Debug.Log("[ITINavigationGuidance] Subscribed to TrialManager.OnPhaseChanged");
            }
            else
            {
                Debug.LogWarning("[ITINavigationGuidance] TrialManager not found - guidance will not function");
            }

            HideAllElements();

            if (showDebugLogs) Debug.Log("[ITINavigationGuidance] Initialized");
        }

        void OnDestroy()
        {
            if (trialManager != null)
            {
                trialManager.OnPhaseChanged -= OnPhaseChanged;
            }
        }

        void FindReferences()
        {
            playerCamera = Camera.main;
            if (playerCamera == null)
            {
                playerCamera = FindFirstObjectByType<Camera>();
            }

            if (playerCamera == null)
            {
                Debug.LogError("[ITINavigationGuidance] No camera found!");
            }

            trialManager = FindFirstObjectByType<TrialManager>();
            if (trialManager == null)
            {
                Debug.LogError("[ITINavigationGuidance] TrialManager not found!");
            }

            startingPlatform = FindFirstObjectByType<StartingPlatform>();
            if (startingPlatform == null)
            {
                Debug.LogError("[ITINavigationGuidance] StartingPlatform not found!");
            }
        }

        void CreateVisualElements()
        {
            if (enableTurnAroundSign)
            {
                CreateTurnAroundSign();
            }

            if (enableHoveringArrow)
            {
                CreateHoveringArrow();
            }

            if (enableFloorArrow)
            {
                CreateFloorArrow();
            }
        }

        #region Turn Around Sign

        void CreateTurnAroundSign()
        {
            turnAroundCanvas = new GameObject("TurnAroundCanvas");
            turnAroundCanvas.transform.SetParent(transform);

            Canvas canvas = turnAroundCanvas.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 100;

            RectTransform canvasRect = turnAroundCanvas.GetComponent<RectTransform>();
            canvasRect.sizeDelta = new Vector2(signSize * 300f, signSize * 50f);
            canvasRect.localScale = Vector3.one * 0.01f;

            turnAroundCanvasGroup = turnAroundCanvas.AddComponent<CanvasGroup>();
            turnAroundCanvasGroup.alpha = 0f;

            GameObject bgPanel = new GameObject("Background");
            bgPanel.transform.SetParent(turnAroundCanvas.transform);

            Image bgImage = bgPanel.AddComponent<Image>();
            bgImage.color = signBackgroundColor;

            RectTransform bgRect = bgPanel.GetComponent<RectTransform>();
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.offsetMin = Vector2.zero;
            bgRect.offsetMax = Vector2.zero;
            bgRect.localPosition = Vector3.zero;
            bgRect.localScale = Vector3.one;

            GameObject textObj = new GameObject("TurnAroundText");
            textObj.transform.SetParent(bgPanel.transform);

            TextMeshProUGUI textMesh = textObj.AddComponent<TextMeshProUGUI>();
            textMesh.text = "TURN AROUND";
            textMesh.fontSize = 28;
            textMesh.fontStyle = FontStyles.Bold;
            textMesh.color = signForegroundColor;
            textMesh.alignment = TextAlignmentOptions.Center;
            textMesh.verticalAlignment = VerticalAlignmentOptions.Middle;
            textMesh.enableWordWrapping = false;

            RectTransform textRect = textObj.GetComponent<RectTransform>();
            textRect.anchorMin = new Vector2(0.05f, 0.1f);
            textRect.anchorMax = new Vector2(0.95f, 0.9f);
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
            textRect.localPosition = Vector3.zero;
            textRect.localScale = Vector3.one;

            turnAroundCanvas.SetActive(false);

            if (showDebugLogs) Debug.Log("[ITINavigationGuidance] Turn Around sign created");
        }

        #endregion

        #region Hovering Arrow

        void CreateHoveringArrow()
        {
            hoveringArrow = new GameObject("HoveringArrow");
            hoveringArrow.transform.SetParent(transform);

            float arrowScale = hoveringArrowSize;

            // Capsule head pointing down
            GameObject head = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            head.name = "ArrowHead";
            head.transform.SetParent(hoveringArrow.transform);
            head.transform.localPosition = new Vector3(0, -0.1f * arrowScale, 0);
            head.transform.localScale = new Vector3(0.5f * arrowScale, 0.3f * arrowScale, 0.5f * arrowScale);

            Collider headCollider = head.GetComponent<Collider>();
            if (headCollider != null) Destroy(headCollider);

            VRMaterialProvider.TintPrimitiveMaterial(head, hoveringArrowColor);

            GameObject shaft = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            shaft.name = "ArrowShaft";
            shaft.transform.SetParent(hoveringArrow.transform);
            shaft.transform.localPosition = new Vector3(0, 0.4f * arrowScale, 0);
            shaft.transform.localScale = new Vector3(0.15f * arrowScale, 0.4f * arrowScale, 0.15f * arrowScale);

            Collider shaftCollider = shaft.GetComponent<Collider>();
            if (shaftCollider != null) Destroy(shaftCollider);

            VRMaterialProvider.TintPrimitiveMaterial(shaft, hoveringArrowColor);

            hoveringArrow.SetActive(false);

            if (showDebugLogs) Debug.Log("[ITINavigationGuidance] Hovering arrow created");
        }

        #endregion

        #region Floor Arrow

        void CreateFloorArrow()
        {
            floorArrow = new GameObject("FloorDirectionArrow");
            floorArrow.transform.SetParent(transform);

            float bodyLength = floorArrowLength * 0.6f;
            float bodyWidth = floorArrowWidth * 0.4f;
            float thickness = 0.03f;

            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "ArrowBody";
            body.transform.SetParent(floorArrow.transform);
            body.transform.localPosition = new Vector3(0, thickness / 2, -bodyLength * 0.3f);
            body.transform.localScale = new Vector3(bodyWidth, thickness, bodyLength);

            Collider bodyCollider = body.GetComponent<Collider>();
            if (bodyCollider != null) Destroy(bodyCollider);

            VRMaterialProvider.TintPrimitiveMaterial(body, floorArrowColor);

            // Chevron head from two angled cubes
            float headLength = floorArrowLength * 0.35f;
            float headWidth = floorArrowWidth * 0.35f;
            float headZ = bodyLength * 0.3f;

            GameObject leftWing = GameObject.CreatePrimitive(PrimitiveType.Cube);
            leftWing.name = "ArrowHeadLeft";
            leftWing.transform.SetParent(floorArrow.transform);
            leftWing.transform.localPosition = new Vector3(-headWidth * 0.5f, thickness / 2, headZ - headLength * 0.3f);
            leftWing.transform.localScale = new Vector3(bodyWidth * 0.8f, thickness, headLength);
            leftWing.transform.localRotation = Quaternion.Euler(0, 35, 0);

            Collider leftCollider = leftWing.GetComponent<Collider>();
            if (leftCollider != null) Destroy(leftCollider);

            VRMaterialProvider.TintPrimitiveMaterial(leftWing, floorArrowColor);

            GameObject rightWing = GameObject.CreatePrimitive(PrimitiveType.Cube);
            rightWing.name = "ArrowHeadRight";
            rightWing.transform.SetParent(floorArrow.transform);
            rightWing.transform.localPosition = new Vector3(headWidth * 0.5f, thickness / 2, headZ - headLength * 0.3f);
            rightWing.transform.localScale = new Vector3(bodyWidth * 0.8f, thickness, headLength);
            rightWing.transform.localRotation = Quaternion.Euler(0, -35, 0);

            Collider rightCollider = rightWing.GetComponent<Collider>();
            if (rightCollider != null) Destroy(rightCollider);

            VRMaterialProvider.TintPrimitiveMaterial(rightWing, floorArrowColor);

            floorArrow.SetActive(false);

            if (showDebugLogs) Debug.Log("[ITINavigationGuidance] Floor direction arrow created");
        }

        #endregion

        #region Phase Management

        void OnPhaseChanged(TrialManager.TrialPhase newPhase)
        {
            if (showDebugLogs) Debug.Log($"[ITINavigationGuidance] Phase changed to: {newPhase}");

            switch (newPhase)
            {
                case TrialManager.TrialPhase.InterTrialInterval:
                    ShowGuidance();
                    break;
                default:
                    HideGuidance();
                    break;
            }
        }

        public void ShowGuidance()
        {
            if (isGuidanceActive) return;
            isGuidanceActive = true;

            if (showDebugLogs) Debug.Log("[ITINavigationGuidance] Showing guidance");

            turnAroundSignHidden = false;

            PositionGuidanceElements();

            if (enableTurnAroundSign && turnAroundCanvas != null)
            {
                PositionTurnAroundSign();
                turnAroundCanvas.SetActive(true);

                if (fadeCoroutine != null) StopCoroutine(fadeCoroutine);
                fadeCoroutine = StartCoroutine(FadeIn(turnAroundCanvasGroup, fadeInDuration));
            }

            if (enableHoveringArrow && hoveringArrow != null)
            {
                hoveringArrow.SetActive(true);
            }

            if (enableFloorArrow && floorArrow != null)
            {
                floorArrow.SetActive(true);
            }
        }

        public void HideGuidance()
        {
            if (!isGuidanceActive) return;
            isGuidanceActive = false;

            if (showDebugLogs) Debug.Log("[ITINavigationGuidance] Hiding guidance");

            HideAllElements();
        }

        void HideAllElements()
        {
            if (turnAroundCanvas != null)
            {
                turnAroundCanvas.SetActive(false);
                if (turnAroundCanvasGroup != null) turnAroundCanvasGroup.alpha = 0f;
            }

            if (hoveringArrow != null)
            {
                hoveringArrow.SetActive(false);
            }

            if (floorArrow != null)
            {
                floorArrow.SetActive(false);
            }
        }

        #endregion

        #region Positioning and Animation

        void LateUpdate()
        {
            if (!isGuidanceActive) return;

            if (enableTurnAroundSign && turnAroundCanvas != null)
            {
                UpdateTurnAroundSignVisibility();
            }

            if (enableTurnAroundSign && turnAroundCanvas != null && turnAroundCanvas.activeInHierarchy && !turnAroundSignHidden)
            {
                PositionTurnAroundSign();
            }

            if (enableHoveringArrow && hoveringArrow != null && hoveringArrow.activeInHierarchy)
            {
                AnimateHoveringArrow();
            }
        }

        /// <summary>Hide the Turn Around sign while the player is already facing the platform.</summary>
        void UpdateTurnAroundSignVisibility()
        {
            if (playerCamera == null || startingPlatform == null) return;

            Vector3 platformPos = startingPlatform.GetPlatformPosition();
            Vector3 playerPos = playerCamera.transform.position;
            Vector3 dirToPlatform = (platformPos - playerPos).normalized;

            // Flatten to XZ plane so heading comparison ignores pitch
            dirToPlatform.y = 0;
            dirToPlatform.Normalize();

            Vector3 playerForward = playerCamera.transform.forward;
            playerForward.y = 0;
            playerForward.Normalize();

            float dotProduct = Vector3.Dot(playerForward, dirToPlatform);

            bool shouldHide = dotProduct > platformVisibilityThreshold;

            if (shouldHide && !turnAroundSignHidden)
            {
                turnAroundSignHidden = true;
                if (fadeCoroutine != null) StopCoroutine(fadeCoroutine);
                fadeCoroutine = StartCoroutine(FadeOut(turnAroundCanvasGroup, fadeOutDuration));

                if (showDebugLogs) Debug.Log($"[ITINavigationGuidance] Player facing platform (dot={dotProduct:F2}), hiding Turn Around sign");
            }
            else if (!shouldHide && turnAroundSignHidden)
            {
                turnAroundSignHidden = false;
                turnAroundCanvas.SetActive(true);
                if (fadeCoroutine != null) StopCoroutine(fadeCoroutine);
                fadeCoroutine = StartCoroutine(FadeIn(turnAroundCanvasGroup, fadeInDuration));

                if (showDebugLogs) Debug.Log($"[ITINavigationGuidance] Player facing away (dot={dotProduct:F2}), showing Turn Around sign");
            }
        }

        void PositionTurnAroundSign()
        {
            if (playerCamera == null) return;

            Vector3 targetPosition = playerCamera.transform.position +
                                    playerCamera.transform.forward * signDisplayDistance;
            turnAroundCanvas.transform.position = targetPosition;

            Vector3 lookDirection = turnAroundCanvas.transform.position - playerCamera.transform.position;
            if (lookDirection.sqrMagnitude > 0.001f)
            {
                turnAroundCanvas.transform.rotation = Quaternion.LookRotation(lookDirection);
            }
        }

        void PositionGuidanceElements()
        {
            if (startingPlatform == null) return;

            Vector3 platformPos = startingPlatform.GetPlatformPosition();
            Vector3 forwardDir = startingPlatform.GetInitialForwardDirection();

            if (hoveringArrow != null)
            {
                hoveringArrow.transform.position = platformPos + Vector3.up * hoveringArrowHeight;
            }

            if (floorArrow != null)
            {
                Vector3 arrowPosition = platformPos + forwardDir * floorArrowForwardOffset + Vector3.up * 0.01f;
                floorArrow.transform.position = arrowPosition;
                floorArrow.transform.rotation = Quaternion.LookRotation(forwardDir, Vector3.up);
            }
        }

        void AnimateHoveringArrow()
        {
            if (startingPlatform == null || hoveringArrow == null) return;

            Vector3 platformPos = startingPlatform.GetPlatformPosition();
            float bobOffset = Mathf.Sin(Time.time * bobFrequency * Mathf.PI * 2) * bobAmplitude;

            hoveringArrow.transform.position = platformPos + Vector3.up * (hoveringArrowHeight + bobOffset);
        }

        IEnumerator FadeIn(CanvasGroup canvasGroup, float duration)
        {
            if (canvasGroup == null) yield break;

            float elapsed = 0f;
            float startAlpha = canvasGroup.alpha;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                canvasGroup.alpha = Mathf.Lerp(startAlpha, 1f, elapsed / duration);
                yield return null;
            }

            canvasGroup.alpha = 1f;
        }

        IEnumerator FadeOut(CanvasGroup canvasGroup, float duration)
        {
            if (canvasGroup == null) yield break;

            float elapsed = 0f;
            float startAlpha = canvasGroup.alpha;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                canvasGroup.alpha = Mathf.Lerp(startAlpha, 0f, elapsed / duration);
                yield return null;
            }

            canvasGroup.alpha = 0f;

            if (turnAroundCanvas != null)
            {
                turnAroundCanvas.SetActive(false);
            }
        }

        #endregion

        #region Public API

        /// <summary>True while ITI guidance visuals are active.</summary>
        public bool IsGuidanceActive()
        {
            return isGuidanceActive;
        }

        /// <summary>Force-show guidance (testing hook).</summary>
        public void ForceShowGuidance()
        {
            ShowGuidance();
        }

        /// <summary>Force-hide guidance (testing hook).</summary>
        public void ForceHideGuidance()
        {
            HideGuidance();
        }

        #endregion
    }
}
