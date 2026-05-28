// SPDX-License-Identifier: MIT
using TMPro;
using UnityEngine;

namespace EyeLean.NavigationMaze.UI
{
    public class MazeHUDController : MonoBehaviour
    {
        [SerializeField] private Vector3 cornerOffset = new Vector3(-0.45f, -0.30f, 1.0f);
        [SerializeField] private float fontSize = 0.18f;

        private TextMeshPro text;
        private string blockInfoText = "";
        private string timerText = "";
        private string statusText = "";
        private string trialLine = "";

        private void Awake()
        {
            var go = new GameObject("MazeHUDText");
            go.transform.SetParent(transform, false);
            text = go.AddComponent<TextMeshPro>();
            text.alignment = TextAlignmentOptions.Left;
            text.color = new Color(0.85f, 0.85f, 0.85f, 0.9f);
            text.fontSize = fontSize;
            text.rectTransform.sizeDelta = new Vector2(0.9f, 0.35f);
            text.SetText("");
        }

        /// <summary>
        /// Place the HUD relative to the supplied camera transform. Canonical
        /// pattern shared with NBackHUDController — caller decides timing
        /// (e.g., after VRReadinessService or replay's IsPlaying state).
        /// </summary>
        public void PlaceInFrontOf(Transform camT)
        {
            Vector3 forward = Vector3.ProjectOnPlane(camT.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
            Vector3 center = camT.position + forward * cornerOffset.z;
            transform.position = center + right * cornerOffset.x + Vector3.up * cornerOffset.y;
            transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
        }

        public void SetBlockInfo(int blockIndex, int totalBlocks, string modeName)
        {
            blockInfoText = $"Block {blockIndex + 1}/{totalBlocks} — {modeName}";
            Refresh();
        }

        public void SetTrial(int trialIndex, int totalTrials, string trialId, string condition)
        {
            trialLine = string.Format("Trial {0}/{1}  [{2}]", trialIndex + 1, totalTrials, condition);
            statusText = "";
            Refresh();
        }

        public void SetTimer(float secondsRemaining)
        {
            int s = Mathf.CeilToInt(secondsRemaining);
            timerText = $"Time: {s / 60}:{s % 60:D2}";
            Refresh();
        }

        public void SetStatus(string msg)
        {
            statusText = msg;
            Refresh();
        }

        public void SetMessage(string msg)
        {
            text.SetText(msg);
        }

        public void Clear()
        {
            trialLine = "";
            blockInfoText = "";
            timerText = "";
            statusText = "";
            text.SetText("");
        }

        private void Refresh()
        {
            string result = trialLine;
            if (!string.IsNullOrEmpty(blockInfoText)) result += "\n" + blockInfoText;
            if (!string.IsNullOrEmpty(timerText)) result += "\n" + timerText;
            if (!string.IsNullOrEmpty(statusText)) result += "\n" + statusText;
            text.text = result;
        }
    }
}
