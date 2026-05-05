// SPDX-License-Identifier: MIT
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace EyeTracking.Metrics
{
    /// <summary>
    /// Plug-and-play screen-space cognitive-load HUD. Drop on any
    /// GameObject in any scene, press Play, and the gauge appears in the
    /// chosen corner. Builds its own ScreenSpaceOverlay canvas + filled
    /// Image + label internally; no UI authoring required. The component
    /// auto-binds to <see cref="RIPAMonitor.Instance"/> via
    /// <see cref="RIPAGauge"/>; the RIPAMonitor itself is auto-spawned by
    /// <see cref="RIPAMonitorBootstrap"/>, so a researcher only needs this
    /// one component to get a working on-screen indicator.
    /// </summary>
    public sealed class RIPAOverlay : MonoBehaviour
    {
        public enum Corner { TopLeft, TopRight, BottomLeft, BottomRight }

        [Header("Placement")]
        [Tooltip("Which screen corner the gauge anchors to.")]
        [SerializeField] private Corner corner = Corner.TopLeft;
        [Tooltip("Pixel offset from the chosen corner, in screen pixels.")]
        [SerializeField] private Vector2 margin = new Vector2(24f, 24f);
        [Tooltip("Gauge bar size in screen pixels. Width × height.")]
        [SerializeField] private Vector2 size = new Vector2(220f, 32f);
        [Tooltip("Sort order of the overlay canvas. Raise if a fullscreen UI is hiding the gauge.")]
        [SerializeField] private int sortingOrder = 9999;

        [Header("Display")]
        [Tooltip("RIPA value mapped to a full bar. Paper clips to 1.5.")]
        [SerializeField] private float displayMax = 1.5f;
        [Tooltip("Text format. {0} is the load value. Set blank to hide the label.")]
        [SerializeField] private string labelFormat = "Load {0:F2}";
        [Tooltip("Use the smoothed (true) or raw (false) RIPA value.")]
        [SerializeField] private bool useSmoothedValue = true;

        [Header("Visibility")]
        [Tooltip("Hide the overlay automatically while the RIPA monitor is warming up (no IsValid output yet).")]
        [SerializeField] private bool hideUntilValid = true;

        private Canvas overlayCanvas;
        private GameObject root;
        private RIPAGauge gauge;

        private void OnEnable()
        {
            if (root == null) Build();
        }

        private void OnDisable()
        {
            if (overlayCanvas != null) overlayCanvas.gameObject.SetActive(false);
        }

        private void OnDestroy()
        {
            if (overlayCanvas != null) Destroy(overlayCanvas.gameObject);
        }

        private void Update()
        {
            if (!hideUntilValid || root == null) return;
            var m = RIPAMonitor.Instance;
            bool show = m != null && m.IsValid && m.Enabled;
            if (root.activeSelf != show) root.SetActive(show);
        }

        private void Build()
        {
            // Self-contained overlay canvas — kept off the application's
            // UI tree so it does not interfere with researcher-authored
            // canvases.
            var canvasGo = new GameObject("[RIPAOverlay]");
            canvasGo.transform.SetParent(transform, false);
            overlayCanvas = canvasGo.AddComponent<Canvas>();
            overlayCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            overlayCanvas.sortingOrder = sortingOrder;
            canvasGo.AddComponent<CanvasScaler>();

            root = new GameObject("Root");
            root.transform.SetParent(canvasGo.transform, false);
            var rootRect = root.AddComponent<RectTransform>();
            ApplyCornerAnchor(rootRect);

            // Background plate.
            var bgGo = new GameObject("Background");
            bgGo.transform.SetParent(root.transform, false);
            var bgRect = bgGo.AddComponent<RectTransform>();
            bgRect.anchorMin = bgRect.anchorMax = new Vector2(0.5f, 0.5f);
            bgRect.sizeDelta = size + new Vector2(16f, 16f);
            bgRect.anchoredPosition = Vector2.zero;
            var bg = bgGo.AddComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.55f);
            bg.raycastTarget = false;

            // Filled bar.
            var barGo = new GameObject("Bar");
            barGo.transform.SetParent(root.transform, false);
            var barRect = barGo.AddComponent<RectTransform>();
            barRect.anchorMin = barRect.anchorMax = new Vector2(0.5f, 0.5f);
            barRect.sizeDelta = size;
            barRect.anchoredPosition = Vector2.zero;
            var fill = barGo.AddComponent<Image>();
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillOrigin = (int)Image.OriginHorizontal.Left;
            fill.color = new Color(0.30f, 0.78f, 0.55f, 1f);
            fill.raycastTarget = false;

            // Optional label centered over the bar.
            TextMeshProUGUI label = null;
            if (!string.IsNullOrEmpty(labelFormat))
            {
                var labelGo = new GameObject("Label");
                labelGo.transform.SetParent(root.transform, false);
                var labelRect = labelGo.AddComponent<RectTransform>();
                labelRect.anchorMin = labelRect.anchorMax = new Vector2(0.5f, 0.5f);
                labelRect.sizeDelta = size;
                labelRect.anchoredPosition = Vector2.zero;
                label = labelGo.AddComponent<TextMeshProUGUI>();
                label.alignment = TextAlignmentOptions.Center;
                label.color = Color.white;
                label.fontSize = Mathf.Clamp(size.y * 0.55f, 10f, 22f);
                label.raycastTarget = false;
            }

            // Defer OnEnable until bindings are set: AddComponent on an
            // active GameObject would fire OnEnable immediately with the
            // gauge's default fields.
            barGo.SetActive(false);
            gauge = barGo.AddComponent<RIPAGauge>();
            gauge.Bind(fill, label);
            gauge.DisplayMax = displayMax;
            gauge.LabelFormat = labelFormat;
            gauge.UseSmoothedValue = useSmoothedValue;
            barGo.SetActive(true);
        }

        private void ApplyCornerAnchor(RectTransform rect)
        {
            Vector2 anchor;
            Vector2 pivot;
            Vector2 anchored;
            switch (corner)
            {
                case Corner.TopRight:
                    anchor = pivot = new Vector2(1f, 1f);
                    anchored = new Vector2(-margin.x, -margin.y);
                    break;
                case Corner.BottomLeft:
                    anchor = pivot = new Vector2(0f, 0f);
                    anchored = new Vector2(margin.x, margin.y);
                    break;
                case Corner.BottomRight:
                    anchor = pivot = new Vector2(1f, 0f);
                    anchored = new Vector2(-margin.x, margin.y);
                    break;
                case Corner.TopLeft:
                default:
                    anchor = pivot = new Vector2(0f, 1f);
                    anchored = new Vector2(margin.x, -margin.y);
                    break;
            }
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.sizeDelta = size + new Vector2(16f, 16f);
            rect.anchoredPosition = anchored;
        }
    }
}
