using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using EyeLean.Replay.Analysis;

namespace EyeLean.Tests.EditMode
{
    /// <summary>
    /// Tests for the Krejtz K-coefficient (z(a_i) − z(d_i), mean over
    /// paired fixation/saccade events) in
    /// EyeMovementClassifier.CalculateKCoefficient.
    /// </summary>
    public class EyeMovementClassifierKTests
    {
        private GameObject _go;
        private EyeMovementClassifier _classifier;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("KTests");
            _classifier = _go.AddComponent<EyeMovementClassifier>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null) UnityEngine.Object.DestroyImmediate(_go);
        }

        // The two ring buffers are private; reflect into them so we can
        // seed paired (duration, amplitude) data without driving a full
        // ReplayFrame stream.
        private void SeedPairs(IList<float> durations, IList<float> amplitudes)
        {
            Assert.AreEqual(durations.Count, amplitudes.Count, "test pairs must be index-aligned");
            var t = typeof(EyeMovementClassifier);
            var fixField = t.GetField("fixationDurations", BindingFlags.Instance | BindingFlags.NonPublic);
            var sacField = t.GetField("saccadeAmplitudes", BindingFlags.Instance | BindingFlags.NonPublic);
            var fixQ = (Queue<float>)fixField.GetValue(_classifier);
            var sacQ = (Queue<float>)sacField.GetValue(_classifier);
            fixQ.Clear(); sacQ.Clear();
            for (int i = 0; i < durations.Count; i++)
            {
                fixQ.Enqueue(durations[i]);
                sacQ.Enqueue(amplitudes[i]);
            }
        }

        private float GetK() => _classifier.GetCurrentKCoefficient();

        private static float HandComputeK(float[] d, float[] a)
        {
            int n = d.Length;
            float md = 0f, ma = 0f;
            for (int i = 0; i < n; i++) { md += d[i]; ma += a[i]; }
            md /= n; ma /= n;
            float ssD = 0f, ssA = 0f;
            for (int i = 0; i < n; i++)
            {
                ssD += (d[i] - md) * (d[i] - md);
                ssA += (a[i] - ma) * (a[i] - ma);
            }
            float sD = Mathf.Sqrt(ssD / (n - 1));
            float sA = Mathf.Sqrt(ssA / (n - 1));
            float k = 0f;
            for (int i = 0; i < n; i++)
                k += ((a[i] - ma) / sA) - ((d[i] - md) / sD);
            return k / n;
        }

        [Test]
        public void K_MatchesEq1_OnHandPickedData()
        {
            float[] d = { 0.10f, 0.30f, 0.20f, 0.50f, 0.15f };
            float[] a = { 8.0f,  3.0f,  6.0f,  2.0f,  10.0f };
            SeedPairs(d, a);
            float k = GetK();
            float expected = HandComputeK(d, a);
            Assert.AreEqual(expected, k, 1e-5f);
        }

        [Test]
        public void K_DegenerateSpread_ReturnsZeroNotInf()
        {
            // All durations identical ⇒ σ_d = 0; metric undefined; expect 0.
            float[] d = { 0.2f, 0.2f, 0.2f, 0.2f };
            float[] a = { 1.0f, 5.0f, 2.0f, 4.0f };
            SeedPairs(d, a);
            float k = GetK();
            Assert.AreEqual(0f, k, 1e-6f);
            Assert.IsFalse(float.IsNaN(k));
            Assert.IsFalse(float.IsInfinity(k));
        }

        [Test]
        public void K_TooFewPairs_ReturnsZero()
        {
            SeedPairs(new[] { 0.2f }, new[] { 5.0f });
            Assert.AreEqual(0f, GetK(), 1e-6f);
        }

        [Test]
        public void ClassifyAttention_IsSignBased_ByDefault()
        {
            // kDeadZone defaults to 0 ⇒ pure sign classification.
            Assert.AreEqual(EyeMovementClassifier.AttentionType.Focal,   _classifier.ClassifyAttention(0.01f));
            Assert.AreEqual(EyeMovementClassifier.AttentionType.Ambient, _classifier.ClassifyAttention(-0.01f));
            Assert.AreEqual(EyeMovementClassifier.AttentionType.Neutral, _classifier.ClassifyAttention(0f));
        }

        [Test]
        public void ClassifyAttention_DeadZoneWidensNeutralBand()
        {
            _classifier.kDeadZone = 0.5f;
            Assert.AreEqual(EyeMovementClassifier.AttentionType.Neutral, _classifier.ClassifyAttention(0.3f));
            Assert.AreEqual(EyeMovementClassifier.AttentionType.Focal,   _classifier.ClassifyAttention(0.6f));
            Assert.AreEqual(EyeMovementClassifier.AttentionType.Ambient, _classifier.ClassifyAttention(-0.6f));
        }

        [Test]
        public void ClassifyAttention_NaN_ReturnsUnknown()
        {
            Assert.AreEqual(EyeMovementClassifier.AttentionType.Unknown, _classifier.ClassifyAttention(float.NaN));
        }
    }
}
