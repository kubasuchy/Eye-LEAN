// SPDX-License-Identifier: MIT
namespace EyeTracking.Metrics
{
    /// <summary>
    /// Pupil-diameter ingress contract for <see cref="RIPAMonitor"/>. The
    /// monitor is otherwise self-contained — implementing this interface
    /// is the only integration step required to drop the cognitive-load
    /// stack into any Unity project, regardless of which eye-tracker
    /// SDK or hardware is in use.
    ///
    /// Implementations should return the latest averaged binocular pupil
    /// diameter in millimeters, or <c>double.NaN</c> when no valid
    /// sample is available this frame (occluded eye, blink, lost
    /// tracking, etc.). The monitor calls <see cref="GetLatestPupilDiameterMm"/>
    /// once per <c>Update</c>; values that are <c>NaN</c> or non-positive
    /// are skipped without affecting detector state.
    ///
    /// <para>Example implementations:</para>
    /// <list type="bullet">
    ///   <item><description><see cref="EyeLeanPupilSampleSource"/> — wraps Eye_lean's
    ///   EyeTracker MonoBehaviour and the hardware-detection factory.</description></item>
    ///   <item><description>A Pupil Labs adapter — wraps the Pupil Capture
    ///   network stream and returns its current pupil_diameter field.</description></item>
    ///   <item><description>A Tobii adapter — wraps the Tobii SDK's
    ///   gaze data stream.</description></item>
    /// </list>
    /// </summary>
    public interface IPupilSampleSource
    {
        /// <summary>
        /// Latest pupil diameter sample, in millimeters. Average of left
        /// + right eye if both are tracking; single-eye value if one is
        /// occluded; <c>double.NaN</c> if neither eye is reporting a
        /// valid sample this frame.
        /// </summary>
        double GetLatestPupilDiameterMm();

        /// <summary>
        /// Native sampling rate of the underlying tracker in Hertz, or
        /// <c>0</c> if unknown. Frequency-domain detectors (FFT, DWT)
        /// use this to size their internal windows; if zero, the
        /// monitor falls back to its <c>sampleRateOverrideHz</c>
        /// inspector field or a sensible default (~60 Hz).
        /// </summary>
        float SamplingRateHz { get; }
    }
}
