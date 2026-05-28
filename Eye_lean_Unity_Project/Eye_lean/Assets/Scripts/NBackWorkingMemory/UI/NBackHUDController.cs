// SPDX-License-Identifier: MIT
using TMPro;
using UnityEngine;

namespace EyeLean.NBack.UI
{
    /// <summary>
    /// Corner HUD displaying the current block index, load level, and trial
    /// number. Parented under the camera with a left-bottom offset so it
    /// sits at the participant's peripheral FoV — visible but not
    /// distracting. Mirrors the cognitive-load HUD pattern used by
    /// <c>RIPAOverlay</c>.
    /// </summary>
    public class NBackHUDController : MonoBehaviour
    {
        [SerializeField] private Vector3 cornerOffset = new Vector3(-0.45f, -0.30f, 1.0f);
        [SerializeField] private float fontSize = 0.18f;

        private TextMeshPro text;

        private void Awake()
        {
            var go = new GameObject("NBackHUDText");
            go.transform.SetParent(transform, false);
            text = go.AddComponent<TextMeshPro>();
            text.alignment = TextAlignmentOptions.Left;
            text.color = new Color(0.85f, 0.85f, 0.85f, 0.9f);
            text.fontSize = fontSize;
            text.rectTransform.sizeDelta = new Vector2(0.7f, 0.25f);
            text.SetText("");
        }

        public void PlaceInFrontOf(Transform camT)
        {
            Vector3 forward = Vector3.ProjectOnPlane(camT.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
            Vector3 center = camT.position + forward * cornerOffset.z;
            transform.position = center + right * cornerOffset.x + Vector3.up * cornerOffset.y;
            transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
        }

        public void SetStatus(int blockIndex, int totalBlocks, int loadLevel, int trial, int totalTrials)
        {
            string levelLabel = loadLevel switch
            {
                -1 => "Baseline",
                -2 => "—",
                _ => loadLevel + "-back",
            };
            text.text = string.Format("Block {0}/{1}  {2}\nTrial {3}/{4}",
                blockIndex + 1, totalBlocks, levelLabel, trial, totalTrials);
        }

        public void SetMessage(string msg)
        {
            text.SetText(msg);
        }

        public void Clear()
        {
            text.SetText("");
        }
    }
}
