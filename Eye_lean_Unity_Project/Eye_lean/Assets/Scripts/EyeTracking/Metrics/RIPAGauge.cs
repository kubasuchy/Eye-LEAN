// SPDX-License-Identifier: MIT
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace EyeTracking.Metrics
{
    /// <summary>
    /// Canonical cognitive-load gauge widget for Eye-LEAN. Builds and owns
    /// the visual hierarchy (backdrop + label + vertical fill bar) and
    /// binds to <see cref="RIPAMonitor"/> for data. Use the static factory
    /// <see cref="CreateVerticalStrip"/> to construct one — placement
    /// (corner of screen vs. inside a world-space panel) is the caller's
    /// only responsibility.
    ///
    /// Animation approach: the fill rect's <c>anchorMax.y</c> is driven
    /// from 0 (empty) to 1 (full). This avoids the Unity gotcha where
    /// <c>Image.Type.Filled</c> + <c>fillAmount</c> silently does nothing
    /// when no sprite is assigned. Color tints green→amber→red as load rises.
    /// </summary>
    [DefaultExecutionOrder(-30)]
    public sealed class RIPAGauge : MonoBehaviour
    {
        [Header("Range")]
        [Tooltip("Load value that maps to a full bar. All detectors normalize their CurrentSmoothed output to [0, 1.5], so 1.5 is the canonical default.")]
        [SerializeField] private float displayMax = 1.5f;
        public float DisplayMax { get => displayMax; set => displayMax = Mathf.Max(1e-3f, value); }

        [Header("Label")]
        [Tooltip("{0} = formatted load value. Default 'Load\\n{0:F2}' gives two lines with two decimals.")]
        [SerializeField] private string labelFormat = "Load\n{0:F2}";
        public string LabelFormat { get => labelFormat; set => labelFormat = value; }

        [Header("Detector")]
        [Tooltip("Use the smoothed (true) or raw (false) value from the bound detector.")]
        [SerializeField] private bool useSmoothedValue = true;
        public bool UseSmoothedValue { get => useSmoothedValue; set => useSmoothedValue = value; }

        [Header("Tint thresholds (green → amber → red)")]
        [SerializeField] private Color colorLow = new Color(0.30f, 0.78f, 0.55f, 1f);
        [SerializeField] private Color colorMid = new Color(0.95f, 0.85f, 0.30f, 1f);
        [SerializeField] private Color colorHigh = new Color(0.92f, 0.40f, 0.30f, 1f);

        private RectTransform fillRect;
        private Image fillImage;
        private TextMeshProUGUI label;
        private RIPAMonitor boundMonitor;
        private bool lastValid;

        private static readonly System.Text.RegularExpressions.Regex PlaceholderRegex =
            new System.Text.RegularExpressions.Regex(@"\{0[^}]*\}");

        /// <summary>
        /// Wire the visual bindings. The fill rect is animated via
        /// <c>anchorMax.y</c>; the image (typically on the same rect) is
        /// tinted; the label is updated each frame.
        /// </summary>
        public void Bind(RectTransform fillRect, Image fillImage, TextMeshProUGUI label = null)
        {
            this.fillRect = fillRect;
            this.fillImage = fillImage;
            this.label = label;
        }

        /// <summary>
        /// Build the complete gauge widget (backdrop + label + bar bg + fill)
        /// and attach a bound RIPAGauge component to drive it. The returned
        /// <paramref name="root"/> RectTransform is what the caller places.
        /// Placement (corner anchor / inside a panel / etc.) is the caller's
        /// responsibility.
        /// </summary>
        /// <param name="parent">Parent transform. The widget is parented to it with localPosition zero.</param>
        /// <param name="size">Widget size in canvas units. Vertical-strip proportions (narrow + tall) work best.</param>
        /// <param name="backdropColor">Backdrop tint behind the label and bar.</param>
        public static (RIPAGauge gauge, RectTransform root) CreateVerticalStrip(
            Transform parent, Vector2 size, Color? backdropColor = null)
        {
            Color bgColor = backdropColor ?? new Color(0.05f, 0.05f, 0.13f, 0.92f);

            var rootGo = new GameObject("RIPAGauge");
            rootGo.transform.SetParent(parent, false);
            var rootRect = rootGo.AddComponent<RectTransform>();
            rootRect.sizeDelta = size;
            var rootBg = rootGo.AddComponent<Image>();
            rootBg.color = bgColor;
            rootBg.raycastTarget = false;

            float labelHeight = Mathf.Min(72f, size.y * 0.30f);
            float labelTopPadding = 8f;
            var labelGo = new GameObject("Label");
            labelGo.transform.SetParent(rootGo.transform, false);
            var labelRect = labelGo.AddComponent<RectTransform>();
            labelRect.anchorMin = labelRect.anchorMax = new Vector2(0.5f, 0.5f);
            labelRect.sizeDelta = new Vector2(size.x - 8f, labelHeight);
            labelRect.anchoredPosition = new Vector2(0f, (size.y - labelHeight) * 0.5f - labelTopPadding);
            var label = labelGo.AddComponent<TextMeshProUGUI>();
            label.alignment = TextAlignmentOptions.Center;
            label.color = Color.white;
            label.fontSize = Mathf.Clamp(labelHeight * 0.35f, 12f, 24f);
            label.raycastTarget = false;
            label.text = "Load\n--";

            float barWidth = Mathf.Min(28f, size.x * 0.40f);
            float barTopPadding = labelHeight + labelTopPadding * 2f + 8f;
            float barBottomPadding = 12f;
            float barHeight = size.y - barTopPadding - barBottomPadding;
            float barCenterY = (barBottomPadding - barTopPadding) * 0.5f;

            var barBgGo = new GameObject("BarBg");
            barBgGo.transform.SetParent(rootGo.transform, false);
            var barBgRect = barBgGo.AddComponent<RectTransform>();
            barBgRect.anchorMin = barBgRect.anchorMax = new Vector2(0.5f, 0.5f);
            barBgRect.sizeDelta = new Vector2(barWidth, barHeight);
            barBgRect.anchoredPosition = new Vector2(0f, barCenterY);
            var barBgImg = barBgGo.AddComponent<Image>();
            barBgImg.color = new Color(0.18f, 0.18f, 0.22f, 1f);
            barBgImg.raycastTarget = false;

            var barFillGo = new GameObject("BarFill");
            barFillGo.transform.SetParent(barBgGo.transform, false);
            var barFillRect = barFillGo.AddComponent<RectTransform>();
            barFillRect.anchorMin = new Vector2(0f, 0f);
            barFillRect.anchorMax = new Vector2(1f, 0f);
            barFillRect.offsetMin = Vector2.zero;
            barFillRect.offsetMax = Vector2.zero;
            var barFillImg = barFillGo.AddComponent<Image>();
            barFillImg.color = new Color(0.30f, 0.78f, 0.55f, 1f);
            barFillImg.raycastTarget = false;

            var gauge = rootGo.AddComponent<RIPAGauge>();
            gauge.Bind(barFillRect, barFillImg, label);
            return (gauge, rootRect);
        }

        private void OnEnable()
        {
            ResolveMonitor();
            UpdateUi(boundMonitor != null ? boundMonitor.CurrentLoad : 0f);
        }

        private void OnDisable()
        {
            if (boundMonitor != null) boundMonitor.OnLoadChanged.RemoveListener(UpdateUi);
            boundMonitor = null;
        }

        private void Update()
        {
            if (boundMonitor == null || !boundMonitor) ResolveMonitor();
            if (boundMonitor != null)
            {
                bool valid = boundMonitor.IsValid;
                if (valid != lastValid || valid)
                {
                    lastValid = valid;
                    UpdateUi(boundMonitor.CurrentLoad);
                }
            }
        }

        private void ResolveMonitor()
        {
            var m = RIPAMonitor.Instance;
            if (m == null || m == boundMonitor) return;
            boundMonitor = m;
            boundMonitor.OnLoadChanged.AddListener(UpdateUi);
            UpdateUi(boundMonitor.CurrentLoad);
        }

        private void UpdateUi(float smoothed)
        {
            bool valid = boundMonitor != null && boundMonitor.IsValid;
            float load = !valid ? 0f
                       : (useSmoothedValue ? smoothed : boundMonitor.CurrentRawLoad);
            float t = valid ? Mathf.Clamp01(load / Mathf.Max(1e-3f, displayMax)) : 0f;

            if (fillRect != null)
            {
                Vector2 max = fillRect.anchorMax;
                max.y = t;
                fillRect.anchorMax = max;
            }
            if (fillImage != null)
            {
                fillImage.color = t < 0.5f
                    ? Color.Lerp(colorLow, colorMid, t * 2f)
                    : Color.Lerp(colorMid, colorHigh, (t - 0.5f) * 2f);
            }
            if (label != null)
            {
                label.text = valid
                    ? string.Format(System.Globalization.CultureInfo.InvariantCulture, labelFormat, load)
                    : PlaceholderRegex.Replace(labelFormat, "--");
            }
        }
    }
}
