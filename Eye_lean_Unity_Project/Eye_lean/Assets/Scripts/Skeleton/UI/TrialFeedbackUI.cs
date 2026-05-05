// SPDX-License-Identifier: MIT
using UnityEngine;
using UnityEngine.UI;
using System.Collections;

/// <summary>
/// World-space VR feedback overlay: red X on failure, green checkmark on success.
/// </summary>

namespace EyeLean.Skeleton
{
    public class TrialFeedbackUI : MonoBehaviour
    {
        [Header("Feedback Settings")]
        [Tooltip("Enable visual feedback for trial outcomes")]
        [SerializeField] private bool enableFeedback = true;

        [Tooltip("Show red X on collision (trial restart)")]
        [SerializeField] private bool showCollisionFeedback = true;

        [Tooltip("Show green checkmark on successful exit")]
        [SerializeField] private bool showSuccessFeedback = true;

        [Header("Display Settings")]
        [Tooltip("Distance in front of player to show feedback (meters)")]
        [Range(0.5f, 3.0f)]
        [SerializeField] private float displayDistance = 1.5f;

        [Tooltip("Size of the feedback symbol (meters)")]
        [Range(0.2f, 2.0f)]
        [SerializeField] private float symbolSize = 0.5f;

        [Tooltip("How long to display feedback (seconds)")]
        [Range(0.5f, 5.0f)]
        [SerializeField] private float displayDuration = 1.5f;

        [Tooltip("Fade in/out duration (seconds)")]
        [Range(0.1f, 1.0f)]
        [SerializeField] private float fadeDuration = 0.3f;

        [Header("Colors")]
        [SerializeField] private Color failureColor = new Color(1f, 0.2f, 0.2f, 1f); // Red
        [SerializeField] private Color successColor = new Color(0.2f, 1f, 0.2f, 1f); // Green

        [Header("Debug")]
        [SerializeField] private bool showDebugInfo = false;

        // References
        private Camera playerCamera;
        private GameObject feedbackCanvas;
        private RectTransform canvasRect;
        private Image symbolImage;
        private CanvasGroup canvasGroup;

        // State
        private bool isDisplayingFeedback = false;
        private Coroutine currentFeedbackCoroutine;

        // Symbol textures (generated at runtime)
        private Sprite xSprite;
        private Sprite checkSprite;

        // Singleton for easy access
        public static TrialFeedbackUI Instance { get; private set; }

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
            FindPlayerCamera();
            CreateFeedbackCanvas();
            GenerateSymbolSprites();

            if (canvasGroup != null)
            {
                canvasGroup.alpha = 0f;
            }

            if (showDebugInfo) Debug.Log($"[TrialFeedbackUI] Initialized - Feedback enabled: {enableFeedback}, Collision: {showCollisionFeedback}, Success: {showSuccessFeedback}");
        }

        void FindPlayerCamera()
        {
            playerCamera = Camera.main;
            if (playerCamera == null)
            {
                playerCamera = FindFirstObjectByType<Camera>();
            }

            if (playerCamera == null)
            {
                Debug.LogError("[TrialFeedbackUI] No camera found - feedback display will not work!");
            }
        }

        void CreateFeedbackCanvas()
        {
            feedbackCanvas = new GameObject("TrialFeedbackCanvas");
            feedbackCanvas.transform.SetParent(transform);

            Canvas canvas = feedbackCanvas.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 100;

            canvasRect = feedbackCanvas.GetComponent<RectTransform>();
            canvasRect.sizeDelta = new Vector2(symbolSize * 100f, symbolSize * 100f);
            canvasRect.localScale = Vector3.one * 0.01f; // 1 unit = 1 meter in world space

            canvasGroup = feedbackCanvas.AddComponent<CanvasGroup>();
            canvasGroup.alpha = 0f;

            GameObject symbolObj = new GameObject("SymbolImage");
            symbolObj.transform.SetParent(feedbackCanvas.transform);

            symbolImage = symbolObj.AddComponent<Image>();
            RectTransform symbolRect = symbolObj.GetComponent<RectTransform>();
            symbolRect.anchorMin = Vector2.zero;
            symbolRect.anchorMax = Vector2.one;
            symbolRect.offsetMin = Vector2.zero;
            symbolRect.offsetMax = Vector2.zero;
            symbolRect.localPosition = Vector3.zero;
            symbolRect.localScale = Vector3.one;

            if (showDebugInfo) Debug.Log("[TrialFeedbackUI] Feedback canvas created");
        }

