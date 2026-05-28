// SPDX-License-Identifier: MIT
using UnityEngine;
using EyeLean.NavigationMaze.Generation;

namespace EyeLean.NavigationMaze
{
    public class MazeDecisionPointTracker : MonoBehaviour
    {
        private MazeGrid _grid;
        private JunctionType[,] _junctions;
        private Transform _rig;

        private int _lastR = -1, _lastC = -1;
        private bool _inJunction;
        private float _junctionEnterTime;
        private int _junctionR, _junctionC;

        public int CellR => _lastR;
        public int CellC => _lastC;

        public int WrongTurns { get; private set; }
        public int DeadEndEntries { get; private set; }
        public int BacktrackCount { get; private set; }
        public float ActualPathLength { get; private set; }

        private readonly System.Collections.Generic.HashSet<(int, int)> _visitedCells =
            new System.Collections.Generic.HashSet<(int, int)>();
        private System.Collections.Generic.HashSet<(int, int)> _optimalPathSet;
        private Vector3 _lastPosition;

        public void Initialize(MazeGrid grid, JunctionType[,] junctions, Transform rig)
        {
            _grid = grid;
            _junctions = junctions;
            _rig = rig;
            _lastR = -1;
            _lastC = -1;
            _inJunction = false;
        }

        public void BeginTrial(System.Collections.Generic.List<(int r, int c)> optimalPath)
        {
            WrongTurns = 0;
            DeadEndEntries = 0;
            BacktrackCount = 0;
            ActualPathLength = 0f;
            _visitedCells.Clear();
            _optimalPathSet = new System.Collections.Generic.HashSet<(int, int)>();
            if (optimalPath != null)
                foreach (var cell in optimalPath)
                    _optimalPathSet.Add(cell);
            _lastPosition = _rig != null ? _rig.position : Vector3.zero;
        }

        private void Update()
        {
            if (_grid == null || _rig == null) return;

            Vector3 pos = _rig.position;
            ActualPathLength += Vector3.Distance(pos, _lastPosition);
            _lastPosition = pos;

            var (r, c) = _grid.WorldToCell(pos.x, pos.z);
            if (r == _lastR && c == _lastC) return;

            if (_inJunction)
            {
                float dwellMs = (Time.time - _junctionEnterTime) * 1000f;
                string exitDir = DirectionFrom(_junctionR, _junctionC, r, c);
                EyeLean.SceneState.SceneEventRecorder.RecordKV(
                    "DecisionPointExit", "",
                    ("cellR", _junctionR.ToString()),
                    ("cellC", _junctionC.ToString()),
                    ("exitDir", exitDir),
                    ("dwellMs", dwellMs.ToString("F0")));
                _inJunction = false;
            }

            if (_visitedCells.Contains((r, c))) BacktrackCount++;
            _visitedCells.Add((r, c));

            if (_optimalPathSet != null && !_optimalPathSet.Contains((r, c))) WrongTurns++;

            var jt = _junctions[r, c];
            if (jt == JunctionType.DeadEnd) DeadEndEntries++;

            if (MazeCellClassifier.IsDecisionPoint(jt))
            {
                Vector3 gazeDir = _rig.forward;
                EyeLean.SceneState.SceneEventRecorder.RecordKV(
                    "DecisionPointEnter", "",
                    ("cellR", r.ToString()),
                    ("cellC", c.ToString()),
                    ("junctionType", jt.ToString()),
                    ("gazeX", gazeDir.x.ToString("F3")),
                    ("gazeZ", gazeDir.z.ToString("F3")));
                _inJunction = true;
                _junctionEnterTime = Time.time;
                _junctionR = r;
                _junctionC = c;
            }

            _lastR = r;
            _lastC = c;
        }

        private static string DirectionFrom(int fromR, int fromC, int toR, int toC)
        {
            if (toR > fromR) return "N";
            if (toR < fromR) return "S";
            if (toC > fromC) return "E";
            return "W";
        }
    }
}
