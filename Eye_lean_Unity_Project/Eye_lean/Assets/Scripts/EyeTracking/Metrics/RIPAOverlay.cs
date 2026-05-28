// SPDX-License-Identifier: MIT
using UnityEngine;
using UnityEngine.UI;

namespace EyeTracking.Metrics
{
    /// <summary>
    /// Screen-space corner placement for the canonical <see cref="RIPAGauge"/>.
    /// Builds a self-contained ScreenSpaceOverlay canvas in the chosen corner
    /// and parents a freshly-built gauge widget into it.
    ///
    /// This component contributes only placement — all gauge visuals and
    /// data binding live in <see cref="RIPAGauge"/>. World-space variants
    /// (e.g., inside an experiment panel) call the same factory directly.
    /// </summary>
    public sealed class RIPAOverlay : MonoBehaviour
    {
        public enum Corner { TopLeft, TopRight, BottomLeft, BottomRight }

        [Header("Placement")]
        [Tooltip("Which screen corner the strip anchors to.")]
        [SerializeField] private Corner corner = Corner.TopLeft;
        [Tooltip("Pixel offset from the chosen corner.")]
        [SerializeField] private Vector2 margin = new Vector2(24f, 24f);
        [Tooltip("Overall strip size in pixels. Vertical-strip proportions (narrow + tall).")]
        [SerializeField] private Vector2 size = new Vector2(90f, 360f);
        [Tooltip("Sort order of the overlay canvas. Raise if a fullscreen UI is hiding the strip.")]
        [SerializeField] private int sortingOrder = 9999;

        [Header("Gauge")]
        [Tooltip("Load value mapped to a full bar. Detector outputs are normalized to [0, 1.5].")]
        [SerializeField] private float displayMax = 1.5f;
        [Tooltip("Label format. {0} is the load value.")]
        [SerializeField] private string labelFormat = "Load\n{0:F2}";
        [Tooltip("Use the smoothed (true) or raw (false) RIPA value.")]
        [SerializeField] private bool useSmoothedValue = true;

        [Header("Visibility")]
        [Tooltip("Hide the entire overlay while the monitor has not produced its first valid sample. Default OFF: chassis stays visible with a '--' label until data arrives.")]
        [SerializeField] private bool hideUntilValid = false;

        private Canvas overlayCanvas;
        private GameObject root;
        private RIPAGauge gauge;

        /// <summary>Set the corner anchor before the overlay's first build.</summary>
        public void SetCornerBeforeBuild(Corner c)
        {
            if (root != null)
            {
                Debug.LogWarning("[RIPAOverlay] SetCornerBeforeBuild called after Build; corner is already baked.");
                return;
            }
            corner = c;
        }

        private void OnEnable() { if (root == null) Build(); }
        private void OnDisable() { if (overlayCanvas != null) overlayCanvas.gameObject.SetActive(false); }
        private void OnDestroy() { if (overlayCanvas != null) Destroy(overlayCanvas.gameObject); }

        private void Update()
        {
            if (root == null) return;
            var m = RIPAMonitor.Instance;
            if (m == null) return;
            bool show = m.ShowOverlay && m.Enabled;
            if (hideUntilValid) show = show && m.IsValid;
            if (root.activeSelf != show) root.SetActive(show);
        }

        private void Build()
        {
            var canvasGo = new GameObject("[RIPAOverlay]");
            canvasGo.transform.SetParent(transform, false);
            overlayCanvas = canvasGo.AddComponent<Canvas>();
            overlayCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            overlayCanvas.sortingOrder = sortingOrder;
            canvasGo.AddComponent<CanvasScaler>();

            var rootGo = new GameObject("Root");
            rootGo.transform.SetParent(canvasGo.transform, false);
            var rootRect = rootGo.AddComponent<RectTransform>();
            ApplyCornerAnchor(rootRect);
            root = rootGo;

            var (g, widget) = RIPAGauge.CreateVerticalStrip(rootGo.transform, size);
            widget.anchorMin = widget.anchorMax = new Vector2(0.5f, 0.5f);
            widget.anchoredPosition = Vector2.zero;
            gauge = g;
            gauge.DisplayMax = displayMax;
            gauge.LabelFormat = labelFormat;
            gauge.UseSmoothedValue = useSmoothedValue;
        }

        private void ApplyCornerAnchor(RectTransform rect)
        {
            Vector2 anchor; Vector2 pivot; Vector2 anchored;
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
            rect.sizeDelta = size;
            rect.anchoredPosition = anchored;
        }
    }
}