        void GenerateSymbolSprites()
        {
            xSprite = CreateXSprite(256);
            checkSprite = CreateCheckSprite(256);

            if (showDebugInfo) Debug.Log("[TrialFeedbackUI] Symbol sprites generated");
        }

        Sprite CreateXSprite(int size)
        {
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            Color[] pixels = new Color[size * size];

            // Fill with transparent
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = Color.clear;
            }

            // Draw X
            int lineWidth = size / 10;
            int margin = size / 8;

            for (int i = 0; i < size; i++)
            {
                // Diagonal from top-left to bottom-right
                for (int w = -lineWidth / 2; w <= lineWidth / 2; w++)
                {
                    int x1 = Mathf.Clamp(margin + i + w, 0, size - 1);
                    int y1 = Mathf.Clamp(margin + i, 0, size - 1);
                    if (x1 >= margin && x1 < size - margin)
                        pixels[y1 * size + x1] = Color.white;
                }

                // Diagonal from top-right to bottom-left
                for (int w = -lineWidth / 2; w <= lineWidth / 2; w++)
                {
                    int x2 = Mathf.Clamp((size - margin - 1) - i + w, 0, size - 1);
                    int y2 = Mathf.Clamp(margin + i, 0, size - 1);
                    if (x2 >= margin && x2 < size - margin)
                        pixels[y2 * size + x2] = Color.white;
                }
            }

            texture.SetPixels(pixels);
            texture.Apply();

