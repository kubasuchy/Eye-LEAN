// SPDX-License-Identifier: MIT
using TMPro;
using UnityEngine;

namespace EyeLean.NBack.UI
{
    /// <summary>
    /// World-space panel that renders a single N-back stimulus letter
    /// centered on a black background. Parents itself under the main
    /// camera (HUD-locked) at <see cref="distanceMeters"/> so the letter
    /// always sits at the participant's central FoV regardless of head
    /// pose. The same panel renders the fixation cross during the
    /// passive-baseline block and during ISI between stimuli.
    ///
    /// Avoids per-frame string allocations: glyphs are pushed via SetText
    /// only when the displayed character actually changes.
    /// </summary>
    public class NBackStimulusPanel : MonoBehaviour
    {
        [Tooltip("Distance from camera in meters. RIPA2-paper default ≈ 1.0 m.")]
        [SerializeField] private float distanceMeters = 1.2f;

        [Tooltip("Panel width in meters at the rendering distance.")]
        [SerializeField] private float panelWidth = 0.5f;

        [Tooltip("Panel height in meters at the rendering distance.")]
        [SerializeField] private float panelHeight = 0.5f;

        [Tooltip("Font size for stimulus glyphs (in TMP units, not meters). Picked to fill ~50% of panel height.")]
        [SerializeField] private float glyphFontSize = 4f;

        [Tooltip("Font size for the fixation cross.")]
        [SerializeField] private float fixationFontSize = 4f;

        private TextMeshPro tmp;
        private MeshRenderer backdrop;
        // null sentinel — not "" — so the first ShowBlank() call from Awake
        // actually executes (lastShown == "" guard would otherwise short-
        // circuit before the backdrop is disabled, leaving a black quad
        // visible at scene load).
        private string lastShown = null;

        private void Awake()
        {
            EnsureBackdrop();
            EnsureText();
            // Start hidden so the panel doesn't overlap the instructions
            // panel (both are HUD-locked at the same camera distance). The
            // controller calls ShowFixation / ShowStimulus / ShowBlank as
            // each block phase demands.
            ShowBlank();
        }

        public void PlaceInFrontOf(Transform camT)
        {
            Vector3 forward = Vector3.ProjectOnPlane(camT.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;
            transform.position = camT.position + forward * distanceMeters;
            transform.position = new Vector3(transform.position.x, camT.position.y, transform.position.z);
            transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
        }

        public void ShowStimulus(string letter)
        {
            if (letter == lastShown) return;
            lastShown = letter;
            tmp.fontSize = glyphFontSize;
            tmp.SetText(letter);
            tmp.enabled = true;
            if (backdrop != null) backdrop.enabled = true;
        }

        public void ShowFixation()
        {
            const string cross = "+";
            if (lastShown == cross) return;
            lastShown = cross;
            tmp.fontSize = fixationFontSize;
            tmp.SetText(cross);
            tmp.enabled = true;
            if (backdrop != null) backdrop.enabled = true;
        }

        public void ShowBlank()
        {
            if (lastShown == "") return;
            lastShown = "";
            tmp.SetText("");
            tmp.enabled = false;
            // Hide the backdrop too so this panel doesn't occlude the
            // instructions panel that shares the same camera anchor.
            if (backdrop != null) backdrop.enabled = false;
        }

        private void EnsureBackdrop()
        {
            // The backdrop is a flat quad sized to panelWidth/Height,
            // tinted near-black so the stimulus letter has reliable
            // contrast against the room geometry behind it.
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "StimulusBackdrop";
            quad.transform.SetParent(transform, false);
            quad.transform.localPosition = new Vector3(0f, 0f, 0.001f); // behind the text
            quad.transform.localScale = new Vector3(panelWidth, panelHeight, 1f);
            // Strip the collider — gaze rays should pass straight through to
            // any GazeTargets behind the panel (none in this scene, but
            // future researchers may parent something here).
            var col = quad.GetComponent<Collider>();
            if (col != null) DestroyImmediate(col);
            backdrop = quad.GetComponent<MeshRenderer>();
            if (backdrop != null)
            {
                try { backdrop.material = VRMaterialProvider.GetMaterial(new Color(0.04f, 0.04f, 0.04f, 1f)); }
                catch { backdrop.material.color = new Color(0.04f, 0.04f, 0.04f, 1f); }
            }
        }

        private void EnsureText()
        {
            var textGO = new GameObject("StimulusText");
            textGO.transform.SetParent(transform, false);
            textGO.transform.localPosition = Vector3.zero;
            tmp = textGO.AddComponent<TextMeshPro>();
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;
            tmp.fontSize = glyphFontSize;
            tmp.SetText("");
            tmp.enableWordWrapping = false;
            var rect = tmp.rectTransform;
            rect.sizeDelta = new Vector2(panelWidth, panelHeight);
        }
    }
}
