// SPDX-License-Identifier: MIT
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace EyeTracking.Metrics
{
    /// <summary>
    /// Optional plug-and-play gauge for <see cref="RIPAMonitor"/>. Drop
    /// onto any UI Image (or assign one via the inspector) and the fill
    /// amount will track the current smoothed RIPA2 value, scaled by
    /// <see cref="DisplayMax"/>. Tints the bar green→amber→red as load rises.
    ///
    /// Build it in code or attach to an existing UI prefab — the component
    /// only needs an <see cref="UnityEngine.UI.Image"/> reference (the
    /// "fill" element). A label is optional.
    /// </summary>
    [DefaultExecutionOrder(-30)]
    public sealed class RIPAGauge : MonoBehaviour
    {
        [Header("UI bindings (optional)")]
        [Tooltip("Image whose fillAmount is driven by the current load. If unset, a sibling Image is auto-resolved.")]
        [SerializeField] private Image fillImage;
        [Tooltip("Optional label. {0} = formatted load value. Default '{0:F2}' shows two decimals.")]
        [SerializeField] private TextMeshProUGUI label;
        [SerializeField] private string labelFormat = "Load {0:F2}";

        [Header("Display range")]
        [Tooltip("Load value that maps to fillAmount = 1.0. The paper clips RIPA2 to [0, 1.5], so 1.5 saturates the bar.")]
        [SerializeField] private float displayMax = 1.5f;
        public float DisplayMax { get => displayMax; set => displayMax = Mathf.Max(1e-3f, value); }

        [Header("Tint thresholds")]
        [SerializeField] private Color colorLow = new Color(0.30f, 0.78f, 0.55f, 1f);
        [SerializeField] private Color colorMid = new Color(0.95f, 0.85f, 0.30f, 1f);
        [SerializeField] private Color colorHigh = new Color(0.92f, 0.40f, 0.30f, 1f);
        [Tooltip("Use the smoothed (true) or raw (false) RIPA2 value.")]
        [SerializeField] private bool useSmoothedValue = true;

        public bool UseSmoothedValue { get => useSmoothedValue; set => useSmoothedValue = value; }
        public string LabelFormat { get => labelFormat; set => labelFormat = value; }

        /// <summary>
        /// Wire the UI bindings programmatically. Use this when constructing
        /// the gauge from code (see <see cref="RIPAOverlay"/>) so callers don't
        /// have to depend on the inspector or reflection.
        /// </summary>
        public void Bind(Image fill, TextMeshProUGUI label = null)
        {
            fillImage = fill;
            this.label = label;
        }

        private RIPAMonitor boundMonitor;

        private void OnEnable()
        {
            if (fillImage == null) fillImage = GetComponent<Image>();
            if (fillImage != null && fillImage.type != Image.Type.Filled)
            {
                fillImage.type = Image.Type.Filled;
                fillImage.fillMethod = Image.FillMethod.Horizontal;
                fillImage.fillOrigin = (int)Image.OriginHorizontal.Left;
            }
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
            // Late-bind in case the monitor was spawned after this component
            // enabled. Once bound, updates are event-driven via OnLoadChanged.
            if (boundMonitor == null || !boundMonitor) ResolveMonitor();
        }

        private void ResolveMonitor()
        {
            var m = RIPAMonitor.Instance;
            if (m == null || m == boundMonitor) return;
            boundMonitor = m;
            boundMonitor.OnLoadChanged.AddListener(UpdateUi);
        }

        private void UpdateUi(float smoothed)
        {
            float load = useSmoothedValue ? smoothed
                       : (boundMonitor != null ? boundMonitor.CurrentRawLoad : 0f);
            float t = Mathf.Clamp01(load / Mathf.Max(1e-3f, displayMax));
            if (fillImage != null)
            {
                fillImage.fillAmount = t;
                fillImage.color = (t < 0.5f)
                    ? Color.Lerp(colorLow, colorMid, t * 2f)
                    : Color.Lerp(colorMid, colorHigh, (t - 0.5f) * 2f);
            }
            if (label != null)
            {
                label.text = string.Format(System.Globalization.CultureInfo.InvariantCulture, labelFormat, load);
            }
        }
    }
}
