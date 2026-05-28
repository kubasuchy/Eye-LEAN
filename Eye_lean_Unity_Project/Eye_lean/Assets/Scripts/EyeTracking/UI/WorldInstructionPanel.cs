// SPDX-License-Identifier: MIT
using TMPro;
using UnityEngine;

namespace EyeTracking.UI
{
    /// <summary>
    /// Canonical world-space instruction panel for Eye-LEAN experiments.
    /// Renders a title + body text on a dark backdrop quad, placed once in
    /// the participant's view via <see cref="PlaceInFrontOf"/>. Use the
    /// static <see cref="Create"/> factory to build a fully-configured panel
    /// or attach the component to an empty GameObject and rely on lazy
    /// setup.
    ///
    /// Uses TextMeshPro 3D (not Canvas) so the panel can be placed and
    /// oriented in world space without the Canvas overhead. The backdrop
    /// material is sourced from <see cref="VRMaterialProvider"/> which has
    /// a proven shader fallback chain on Android.
    /// </summary>
    public sealed class WorldInstructionPanel : MonoBehaviour
    {
        [Header("Placement")]
        [Tooltip("Distance in meters from camera when PlaceInFrontOf is called.")]
        [SerializeField] private float distanceMeters = 1.2f;

        [Header("Size")]
        [Tooltip("Width of the panel in world meters.")]
        [SerializeField] private float panelWidth = 1.2f;
        [Tooltip("Height of the panel in world meters.")]
        [SerializeField] private float panelHeight = 0.76f;

        [Header("Typography")]
        [SerializeField] private float titleFontSize = 0.6f;
        [SerializeField] private float bodyFontSize = 0.35f;
        [SerializeField] private Color titleColor = Color.white;
        [SerializeField] private Color bodyColor = new Color(0.92f, 0.92f, 0.92f);

        [Header("Backdrop")]
        [SerializeField] private Color backdropColor = new Color(0.05f, 0.05f, 0.05f, 1f);

        private TextMeshPro titleText;
        private TextMeshPro bodyText;
        private MeshRenderer backdrop;
        private bool initialized;

        /// <summary>
        /// Place the panel at <paramref name="distanceMeters"/> in front of
        /// the camera at the camera's eye height. Yaw-projected forward so
        /// the panel stays upright even if the user is looking up or down.
        /// </summary>
        public void PlaceInFrontOf(Transform camT)
        {
            EnsureSetup();
            Vector3 forward = Vector3.ProjectOnPlane(camT.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;
            transform.position = camT.position + forward * distanceMeters;
            transform.position = new Vector3(transform.position.x, camT.position.y, transform.position.z);
            transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
        }

        /// <summary>Show the panel with a title and body. Both texts are replaced.</summary>
        public void Show(string title, string body)
        {
            EnsureSetup();
            titleText.SetText(title);
            bodyText.SetText(body);
            SetEnabled(true);
        }

        /// <summary>Update only the body text — leaves the title unchanged. Useful for countdowns inside an already-titled briefing.</summary>
        public void SetBodyOnly(string body)
        {
            EnsureSetup();
            bodyText.SetText(body);
            SetEnabled(true);
        }

        /// <summary>Hide the panel (renderers off; placement preserved).</summary>
        public void Hide()
        {
            EnsureSetup();
            SetEnabled(false);
        }

        /// <summary>
        /// Build a panel under <paramref name="parent"/> and return it. Use
        /// when constructing UI from code; otherwise add this component to
        /// a GameObject manually and assign serialized fields in the editor.
        /// </summary>
        public static WorldInstructionPanel Create(Transform parent, Vector2? size = null)
        {
            var go = new GameObject("WorldInstructionPanel");
            go.transform.SetParent(parent, false);
            var panel = go.AddComponent<WorldInstructionPanel>();
            if (size.HasValue)
            {
                panel.panelWidth = size.Value.x;
                panel.panelHeight = size.Value.y;
            }
            panel.EnsureSetup();
            panel.Hide();
            return panel;
        }

        private void Awake()
        {
            EnsureSetup();
            Hide();
        }

        // Lazy init guards against the case where another component calls
        // Show/Hide before this MonoBehaviour's Awake fires (Unity's Awake
        // order across sibling components is non-deterministic).
        private void EnsureSetup()
        {
            if (initialized) return;
            EnsureBackdrop();
            EnsureText();
            initialized = true;
        }

        private void SetEnabled(bool on)
        {
            if (titleText != null) titleText.enabled = on;
            if (bodyText != null) bodyText.enabled = on;
            if (backdrop != null) backdrop.enabled = on;
        }

        private void EnsureBackdrop()
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "Backdrop";
            quad.transform.SetParent(transform, false);
            quad.transform.localPosition = new Vector3(0f, 0f, 0.001f);
            quad.transform.localScale = new Vector3(panelWidth, panelHeight, 1f);
            var col = quad.GetComponent<Collider>();
            if (col != null) DestroyImmediate(col);
            backdrop = quad.GetComponent<MeshRenderer>();
            if (backdrop != null)
            {
                try { backdrop.material = VRMaterialProvider.GetMaterial(backdropColor); }
                catch { backdrop.material.color = backdropColor; }
            }
        }

        private void EnsureText()
        {
            var titleGO = new GameObject("Title");
            titleGO.transform.SetParent(transform, false);
            titleGO.transform.localPosition = new Vector3(0f, panelHeight * 0.30f, 0f);
            titleText = titleGO.AddComponent<TextMeshPro>();
            titleText.alignment = TextAlignmentOptions.Center;
            titleText.color = titleColor;
            titleText.fontSize = titleFontSize;
            titleText.fontStyle = FontStyles.Bold;
            titleText.rectTransform.sizeDelta = new Vector2(panelWidth * 0.9f, panelHeight * 0.3f);

            var bodyGO = new GameObject("Body");
            bodyGO.transform.SetParent(transform, false);
            bodyGO.transform.localPosition = new Vector3(0f, -panelHeight * 0.05f, 0f);
            bodyText = bodyGO.AddComponent<TextMeshPro>();
            bodyText.alignment = TextAlignmentOptions.Center;
            bodyText.color = bodyColor;
            bodyText.fontSize = bodyFontSize;
            bodyText.enableWordWrapping = true;
            bodyText.rectTransform.sizeDelta = new Vector2(panelWidth * 0.85f, panelHeight * 0.55f);
        }
    }
}
