using NUnit.Framework;
using UnityEngine;
using EyeTracking.Components;
using EyeTracking.Core;

namespace EyeLean.Tests.EditMode
{
    /// <summary>
    /// EditMode coverage for HMDDataCollector. Round-9 (2026-05-02) bug 3
    /// regression: SetCoordinateOrigin used to silently no-op when its
    /// cached cameraTransform went Unity-null between Start and a deferred
    /// EnvironmentGenerator.ConfigureEyeTracker call. The new
    /// SetCoordinateOrigin returns bool and re-resolves Camera.main if the
    /// cached transform is dead — these tests pin that contract.
    /// </summary>
    public class HMDDataCollectorTests
    {
        private GameObject _go;
        private GameObject _cameraGo;

        [SetUp]
        public void SetUp()
        {
            _cameraGo = new GameObject("Main Camera");
            _cameraGo.tag = "MainCamera";
            _cameraGo.AddComponent<Camera>();
            _cameraGo.transform.position = new Vector3(1f, 1.7f, -2f);

            _go = new GameObject("HMDCollectorTestRoot");
            // Awake registers with SceneTransitionCoordinator if present —
            // EditMode tests run without a coordinator instance, the
            // registration call is null-checked.
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
            if (_cameraGo != null) Object.DestroyImmediate(_cameraGo);
        }

        // EditMode caveat: Camera.main does not necessarily return the test's
        // camera GameObject — the editor's Scene/Game preview cameras can win.
        // So inject the cameraTransform directly via the SerializeField for
        // deterministic tests instead of relying on Camera.main resolution.
        private static void InjectCamera(HMDDataCollector collector, Transform t)
        {
            var f = typeof(HMDDataCollector).GetField(
                "cameraTransform",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.NotNull(f, "cameraTransform field must exist on HMDDataCollector.");
            f.SetValue(collector, t);
        }

        [Test]
        public void SampleSnapshot_WithDefaultCamera_ReturnsCameraPose()
        {
            var collector = _go.AddComponent<HMDDataCollector>();
            InjectCamera(collector, _cameraGo.transform);
            var snapshot = collector.SampleSnapshot();
            Assert.IsTrue(snapshot.IsValid, "Snapshot should be valid when camera is alive.");
            Assert.AreEqual(_cameraGo.transform.position, snapshot.HeadPosition);
            Assert.AreEqual(_cameraGo.transform.rotation, snapshot.HeadRotation);
        }

        [Test]
        public void SetCoordinateOrigin_NoArg_UsesCurrentCameraPosition()
        {
            // Round-9 bug 3 regression: this used to silently no-op when
            // cameraTransform was Unity-null. Now it returns bool and uses
            // the injected (or resolved) transform.
            var collector = _go.AddComponent<HMDDataCollector>();
            InjectCamera(collector, _cameraGo.transform);
            bool ok = collector.SetCoordinateOrigin();
            Assert.IsTrue(ok, "SetCoordinateOrigin() should return true when a camera is wired.");
            Assert.IsTrue(collector.HasTrialStartPosition);
            Assert.AreEqual(_cameraGo.transform.position, collector.CurrentTrialStartPosition);
        }

        [Test]
        public void SetCoordinateOrigin_WhenCameraNull_LogsErrorReturnsFalse()
        {
            // EditMode caveat: Camera.main routinely resolves to the editor's
            // Scene/Game preview camera even after we destroy our test camera,
            // so we can't easily fake "no camera in scene." Instead disable
            // every Camera component before the test, run, then restore.
            Object.DestroyImmediate(_cameraGo);
            _cameraGo = null;
            var allCams = Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var savedEnabled = new System.Collections.Generic.Dictionary<Camera, bool>();
            foreach (var c in allCams) { savedEnabled[c] = c.enabled; c.enabled = false; }
            try
            {
                var collector = _go.AddComponent<HMDDataCollector>();
                InjectCamera(collector, null);
                UnityEngine.TestTools.LogAssert.Expect(LogType.Error,
                    new System.Text.RegularExpressions.Regex("SetCoordinateOrigin: no live camera available"));
                bool ok = collector.SetCoordinateOrigin();
                Assert.IsFalse(ok, "SetCoordinateOrigin should return false when no camera exists.");
                Assert.IsFalse(collector.HasTrialStartPosition);
            }
            finally
            {
                foreach (var kv in savedEnabled) if (kv.Key != null) kv.Key.enabled = kv.Value;
            }
        }

        [Test]
        public void SetCoordinateOrigin_Vector3Overload_AcceptsExplicitOrigin()
        {
            var collector = _go.AddComponent<HMDDataCollector>();
            Vector3 explicitOrigin = new Vector3(3f, 1.5f, 4f);
            collector.SetCoordinateOrigin(explicitOrigin);
            Assert.IsTrue(collector.HasTrialStartPosition);
            Assert.AreEqual(explicitOrigin, collector.CurrentTrialStartPosition);
        }

        [Test]
        public void ResetCoordinateOrigin_ClearsState()
        {
            var collector = _go.AddComponent<HMDDataCollector>();
            collector.SetCoordinateOrigin(Vector3.one);
            Assert.IsTrue(collector.HasTrialStartPosition);
            collector.ResetCoordinateOrigin();
            Assert.IsFalse(collector.HasTrialStartPosition);
            Assert.AreEqual(Vector3.zero, collector.CurrentTrialStartPosition);
        }
    }
}
