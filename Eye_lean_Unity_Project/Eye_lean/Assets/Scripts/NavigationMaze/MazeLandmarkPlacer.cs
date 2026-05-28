// SPDX-License-Identifier: MIT
using System.Collections.Generic;
using UnityEngine;
using EyeLean.NavigationMaze.Generation;

namespace EyeLean.NavigationMaze
{
    public class MazeLandmarkPlacer : MonoBehaviour
    {
        private readonly List<GameObject> _active = new List<GameObject>();

        public IReadOnlyList<GameObject> ActiveLandmarks => _active;

        public void PlaceDistalLandmarks(MazeConfig config, float mazeSize)
        {
            ClearAll();
            if (config.distalLandmarks == null) return;

            float half = mazeSize * 0.5f;
            float h = config.distalLandmarkHeight;
            float r = config.distalLandmarkRadius;

            Vector3[] positions =
            {
                new Vector3(half, h * 0.5f, mazeSize + 1f), // North
                new Vector3(mazeSize + 1f, h * 0.5f, half), // East
                new Vector3(half, h * 0.5f, -1f),           // South
                new Vector3(-1f, h * 0.5f, half),           // West
            };

            for (int i = 0; i < config.distalLandmarks.Length && i < positions.Length; i++)
            {
                var cfg = config.distalLandmarks[i];
                var go = GameObject.CreatePrimitive(cfg.shape);
                go.name = "DistalLandmark_" + cfg.name;
                go.transform.SetParent(transform, false);
                go.transform.localPosition = positions[i];
                go.transform.localScale = new Vector3(r * 2f, h, r * 2f);
                TintSafe(go, cfg.color);
                // Collider retained so EyeTracker's gaze raycast can report
                // landmarks in the GazedObjectName CSV column ("DistalLandmark_North"
                // etc.). The collider is solid (not a trigger) so the
                // closest-hit raycast picks up the landmark when the
                // participant looks directly at it.
                _active.Add(go);
            }
        }

        public void PlaceProximalLandmarks(
            MazeConfig config, MazeGrid grid, JunctionType[,] junctions)
        {
            ClearAll();
            if (config.proximalLandmarkPool == null || config.proximalLandmarkPool.Length == 0) return;

            var candidates = new List<(int r, int c)>();
            for (int r = 0; r < grid.Size; r++)
                for (int c = 0; c < grid.Size; c++)
                    if (MazeCellClassifier.IsDecisionPoint(junctions[r, c]))
                        candidates.Add((r, c));

            int count = Mathf.Min(candidates.Count, config.maxProximalLandmarks);
            for (int i = 0; i < count; i++)
            {
                var (cr, cc) = candidates[i];
                var center = grid.CellCenter(cr, cc);
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = "ProximalLandmark_" + config.proximalLandmarkPool[i % config.proximalLandmarkPool.Length];
                go.transform.SetParent(transform, false);
                go.transform.localPosition = new Vector3(center.x, config.proximalLandmarkHeight, center.y);
                go.transform.localScale = new Vector3(0.3f, 0.3f, 0.3f);
                float hue = (float)i / count;
                TintSafe(go, Color.HSVToRGB(hue, 0.8f, 0.9f));
                // Collider retained — see PlaceDistalLandmarks for rationale.
                _active.Add(go);
            }
        }

        public void ClearAll()
        {
            foreach (var go in _active)
                if (go != null) DestroyImmediate(go);
            _active.Clear();
        }

        private static void TintSafe(GameObject go, Color c)
        {
            var rend = go.GetComponent<Renderer>();
            if (rend == null) return;
            try { rend.material = VRMaterialProvider.GetMaterial(c); }
            catch { rend.material.color = c; }
        }
    }
}
