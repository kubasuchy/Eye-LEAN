using UnityEngine;

namespace EyeTracking.Core
{
    /// <summary>
    /// Decouples EyeLean.Core from concrete environment generators. A scene
    /// component (e.g. the demo's <c>EnvironmentGenerator</c>) implements
    /// this when it owns a room-local transform that calibration targets and
    /// other core systems should be parented under so they rotate / translate
    /// with the user-spawn frame.
    ///
    /// Returning null is allowed and means "no room frame available; place
    /// this in world space." That fallback is what
    /// <c>CalibrationTestRunner</c> uses when no provider is found.
    /// </summary>
    public interface IRoomFrameProvider
    {
        Transform RoomTransform { get; }
    }
}
