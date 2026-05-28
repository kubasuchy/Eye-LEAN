// SPDX-License-Identifier: MIT
#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;

namespace EyeLean.NavigationMaze.DebugTools
{
    /// <summary>
    /// Editor-only WASD + mouse-look locomotion for testing the maze without
    /// an HMD. Compiled out of player builds via <c>#if UNITY_EDITOR</c> so
    /// it can never ship to the HMD or contaminate the participant UI.
    ///
    /// Uses <see cref="UnityEngine.InputSystem"/> directly (Keyboard.current /
    /// Mouse.current) because Eye_lean's project sets Active Input Handling
    /// to "Input System Package (New)" only — legacy <c>Input.GetKey</c>
    /// calls silently return false in that mode.
    ///
    /// Self-disables when an XR HMD is present (head pose takes priority).
    /// Movement is applied to the GameObject this component sits on — drop
    /// it on the XR Rig root (the camera's parent), not the camera itself,
    /// so XR's own per-frame camera-pose update doesn't fight the
    /// translation.
    ///
    /// Hold right mouse button to look; WASD to move, Q/E to descend/rise,
    /// Shift to sprint. F1 toggles enable.
    /// </summary>
    [AddComponentMenu("Eye_lean/Debug/Editor Debug Locomotion (Maze)")]
    public class EditorDebugLocomotion : MonoBehaviour
    {
        [Tooltip("Meters per second when walking.")]
        [SerializeField] private float walkSpeed = 2.0f;

        [Tooltip("Meters per second when holding shift.")]
        [SerializeField] private float sprintSpeed = 5.0f;

        [Tooltip("Degrees per pixel of mouse delta when right-mouse-look is held.")]
        [SerializeField] private float mouseSensitivity = 0.15f;

        [Tooltip("Toggle this component on/off via F1.")]
        [SerializeField] private bool acceptToggleKey = true;

        [Tooltip("Where the look angles are applied. If null, uses this GameObject. Set this to the camera transform if you want look without moving the rig.")]
        [SerializeField] private Transform lookTarget;

        [Tooltip("Radius of the player capsule for wall collision. 0.3m approximates shoulder width.")]
        [SerializeField] private float collisionRadius = 0.3f;

        [Tooltip("Eye height above floor; the collision capsule's bottom is at floor level.")]
        [SerializeField] private float eyeHeight = 1.6f;

        [Tooltip("Layer mask used for wall-collision sweeps. Default (-1) checks all layers.")]
        [SerializeField] private LayerMask collisionMask = ~0;

        private float yawDeg;
        private float pitchDeg;
        private bool xrChecked;

        private void OnEnable()
        {
            if (lookTarget == null) lookTarget = transform;
            var e = lookTarget.localEulerAngles;
            yawDeg = e.y;
            pitchDeg = e.x;
            Debug.Log($"[EditorDebugLocomotion] Active on '{gameObject.name}'. WASD: move · Right-click drag: look · Q/E: vertical · Shift: sprint · F1: disable.");
        }

        private void Update()
        {
            if (!xrChecked)
            {
                xrChecked = true;
                if (XRSettings.isDeviceActive)
                {
                    Debug.Log("[EditorDebugLocomotion] XR device detected; disabling editor locomotion. The HMD owns the camera pose.");
                    enabled = false;
                    return;
                }
            }

            var kb = Keyboard.current;
            if (kb == null) return; // headless editor or input system not initialized

            if (acceptToggleKey && kb.f1Key.wasPressedThisFrame)
            {
                Debug.Log("[EditorDebugLocomotion] Toggled off via F1; re-enable from the GameObject inspector.");
                enabled = false;
                return;
            }

            HandleLook();
            HandleMove(kb);
        }

        private void HandleLook()
        {
            var mouse = Mouse.current;
            if (mouse == null || !mouse.rightButton.isPressed) return;
            // Mouse delta is in pixels/frame; scale by sensitivity. The
            // historical 30× factor in the previous version was tuned for
            // Input.GetAxisRaw("Mouse X") which is already smoothed; raw
            // pixel deltas are more sensitive, so the base sensitivity is
            // smaller (0.15 vs 0.2) for the same feel.
            Vector2 delta = mouse.delta.ReadValue();
            yawDeg += delta.x * mouseSensitivity;
            pitchDeg -= delta.y * mouseSensitivity;
            pitchDeg = Mathf.Clamp(pitchDeg, -89f, 89f);
            lookTarget.localRotation = Quaternion.Euler(pitchDeg, yawDeg, 0f);
        }

        private void HandleMove(Keyboard kb)
        {
            bool sprint = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
            float speed = sprint ? sprintSpeed : walkSpeed;

            Vector3 fwd = lookTarget.forward;
            Vector3 right = lookTarget.right;
            // Flatten so WASD doesn't tilt the rig when looking up/down.
            fwd.y = 0f; right.y = 0f;
            fwd.Normalize(); right.Normalize();

            Vector3 v = Vector3.zero;
            if (kb.wKey.isPressed) v += fwd;
            if (kb.sKey.isPressed) v -= fwd;
            if (kb.dKey.isPressed) v += right;
            if (kb.aKey.isPressed) v -= right;
            if (kb.eKey.isPressed) v += Vector3.up;
            if (kb.qKey.isPressed) v -= Vector3.up;

            if (v.sqrMagnitude > 0f)
            {
                Vector3 move = v.normalized * speed * Time.deltaTime;
                MoveWithCollision(move);
            }
        }

        // Capsule sweep along the intended motion vector. If the sweep hits
        // a non-trigger collider, slide along the surface (project remaining
        // motion onto the wall plane). Q/E (vertical) motion is unaffected
        // because we sweep horizontally first, then apply vertical directly.
        private void MoveWithCollision(Vector3 move)
        {
            float verticalY = move.y;
            Vector3 horizontal = new Vector3(move.x, 0f, move.z);

            if (horizontal.sqrMagnitude > 0f)
            {
                horizontal = SweepAxis(horizontal);
                // Second pass for sliding: try the remaining motion
                // perpendicular to the first hit normal so the user grazes
                // walls instead of stopping dead.
                horizontal = SweepAxis(horizontal);
            }

            transform.position += new Vector3(horizontal.x, verticalY, horizontal.z);
        }

        private Vector3 SweepAxis(Vector3 motion)
        {
            float dist = motion.magnitude;
            if (dist <= 1e-5f) return motion;
            Vector3 dir = motion / dist;
            // Capsule centered at transform.position (rig at eye height post-
            // teleport). Half-height extends downward to chest and upward to
            // just above head so the capsule overlaps walls (0-2.4m typically)
            // across most of its span. Pre-fix bug: capsule was above the
            // rig position, sitting above most walls and missing collision.
            float halfH = Mathf.Max(0.1f, (eyeHeight * 0.5f) - collisionRadius);
            Vector3 p0 = transform.position - Vector3.up * halfH;
            Vector3 p1 = transform.position + Vector3.up * halfH;
            if (Physics.CapsuleCast(p0, p1, collisionRadius, dir, out RaycastHit hit,
                                    dist, collisionMask, QueryTriggerInteraction.Ignore))
            {
                // Move up to just before the hit, then return the component
                // of remaining motion along the hit's surface plane.
                float safe = Mathf.Max(0f, hit.distance - 0.01f);
                Vector3 advance = dir * safe;
                Vector3 remaining = motion - advance;
                Vector3 slide = Vector3.ProjectOnPlane(remaining, hit.normal);
                return advance + slide;
            }
            return motion;
        }
    }
}
#endif
