// FireRescueAI — DroneInput.cs
// ----------------------------------------------------------
// Reads player input using Unity's NEW Input System (package
// com.unity.inputsystem, already installed in this project).
//
// This script is ONLY responsible for reading hardware.
// It does NOT move the drone. DroneController reads from it.
//
// CONTROLS:
//   W / S        — Move forward / backward
//   A / D        — Strafe left / right
//   Space        — Move up
//   Left Ctrl    — Move down
//   Left Shift   — Faster movement (boost)
//   Q            — Yaw (rotate) left
//   E            — Yaw (rotate) right
//   F            — Toggle first-person / third-person camera
//   Escape       — Pause (future milestone)
// ----------------------------------------------------------

using UnityEngine;
using UnityEngine.InputSystem;

namespace FireRescueAI.Drone
{
    public class DroneInput : MonoBehaviour
    {
        // ── Public Read-Only Properties ───────────────────────

        /// Horizontal movement: X = strafe, Y = forward.
        /// Values are -1, 0, or 1 (digital keys).
        public Vector2 MoveInput { get; private set; }

        /// Vertical movement: +1 = ascend, -1 = descend.
        public float AltitudeInput { get; private set; }

        /// Yaw rotation: +1 = turn right, -1 = turn left.
        public float YawInput { get; private set; }

        /// True for one frame when the player presses F.
        public bool ToggleCameraThisFrame { get; private set; }

        /// True for one frame when the player presses T (mode toggle).
        public bool ToggleModeThisFrame { get; private set; }

        /// True while Left Shift is held (speed boost).
        public bool IsBoost { get; private set; }

        /// True while the player holds W (used for rotor spin animation).
        public bool IsThrusting =>
            MoveInput.magnitude > 0.01f || Mathf.Abs(AltitudeInput) > 0.01f;

        // ── Private ───────────────────────────────────────────
        private Keyboard _keyboard;
        private bool     _prevF;   // previous frame F state, for edge detection
        private bool     _prevT;   // previous frame T state, for edge detection

        // ── Lifecycle ─────────────────────────────────────────

        private void Awake()
        {
            // Grab the current keyboard device.
            // If a gamepad is connected we can extend this later.
            _keyboard = Keyboard.current;
        }

        private void Update()
        {
            // Re-query in case the device disconnected and reconnected.
            if (_keyboard == null) _keyboard = Keyboard.current;
            if (_keyboard == null)
            {
                MoveInput     = Vector2.zero;
                AltitudeInput = 0f;
                YawInput      = 0f;
                ToggleCameraThisFrame = false;
                return;
            }

            // ── Horizontal movement ───────────────────────────
            float moveX = 0f;
            float moveY = 0f;

            if (_keyboard.dKey.isPressed) moveX += 1f;
            if (_keyboard.aKey.isPressed) moveX -= 1f;
            if (_keyboard.wKey.isPressed) moveY += 1f;
            if (_keyboard.sKey.isPressed) moveY -= 1f;

            MoveInput = new Vector2(moveX, moveY);

            // ── Vertical (altitude) ───────────────────────────
            float alt = 0f;
            if (_keyboard.spaceKey.isPressed)    alt += 1f;
            if (_keyboard.leftCtrlKey.isPressed) alt -= 1f;
            AltitudeInput = alt;

            // ── Boost (Shift = faster movement) ──────────────
            IsBoost = _keyboard.leftShiftKey.isPressed;

            // ── Yaw ───────────────────────────────────────────
            float yaw = 0f;
            if (_keyboard.eKey.isPressed) yaw += 1f;
            if (_keyboard.qKey.isPressed) yaw -= 1f;
            YawInput = yaw;

            // ── Camera toggle (rising edge only) ─────────────
            bool fNow = _keyboard.fKey.isPressed;
            ToggleCameraThisFrame = fNow && !_prevF;
            _prevF = fNow;

            // ── Mode toggle (T key rising edge) ───────────────
            bool tNow = _keyboard.tKey.isPressed;
            ToggleModeThisFrame = tNow && !_prevT;
            _prevT = tNow;
        }
    }
}
