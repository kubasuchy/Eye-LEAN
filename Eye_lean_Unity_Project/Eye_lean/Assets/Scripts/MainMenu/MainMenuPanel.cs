// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace EyeLean.MainMenu
{
    public class MainMenuPanel : MonoBehaviour
    {
        [Header("Layout")]
        [SerializeField] private float distanceMeters = 1.2f;
        [SerializeField] private float panelWidth = 1.6f;
        [SerializeField] private float buttonHeight = 0.10f;
        [SerializeField] private float buttonSpacing = 0.06f;
        [SerializeField] private float buttonInset = 0.08f;
        [SerializeField] private float headerHeight = 0.30f;
        [SerializeField] private float footerHeight = 0.08f;

        [Header("Dwell")]
        [SerializeField] private float dwellTimeSeconds = 3.0f;
        [SerializeField] private float gazeAngleDegrees = 6f;

        [Header("Colors")]
        [SerializeField] private Color panelColor = new Color(0.025f, 0.025f, 0.04f);
        [SerializeField] private Color buttonColor = new Color(0.06f, 0.06f, 0.08f);
        [SerializeField] private Color buttonHoverColor = new Color(0.10f, 0.10f, 0.14f);
        [SerializeField] private Color fillColor = new Color(0.18f, 0.45f, 0.85f, 0.9f);
        [SerializeField] private Color textColor = new Color(0.92f, 0.92f, 0.92f);
        [SerializeField] private Color subtitleColor = new Color(0.55f, 0.55f, 0.60f);

        public event Action<int> OnButtonActivated;

        private readonly List<ButtonSlot> buttons = new List<ButtonSlot>();
        private TextMeshPro titleText;
        private TextMeshPro subtitleText;
        private TextMeshPro footerText;
        private MeshRenderer panelBackdrop;
        private int activeButton = -1;
        private float dwellElapsed;
        private bool placed;
        private bool readyToPlace;
        private Material cachedButtonMat;
        private Material cachedHoverMat;

        private struct ButtonSlot
        {
            public Transform root;
            public MeshRenderer backdrop;
            public MeshRenderer fill;
            public TextMeshPro label;
            public Vector3 worldCenter;
        }

        public void Build(string title, string subtitle, string[] labels)
        {
            try { cachedButtonMat = VRMaterialProvider.GetMaterial(buttonColor); } catch {}
            try { cachedHoverMat = VRMaterialProvider.GetMaterial(buttonHoverColor); } catch {}
            StartCoroutine(WaitForCameraThenPlace());
            float buttonWidth = panelWidth - buttonInset * 2f;
            float totalButtonsH = labels.Length * buttonHeight + (labels.Length - 1) * buttonSpacing;
            float panelHeight = headerHeight + totalButtonsH + footerHeight + 0.04f;

            CreateBackdrop(panelWidth, panelHeight);
            float yTop = panelHeight * 0.5f;

            titleText = CreateText("Title", new Vector3(0, yTop - 0.07f, -0.001f),
                title, 0.45f, textColor, FontStyles.Bold, panelWidth * 0.9f, 0.12f);

            subtitleText = CreateText("Subtitle", new Vector3(0, yTop - 0.18f, -0.001f),
                subtitle, 0.20f, subtitleColor, FontStyles.Normal, panelWidth * 0.9f, 0.14f);

            float btnY = yTop - headerHeight;
            for (int i = 0; i < labels.Length; i++)
            {
                float y = btnY - i * (buttonHeight + buttonSpacing) - buttonHeight * 0.5f;
                buttons.Add(CreateButton(i, labels[i], new Vector3(0, y, -0.001f), buttonWidth, buttonHeight));
            }

            float footerY = btnY - totalButtonsH - 0.03f;
            footerText = CreateText("Footer", new Vector3(0, footerY, -0.001f),
                $"Look at a button for {dwellTimeSeconds:F1}s to select", 0.14f,
                subtitleColor, FontStyles.Italic, panelWidth * 0.9f, 0.06f);
        }

        public void SetSubtitle(string text)
        {
            if (subtitleText != null) subtitleText.SetText(text);
        }

        private System.Collections.IEnumerator WaitForCameraThenPlace()
        {
            var readiness = EyeTracking.Core.VRReadinessService.Instance;
            if (readiness != null) yield return readiness.WaitForCameraReady(8f);
            yield return null;

            var cam = Camera.main;
            if (cam != null)
            {
                var camT = cam.transform;
                Vector3 forward = Vector3.ProjectOnPlane(camT.forward, Vector3.up).normalized;
                if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;
                transform.position = camT.position + forward * distanceMeters;
                transform.position = new Vector3(transform.position.x, camT.position.y, transform.position.z);
                transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
            }
            placed = true;
        }

        private void LateUpdate()
        {
            if (!placed) return;
            var cam = Camera.main;
            if (cam == null) return;
            UpdateGaze(cam.transform);
        }

        private void UpdateGaze(Transform camT)
        {
            // Prefer real eye gaze when the tracker is producing valid
            // samples; fall back to head direction otherwise (Mac editor,
            // pre-warmup, hardware fault). Same pattern used by the Maze
            // landmark fixation tracker.
            Vector3 gazeDir = camT.forward;
            Vector3 origin = camT.position;
            var tracker = EyeTracking.Core.EyeTrackerFactory.GetEyeTracker();
            if (tracker != null && tracker.IsAvailable
                && tracker.GetCombinedGazeOrigin(out Vector3 trackerOrigin)
                && tracker.GetCombinedGazeDirection(out Vector3 trackerDir)
                && trackerDir.sqrMagnitude > 0.01f)
            {
                origin = trackerOrigin;
                gazeDir = trackerDir;
            }

            int hoveredIdx = -1;
            float bestAngle = gazeAngleDegrees;

            for (int i = 0; i < buttons.Count; i++)
            {
                Vector3 toBtn = (buttons[i].root.position - origin).normalized;
                float angle = Vector3.Angle(gazeDir, toBtn);
                if (angle < bestAngle)
                {
                    bestAngle = angle;
                    hoveredIdx = i;
                }
            }

            if (hoveredIdx != activeButton)
            {
                if (activeButton >= 0) SetButtonVisual(activeButton, false, 0f);
                activeButton = hoveredIdx;
                dwellElapsed = 0f;
                if (activeButton >= 0) SetButtonVisual(activeButton, true, 0f);
            }

            if (activeButton >= 0)
            {
                dwellElapsed += Time.deltaTime;
                float progress = Mathf.Clamp01(dwellElapsed / dwellTimeSeconds);
                SetButtonVisual(activeButton, true, progress);

                if (dwellElapsed >= dwellTimeSeconds)
                {
                    int idx = activeButton;
                    activeButton = -1;
                    dwellElapsed = 0f;
                    OnButtonActivated?.Invoke(idx);
                }
            }
        }

        private void SetButtonVisual(int idx, bool hovered, float progress)
        {
            var slot = buttons[idx];
            if (slot.backdrop != null)
            {
                var mat = hovered ? cachedHoverMat : cachedButtonMat;
                if (mat != null) slot.backdrop.sharedMaterial = mat;
            }
            if (slot.fill != null)
            {
                slot.fill.enabled = progress > 0.001f;
                float fillWidth = (panelWidth - buttonInset * 2f) * progress;
                float fullWidth = panelWidth - buttonInset * 2f;
                slot.fill.transform.localScale = new Vector3(fillWidth, buttonHeight - 0.005f, 1f);
                slot.fill.transform.localPosition = new Vector3(
                    -fullWidth * 0.5f + fillWidth * 0.5f,
                    slot.root.localPosition.y,
                    -0.0015f);
            }
        }

        private void CreateBackdrop(float w, float h)
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "PanelBackdrop";
            quad.transform.SetParent(transform, false);
            quad.transform.localScale = new Vector3(w, h, 1f);
            var col = quad.GetComponent<Collider>();
            if (col != null) DestroyImmediate(col);
            panelBackdrop = quad.GetComponent<MeshRenderer>();
            try { panelBackdrop.material = VRMaterialProvider.GetMaterial(panelColor); }
            catch { panelBackdrop.material.color = panelColor; }
        }

        private ButtonSlot CreateButton(int index, string labelStr, Vector3 localPos, float w, float h)
        {
            var root = new GameObject($"Button_{index}");
            root.transform.SetParent(transform, false);
            root.transform.localPosition = localPos;

            var bgQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            bgQuad.name = "BtnBg";
            bgQuad.transform.SetParent(transform, false);
            bgQuad.transform.localPosition = localPos;
            bgQuad.transform.localScale = new Vector3(w, h, 1f);
            var bgCol = bgQuad.GetComponent<Collider>();
            if (bgCol != null) DestroyImmediate(bgCol);
            var bgRend = bgQuad.GetComponent<MeshRenderer>();
            try { bgRend.material = VRMaterialProvider.GetMaterial(buttonColor); }
            catch { bgRend.material.color = buttonColor; }

            var fillQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            fillQuad.name = "BtnFill";
            fillQuad.transform.SetParent(transform, false);
            fillQuad.transform.localPosition = new Vector3(localPos.x - w * 0.5f, localPos.y, localPos.z - 0.0005f);
            fillQuad.transform.localScale = new Vector3(0f, h - 0.005f, 1f);
            var fillCol = fillQuad.GetComponent<Collider>();
            if (fillCol != null) DestroyImmediate(fillCol);
            var fillRend = fillQuad.GetComponent<MeshRenderer>();
            try { fillRend.material = VRMaterialProvider.GetMaterial(fillColor); }
            catch { fillRend.material.color = fillColor; }
            fillRend.enabled = false;

            var label = CreateText($"Label_{index}",
                new Vector3(localPos.x, localPos.y, localPos.z - 0.001f),
                labelStr, 0.22f, textColor, FontStyles.Normal, w * 0.9f, h);

            return new ButtonSlot
            {
                root = root.transform,
                backdrop = bgRend,
                fill = fillRend,
                label = label,
            };
        }

        private TextMeshPro CreateText(string name, Vector3 localPos, string content,
            float size, Color color, FontStyles style, float width, float height)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = localPos;
            var tmp = go.AddComponent<TextMeshPro>();
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = color;
            tmp.fontSize = size;
            tmp.fontStyle = style;
            tmp.enableWordWrapping = true;
            tmp.rectTransform.sizeDelta = new Vector2(width, height);
            tmp.SetText(content);
            return tmp;
        }
    }
}
