// SPDX-License-Identifier: MIT
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR;

namespace EyeLean.Skeleton
{
    /// <summary>
    /// Editor-only desktop test locomotion for the participant camera so the trial
    /// loop can be exercised without a headset: WASD to walk (relative to facing,
    /// horizontal so looking down doesn't sink you), Q/E down/up, hold RIGHT mouse
    /// to look, Left Shift to sprint.
    ///
    /// VR-safe by construction: does nothing unless running in the Editor AND no XR
    /// device is active, so it never interferes with a real headset session or a
    /// shipped build. Because <see cref="HmdPoseDriverBootstrap"/> attaches a
    /// <c>TrackedPoseDriver</c> to Camera.main at runtime even with no headset, this
    /// disables that driver in desktop mode so it can't zero out the camera each frame.
    ///
    /// Attach to Camera.main (the Skeleton wizard does this automatically). Moving the
    /// transform also fires trigger events, so you can walk onto the StartingPlatform
    /// and through the exit to drive the loop.
    /// </summary>
    public class DesktopTestCameraController : MonoBehaviour
    {
        [Header("Movement (metres / second)")]
        [Tooltip("Walk speed.")]
        public float moveSpeed = 2.0f;

        [Tooltip("Speed multiplier while holding Left Shift.")]
        public float sprintMultiplier = 3.0f;

        [Tooltip("Vertical speed for Q (down) / E (up).")]
        public float verticalSpeed = 1.5f;

        [Header("Look (hold right mouse button)")]
        [Tooltip("Degrees of rotation per pixel of mouse movement.")]
        public float lookSensitivity = 0.15f;

        [Tooltip("Pitch is clamped to +/- this many degrees.")]
        public float maxPitch = 85f;

        [Header("Standing Eye Height")]
        [Tooltip("Desktop mode has no HMD to set head height, so the camera is held at this standing eye height. Matches the ~1.6 m that head-height interactions (start-platform trigger, gaze) assume. Q/E release the hold so you can adjust manually.")]
        public float eyeHeight = 1.6f;

        private float yaw;
        private float pitch;
        private bool anglesInitialized;
        private bool manualHeight; // set once the user adjusts height with Q/E
        private TrackedPoseDriver poseDriver;

        private void Update()
        {
            // Editor test tool only; in VR the HMD's TrackedPoseDriver owns the camera.
            if (!Application.isEditor || XRSettings.isDeviceActive) return;

            // The HMD bootstrap attaches a TrackedPoseDriver at runtime (even with no
            // headset); disable it in desktop mode so it doesn't overwrite our movement.
            if (poseDriver == null) poseDriver = GetComponent<TrackedPoseDriver>();
            if (poseDriver != null && poseDriver.enabled) poseDriver.enabled = false;

            Keyboard kb = Keyboard.current;
            if (kb == null) return; // no keyboard (e.g. headless) — nothing to do
            Mouse mouse = Mouse.current;

            if (!anglesInitialized)
            {
                Vector3 euler = transform.eulerAngles;
                yaw = euler.y;
                pitch = euler.x;
                anglesInitialized = true;
            }

            // --- Look: hold right mouse button and drag (scene-view style) ---
            if (mouse != null && mouse.rightButton.isPressed)
            {
                Vector2 delta = mouse.delta.ReadValue();
                yaw += delta.x * lookSensitivity;
                pitch -= delta.y * lookSensitivity;
                pitch = Mathf.Clamp(pitch, -maxPitch, maxPitch);
                transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
            }

            // --- Move: WASD on the horizontal plane, relative to where we're facing ---
            Vector3 forward = transform.forward; forward.y = 0f; forward.Normalize();
            Vector3 right = transform.right; right.y = 0f; right.Normalize();

            Vector3 move = Vector3.zero;
            if (kb.wKey.isPressed) move += forward;
            if (kb.sKey.isPressed) move -= forward;
            if (kb.dKey.isPressed) move += right;
            if (kb.aKey.isPressed) move -= right;
            move = Vector3.ClampMagnitude(move, 1f) * (moveSpeed * (kb.leftShiftKey.isPressed ? sprintMultiplier : 1f));

            // Vertical: E up, Q down. Manual vertical input releases the
            // eye-height hold below.
            if (kb.eKey.isPressed || kb.qKey.isPressed) manualHeight = true;
            if (kb.eKey.isPressed) move.y += verticalSpeed;
            if (kb.qKey.isPressed) move.y -= verticalSpeed;

            transform.position += move * Time.deltaTime;

            // No HMD to set head height in desktop mode: hold the camera at a
            // standing eye height so head-height interactions (e.g. the start
            // platform's head-height trigger) line up with the VR assumption.
            // Re-asserted each frame so the runtime-attached TrackedPoseDriver
            // can't sink the camera to floor level; released once the user
            // adjusts height manually with Q/E.
            if (!manualHeight)
            {
                Vector3 held = transform.position;
                held.y = eyeHeight;
                transform.position = held;
            }
        }
    }
}
