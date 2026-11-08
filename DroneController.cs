// FireRescueAI — DroneController.cs
// ----------------------------------------------------------
// Physics-based UAV movement for the rescue drone.
//
// HOW IT WORKS:
//   Every physics frame (FixedUpdate), we read from DroneInput
//   and apply forces to a Rigidbody. The Rigidbody handles
//   collisions naturally — the drone will bounce off walls.
//
// DESIGN NOTES:
//   • useGravity = false  → drone hovers; no falling
//   • High linear drag   → drone decelerates quickly when
//                          keys are released (stable hover)
//   • FreezeRotationX/Z  → drone stays upright; only yaw is
//                          allowed (simplified quadrotor)
//   • All motion is in the drone's LOCAL coordinate frame
//     for forward/back/strafe, so the drone "flies where
//     it faces" regardless of world orientation.
//
// REPLACING THE DRONE MODEL:
//   All visual children are in a separate child called
//   "DroneVisuals". You can swap that GameObject for a real
//   3D model without touching this script at all.
// ----------------------------------------------------------

using UnityEngine;
using FireRescueAI.Core;
using FireRescueAI.Utilities;

namespace FireRescueAI.Drone
{
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(DroneInput))]
    public class DroneController : MonoBehaviour
    {
        // ── Inspector Tuning ──────────────────────────────────
        [Header("Movement Forces")]
        [Tooltip("Horizontal force applied per frame when keys are held.")]
        public float horizontalForce = 5.5f;

        [Tooltip("Vertical force for ascending / descending.")]
        public float verticalForce = 5.0f;

        [Tooltip("Torque applied for yaw (rotation) inputs.")]
        public float yawTorque = 3f;

        [Tooltip("Speed multiplier when Left Shift (boost) is held.")]
        public float boostMultiplier = 2.0f;

        [Header("Speed Limits")]
        [Tooltip("Maximum horizontal speed (m/s).")]
        public float maxHorizontalSpeed = 2.2f;

        [Tooltip("Maximum vertical speed (m/s up or down).")]
        public float maxVerticalSpeed = 1.8f;

        [Tooltip("Maximum rotation speed (degrees/second).")]
        public float maxYawSpeed = 60f;

        [Header("Hover Stability")]
        [Tooltip("Damping applied when no horizontal input — makes the drone stop smoothly.")]
        public float horizontalDamping = 7f;

        [Tooltip("Damping applied when no vertical input — holds altitude.")]
        public float verticalDamping = 8f;

        // ── Public Read-Only Properties ───────────────────────
        /// Current operating control mode (Manual, HeuristicAutonomous, StandardPPO, RecurrentPPO).
        public ControlMode CurrentControlMode { get; private set; } = ControlMode.Manual;

        /// Current world speed (magnitude of velocity, m/s).
        public float Speed => _rb == null ? 0f : _rb.linearVelocity.magnitude;

        /// Current altitude above world Y=0 (roughly above floor).
        public float Altitude => transform.position.y;

        /// Horizontal speed only (ignores vertical component).
        public float HorizontalSpeed
        {
            get
            {
                if (_rb == null) return 0f;
                Vector3 h = new Vector3(_rb.linearVelocity.x, 0f, _rb.linearVelocity.z);
                return h.magnitude;
            }
        }

        // ── Private State ─────────────────────────────────────
        private Rigidbody  _rb;
        private DroneInput _input;

        // ── Override inputs (for AI / RL in Milestone 5) ──────
        // When _useOverride is true, DroneInput is ignored and
        // these values are used instead.
        private bool    _useOverride = false;
        private Vector2 _overrideMoveInput;
        private float   _overrideAltitude;
        private float   _overrideYaw;

        // ── Lifecycle ─────────────────────────────────────────

        private void Awake()
        {
            _rb    = GetComponent<Rigidbody>();
            _input = GetComponent<DroneInput>();

            // Configure rigidbody for drone physics
            _rb.useGravity     = false;
            _rb.linearDamping           = 1f;   // light drag; we apply damping manually
            _rb.angularDamping = 10f;  // strong angular drag → stops spinning fast
            _rb.constraints    =
                RigidbodyConstraints.FreezeRotationX |
                RigidbodyConstraints.FreezeRotationZ;
        }

        private void FixedUpdate()
        {
            // ── 1. Get current inputs ─────────────────────────
            Vector2 moveInput;
            float   altInput;
            float   yawInput;

            if (_useOverride || CurrentControlMode != ControlMode.Manual)
            {
                moveInput = _overrideMoveInput;
                altInput  = _overrideAltitude;
                yawInput  = _overrideYaw;
            }
            else
            {
                moveInput = _input != null ? _input.MoveInput : Vector2.zero;
                altInput  = _input != null ? _input.AltitudeInput : 0f;
                yawInput  = _input != null ? _input.YawInput : 0f;
            }

            // ── 2. Horizontal movement (Strictly World-Aligned) ──
            // moveInput: X = East (+X) / West (-X), Y = North (+Z) / South (-Z)
            // Both discrete RL actions and top-down WASD controls align directly with world coordinates
            Vector3 worldMove = new Vector3(moveInput.x, 0f, moveInput.y);
            if (worldMove.sqrMagnitude > 1f) worldMove.Normalize();

            if (worldMove.sqrMagnitude > 0.01f)
            {
                float speedMult = (_useOverride ? 1f : ((_input != null && _input.IsBoost) ? boostMultiplier : 1f));
                _rb.AddForce(worldMove * horizontalForce * speedMult, ForceMode.Acceleration);

                // Smoothly rotate visual drone body to face world movement direction
                Quaternion targetRot = Quaternion.LookRotation(worldMove, Vector3.up);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Time.fixedDeltaTime * 10f);
            }
            else
            {
                // Dampen horizontal velocity when no input (stable hover)
                Vector3 hVel = new Vector3(_rb.linearVelocity.x, 0f, _rb.linearVelocity.z);
                _rb.AddForce(-hVel * horizontalDamping, ForceMode.Acceleration);
            }

            // ── 3. Vertical movement ──────────────────────────
            if (Mathf.Abs(altInput) > 0.01f)
            {
                _rb.AddForce(Vector3.up * altInput * verticalForce, ForceMode.Acceleration);
            }
            else
            {
                // Dampen vertical velocity when no altitude input (hover hold)
                float vVel = _rb.linearVelocity.y;
                _rb.AddForce(Vector3.down * vVel * verticalDamping, ForceMode.Acceleration);
            }

            // ── 4. Yaw rotation (manual mode only) ─────────────
            if (!_useOverride && Mathf.Abs(yawInput) > 0.01f)
            {
                _rb.AddTorque(Vector3.up * yawInput * yawTorque, ForceMode.Acceleration);
            }

            // ── 5. Clamp speeds ───────────────────────────────
            ClampVelocity();

            // ── 6. Publish position event ─────────────────────
            GameEvents.PublishDroneMoved(transform.position);
        }

        private void ClampVelocity()
        {
            Vector3 vel = _rb.linearVelocity;

            // Horizontal clamp
            Vector3 hVel = new Vector3(vel.x, 0f, vel.z);
            if (hVel.magnitude > maxHorizontalSpeed)
            {
                hVel = hVel.normalized * maxHorizontalSpeed;
                vel  = new Vector3(hVel.x, vel.y, hVel.z);
            }

            // Vertical clamp
            vel.y = Mathf.Clamp(vel.y, -maxVerticalSpeed, maxVerticalSpeed);

            _rb.linearVelocity = vel;

            // Yaw speed clamp
            Vector3 av = _rb.angularVelocity;
            float   maxRadYaw = maxYawSpeed * Mathf.Deg2Rad;
            av.y = Mathf.Clamp(av.y, -maxRadYaw, maxRadYaw);
            _rb.angularVelocity = av;
        }

        // ── AI / RL Override API ──────────────────────────────
        // Call SetControlMode() to switch between Manual, Autonomous, and RL.
        // Call SetOverrideInput() to feed movement commands.
        // Call ClearOverride() to return control to the player.

        /// <summary>
        /// Explicitly sets the operating control mode.
        /// When not Manual, human keyboard inputs are completely bypassed.
        /// </summary>
        public void SetControlMode(ControlMode mode)
        {
            CurrentControlMode = mode;
            _useOverride = (mode != ControlMode.Manual);
            if (!_useOverride)
            {
                _overrideMoveInput = Vector2.zero;
                _overrideAltitude  = 0f;
                _overrideYaw       = 0f;
            }
        }

        /// <summary>
        /// Provide synthetic inputs for AI/RL control.
        /// This bypasses DroneInput completely.
        /// </summary>
        public void SetOverrideInput(Vector2 move, float altitude, float yaw)
        {
            _useOverride       = true;
            _overrideMoveInput = move;
            _overrideAltitude  = altitude;
            _overrideYaw       = yaw;
        }

        /// <summary>
        /// Return control to the human player via DroneInput.
        /// </summary>
        public void ClearOverride()
        {
            _useOverride       = false;
            CurrentControlMode = ControlMode.Manual;
            _overrideMoveInput = Vector2.zero;
            _overrideAltitude  = 0f;
            _overrideYaw       = 0f;
        }

        /// <summary>
        /// Instantly teleport the drone (used for episode reset in RL).
        /// </summary>
        public void Teleport(Vector3 worldPosition)
        {
            _rb.linearVelocity  = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
            transform.position  = worldPosition;
            transform.rotation  = Quaternion.identity;
        }
    }
}