            return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        }

        Sprite CreateCheckSprite(int size)
        {
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            Color[] pixels = new Color[size * size];

            // Fill with transparent
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = Color.clear;
            }

            // Draw checkmark
            int lineWidth = size / 10;
            int margin = size / 8;

            // Short leg (bottom-left to middle-bottom)
            int shortLegLength = size / 3;
            for (int i = 0; i < shortLegLength; i++)
            {
                float t = (float)i / shortLegLength;
                int x = margin + (int)(t * shortLegLength);
                int y = size / 2 + (int)(t * shortLegLength / 2);

                for (int w = -lineWidth / 2; w <= lineWidth / 2; w++)
                {
                    for (int h = -lineWidth / 2; h <= lineWidth / 2; h++)
                    {
                        int px = Mathf.Clamp(x + w, 0, size - 1);
                        int py = Mathf.Clamp(y + h, 0, size - 1);
                        pixels[py * size + px] = Color.white;
                    }
                }
            }

            // Long leg (middle-bottom to top-right)
            int longLegLength = size - margin * 2 - shortLegLength;
            for (int i = 0; i < longLegLength; i++)
            {
                float t = (float)i / longLegLength;
                int x = margin + shortLegLength + (int)(t * longLegLength);
                int y = size / 2 + shortLegLength / 2 - (int)(t * (size / 2 + shortLegLength / 2 - margin));

                for (int w = -lineWidth / 2; w <= lineWidth / 2; w++)
                {
                    for (int h = -lineWidth / 2; h <= lineWidth / 2; h++)
                    {
                        int px = Mathf.Clamp(x + w, 0, size - 1);
                        int py = Mathf.Clamp(y + h, 0, size - 1);
                        pixels[py * size + px] = Color.white;
                    }
                }
            }

            texture.SetPixels(pixels);
            texture.Apply();

            return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        }

        void LateUpdate()
        {
            if (isDisplayingFeedback && feedbackCanvas != null && playerCamera != null)
            {
                PositionCanvasInFrontOfPlayer();
            }
        }

        void PositionCanvasInFrontOfPlayer()
        {
            Vector3 targetPosition = playerCamera.transform.position + playerCamera.transform.forward * displayDistance;
            feedbackCanvas.transform.position = targetPosition;

            feedbackCanvas.transform.rotation = Quaternion.LookRotation(
                feedbackCanvas.transform.position - playerCamera.transform.position
            );

            canvasRect.localScale = Vector3.one * (symbolSize / 100f);
        }

        #region Public API

        /// <summary>Show failure feedback (red X) on collision.</summary>
        public void ShowFailureFeedback()
        {
            if (!enableFeedback || !showCollisionFeedback)
            {
                if (showDebugInfo)
                    Debug.Log("[TrialFeedbackUI] Failure feedback skipped (disabled)");
                return;
            }

            ShowFeedback(xSprite, failureColor, "FAILURE");
        }

        /// <summary>Show success feedback (green checkmark) on successful exit.</summary>
        public void ShowSuccessFeedback()
        {
            if (!enableFeedback || !showSuccessFeedback)
            {
                if (showDebugInfo)
                    Debug.Log("[TrialFeedbackUI] Success feedback skipped (disabled)");
                return;
            }

            ShowFeedback(checkSprite, successColor, "SUCCESS");
        }

        /// <summary>Enable or disable all feedback.</summary>
        public void SetFeedbackEnabled(bool enabled)
        {
            enableFeedback = enabled;
            if (showDebugInfo) Debug.Log($"[TrialFeedbackUI] Feedback {(enabled ? "ENABLED" : "DISABLED")}");
        }

        /// <summary>Enable or disable collision (failure) feedback only.</summary>
        public void SetCollisionFeedbackEnabled(bool enabled)
        {
            showCollisionFeedback = enabled;
            if (showDebugInfo) Debug.Log($"[TrialFeedbackUI] Collision feedback {(enabled ? "ENABLED" : "DISABLED")}");
        }

        /// <summary>Enable or disable success feedback only.</summary>
        public void SetSuccessFeedbackEnabled(bool enabled)
        {
            showSuccessFeedback = enabled;
            if (showDebugInfo) Debug.Log($"[TrialFeedbackUI] Success feedback {(enabled ? "ENABLED" : "DISABLED")}");
        }

        /// <summary>True while feedback is being displayed.</summary>
        public bool IsDisplayingFeedback()
        {
            return isDisplayingFeedback;
        }

        /// <summary>Force-hide any active feedback.</summary>
        public void HideFeedback()
        {
            if (currentFeedbackCoroutine != null)
            {
                StopCoroutine(currentFeedbackCoroutine);
                currentFeedbackCoroutine = null;
            }

            if (canvasGroup != null)
            {
                canvasGroup.alpha = 0f;
            }

            isDisplayingFeedback = false;
        }

        #endregion

        void ShowFeedback(Sprite sprite, Color color, string type)
        {
            if (symbolImage == null || canvasGroup == null)
            {
                Debug.LogError("[TrialFeedbackUI] Cannot show feedback - UI not initialized");
                return;
            }

            if (currentFeedbackCoroutine != null)
            {
                StopCoroutine(currentFeedbackCoroutine);
            }

            symbolImage.sprite = sprite;
            symbolImage.color = color;

            PositionCanvasInFrontOfPlayer();

            currentFeedbackCoroutine = StartCoroutine(DisplayFeedbackCoroutine(type));

            if (showDebugInfo) Debug.Log($"[TrialFeedbackUI] Showing {type} feedback");
        }

        IEnumerator DisplayFeedbackCoroutine(string type)
        {
            isDisplayingFeedback = true;

            float elapsed = 0f;
            while (elapsed < fadeDuration)
            {
                elapsed += Time.deltaTime;
                canvasGroup.alpha = Mathf.Lerp(0f, 1f, elapsed / fadeDuration);
                yield return null;
            }
            canvasGroup.alpha = 1f;

            yield return new WaitForSeconds(displayDuration - fadeDuration * 2f);

            elapsed = 0f;
            while (elapsed < fadeDuration)
            {
                elapsed += Time.deltaTime;
                canvasGroup.alpha = Mathf.Lerp(1f, 0f, elapsed / fadeDuration);
                yield return null;
            }
            canvasGroup.alpha = 0f;

            isDisplayingFeedback = false;
            currentFeedbackCoroutine = null;

            if (showDebugInfo)
                Debug.Log($"[TrialFeedbackUI] {type} feedback display complete");
        }

        void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }

            if (xSprite != null && xSprite.texture != null)
            {
                Destroy(xSprite.texture);
            }
            if (checkSprite != null && checkSprite.texture != null)
            {
                Destroy(checkSprite.texture);
            }
        }

        #region Debug

        void OnGUI()
        {
            if (!showDebugInfo || !Application.isPlaying) return;

            GUILayout.BeginArea(new Rect(10, 450, 300, 100));
            GUILayout.Label("<b>TRIAL FEEDBACK DEBUG</b>");
            GUILayout.Label($"Enabled: {enableFeedback}");
            GUILayout.Label($"Collision FB: {showCollisionFeedback} | Success FB: {showSuccessFeedback}");
            GUILayout.Label($"Currently Displaying: {isDisplayingFeedback}");
            GUILayout.EndArea();
        }

        #endregion
    }
}
