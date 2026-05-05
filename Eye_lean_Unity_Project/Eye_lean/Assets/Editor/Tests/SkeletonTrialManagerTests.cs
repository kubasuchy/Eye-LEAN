using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using EyeLean.Skeleton;
using EyeTracking.Components;

namespace EyeLean.Tests.EditMode
{
    /// <summary>
    /// EditMode coverage for Skeleton's TrialManager wiring into
    /// Eye_lean's SessionRecorder + SceneEventRecorder. We only
    /// exercise paths that work without Unity's per-scene Start
    /// lifecycle (no scene load, no SetActive) — full end-to-end
    /// behavior is covered by PlayMode runs.
    /// </summary>
    public class SkeletonTrialManagerTests
    {
        private GameObject root;

        [SetUp] public void SetUp() => root = new GameObject("SkeletonTrialMgrTestRoot");

        [TearDown]
        public void TearDown()
        {
            if (root != null) Object.DestroyImmediate(root);
        }

        [Test]
        public void SetPhaseHandler_RegistersHandlerWithoutThrowing()
        {
            var trialMgr = root.AddComponent<TrialManager>();
            var stub = new StubPhaseHandler();
            // Should not throw — even when no SessionRecorder is in
            // scene the handler registration is purely local state.
            Assert.DoesNotThrow(() => trialMgr.SetPhaseHandler(stub));
        }

        [Test]
        public void GetCurrentPhase_DefaultsToInterTrialInterval()
        {
            var trialMgr = root.AddComponent<TrialManager>();
            Assert.AreEqual(TrialManager.TrialPhase.InterTrialInterval, trialMgr.GetCurrentPhase());
        }

        [Test]
        public void IsUsingConfiguration_FalseWhenNoConfigAssigned()
        {
            var trialMgr = root.AddComponent<TrialManager>();
            Assert.IsFalse(trialMgr.IsUsingConfiguration());
        }

        [Test]
        public void TrialConfiguration_TotalsMatchBlockSum()
        {
            var cfg = ScriptableObject.CreateInstance<TrialConfiguration>();
            cfg.blocks.Add(new TrialBlock { blockName = "A", trialsInBlock = 3 });
            cfg.blocks.Add(new TrialBlock { blockName = "B", trialsInBlock = 5 });
            Assert.AreEqual(8, cfg.TotalTrials);
            Assert.AreEqual(2, cfg.TotalBlocks);
            Object.DestroyImmediate(cfg);
        }

        [Test]
        public void TrialConfiguration_RejectsDuplicateBlockNames()
        {
            var cfg = ScriptableObject.CreateInstance<TrialConfiguration>();
            cfg.blocks.Add(new TrialBlock { blockName = "Same", trialsInBlock = 3 });
            cfg.blocks.Add(new TrialBlock { blockName = "Same", trialsInBlock = 3 });
            bool ok = cfg.IsValid(out string msg);
            Assert.IsFalse(ok);
            StringAssert.Contains("Duplicate", msg);
            Object.DestroyImmediate(cfg);
        }

        [Test]
        public void TrialConfiguration_RejectsZeroTrialBlock()
        {
            var cfg = ScriptableObject.CreateInstance<TrialConfiguration>();
            cfg.blocks.Add(new TrialBlock { blockName = "Empty", trialsInBlock = 0 });
            Assert.IsFalse(cfg.IsValid(out _));
            Object.DestroyImmediate(cfg);
        }

        [Test]
        public void RegisterMetric_FromSkeletonHandler_ReachesRecorderRegistry()
        {
            // The demo handler registers a 'StimulusVisible' metric in
            // its Awake. Verify the SessionRecorder API contract that
            // it relies on still works — we use a fresh recorder here
            // and a synthetic getter to keep the test hermetic.
            var rec = root.AddComponent<SessionRecorder>();
            float visible = 0f;
            rec.RegisterMetric("StimulusVisible", () => visible, "F0");
            Assert.AreEqual(1, rec.RegisteredMetrics.Count);
            Assert.AreEqual("StimulusVisible", rec.RegisteredMetrics[0].Name);
            visible = 1f;
            Assert.AreEqual("1", rec.RegisteredMetrics[0].Getter());
        }

        [Test]
        public void TrialBlock_GetSummary_IncludesNameAndTrialCount()
        {
            var b = new TrialBlock { blockName = "Practice", trialsInBlock = 7, agentMeanSpeed = 1.2f };
            string s = b.GetSummary();
            StringAssert.Contains("Practice", s);
            StringAssert.Contains("7", s);
            StringAssert.Contains("1.20", s);
        }

        // Minimal IExperimentPhaseHandler test stub.
        private sealed class StubPhaseHandler : IExperimentPhaseHandler
        {
            public bool started, ended;
            public bool complete;
            public Dictionary<string, object> data = new Dictionary<string, object> { { "stub", 1 } };
            public void OnPhaseStart() => started = true;
            public void OnPhaseEnd() => ended = true;
            public bool IsPhaseComplete() => complete;
            public Dictionary<string, object> GetPhaseData() => data;
        }
    }
}
