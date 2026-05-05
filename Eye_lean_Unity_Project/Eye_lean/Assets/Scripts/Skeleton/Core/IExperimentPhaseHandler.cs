// SPDX-License-Identifier: MIT
using System.Collections.Generic;

namespace EyeLean.Skeleton
{
    /// <summary>
    /// Researcher-facing hook for the per-trial ExperimentalPhase. <see cref="TrialManager"/>
    /// drives the state machine (ITI -> Platform -> Fixation -> ExperimentalPhase ->
    /// TrialComplete) and delegates the experimental phase to a single component
    /// implementing this interface. Register your handler via
    /// <c>TrialManager.SetPhaseHandler(this)</c> in <c>Awake</c>; trial metadata returned
    /// from <see cref="GetPhaseData"/> is auto-flushed to the session CSV and
    /// trial-event sidecar. See <c>Examples/EyeleanDemoPhaseHandler.cs</c> for a
    /// reference implementation, and <c>docs/SKELETON_*.md</c> for long-form docs.
    /// </summary>
    public interface IExperimentPhaseHandler
    {
        /// <summary>Runs once when the ExperimentalPhase begins for the current trial.</summary>
        void OnPhaseStart();

        /// <summary>Runs once when the ExperimentalPhase ends (either via <see cref="IsPhaseComplete"/> or <c>TrialManager.CompleteExperimentalPhase()</c>).</summary>
        void OnPhaseEnd();

        /// <summary>Polled every frame while the phase is active. Return <c>true</c> to advance to <c>TrialComplete</c>.</summary>
        bool IsPhaseComplete();

        /// <summary>Per-trial data merged into the trial record (CSV + events sidecar). Return <c>null</c> or an empty dict if there is nothing to record.</summary>
        Dictionary<string, object> GetPhaseData();
    }
}
