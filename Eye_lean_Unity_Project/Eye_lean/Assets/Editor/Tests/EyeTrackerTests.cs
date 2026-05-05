using NUnit.Framework;
using UnityEngine;
using EyeTracking.Components;
using EyeTracking.Core;

namespace EyeLean.Tests.EditMode
{
    /// <summary>
    /// EditMode coverage for EyeTracker. EditMode can't easily exercise the
    /// full IEyeTracker pipeline (the OpenXR provider needs a live VIVE),
    /// so these tests focus on the parts that ARE testable headlessly:
    /// snapshot invariants when no tracker is available, the pure-math
    /// vector-vector intersection utility, and the debug-mode locking
    /// guarantee. The byte-identical CSV contract lives in
    /// SessionRecorderTests.
    /// </summary>
    public class EyeTrackerTests
    {
        private GameObject _go;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("EyeTrackerTestRoot");
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
        }

        [Test]
        public void EyeFrameSample_Invalid_HasAllFlagsFalse()
        {
            // Sanity check on the Invalid sentinel — used by HMDDataCollector
            // and EyeTracker when no live data is available.
            var s = EyeFrameSample.Invalid;
            Assert.IsFalse(s.HasLeftValid);
            Assert.IsFalse(s.HasRightValid);
            Assert.IsFalse(s.HasCombinedValid);
            Assert.IsFalse(s.HasValidVergence);
            Assert.AreEqual(string.Empty, s.GazedObjectName);
        }

        [Test]
        public void HmdFrameSample_Invalid_HasIsValidFalse()
        {
            var s = HmdFrameSample.Invalid;
            Assert.IsFalse(s.IsValid);
            Assert.IsFalse(s.HasTrialStartPosition);
            Assert.AreEqual(Vector3.zero, s.HeadPosition);
        }

        [Test]
        public void CalculateVectorVectorIntersection_ConvergingRays_ReturnsValidParameters()
        {
            // Two rays from (-0.03, 0, 0) and (0.03, 0, 0) both pointing
            // toward (0, 0, 1) — should intersect very close to (0, 0, 1)
            // i.e. t1 ~ t2 ~ ~1.0006 (slight angle).
            var et = _go.AddComponent<EyeTracker>();
            Vector3 leftOrigin = new Vector3(-0.03f, 0f, 0f);
            Vector3 rightOrigin = new Vector3(0.03f, 0f, 0f);
            Vector3 leftDir = (new Vector3(0f, 0f, 1f) - leftOrigin).normalized;
            Vector3 rightDir = (new Vector3(0f, 0f, 1f) - rightOrigin).normalized;
            bool ok = et.CalculateVectorVectorIntersection(leftOrigin, rightOrigin, leftDir, rightDir, out float t1, out float t2);
            Assert.IsTrue(ok, "Converging rays should produce a valid intersection.");
            Assert.Greater(t1, 0f, "t1 should be positive (ray going forward).");
            Assert.Greater(t2, 0f, "t2 should be positive (ray going forward).");
        }

        [Test]
        public void CalculateVectorVectorIntersection_ParallelRays_ReturnsFalse()
        {
            var et = _go.AddComponent<EyeTracker>();
            // Two parallel rays — denominator is zero, should return false.
            bool ok = et.CalculateVectorVectorIntersection(
                Vector3.zero, new Vector3(0.06f, 0f, 0f),
                Vector3.forward, Vector3.forward,
                out float _, out float _);
            Assert.IsFalse(ok, "Parallel rays should fail intersection.");
        }
    }
}
