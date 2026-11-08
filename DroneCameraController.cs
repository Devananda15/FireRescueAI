// FireRescueAI — DroneCameraController.cs
// ----------------------------------------------------------
// Manages two camera modes:
//
//  THIRD-PERSON (default):
//    A camera floats behind and slightly above the drone.
//    It smoothly follows the drone using Lerp so it feels
//    cinematic rather than rigidly attached.
//    Toggle with [F].
//
//  FIRST-PERSON:
//    The camera is parented to the drone's front, pointing
//    forward. Gives an immersive pilot view.
//    Toggle back with [F].
//
// The camera objects themselves are created by SceneBootstrapper
// and assigned here. This script does not create cameras itself
// so you can later swap in Cinemachine virtual cameras (Mode A)
// without touching this script — just replace the Camera
// references.
// ----------------------------------------------------------

using UnityEngine;
using FireRescueAI.Drone;

namespace FireRescueAI.Drone
{
    public class DroneCameraController : MonoBehaviour
    {
        // ── Inspector Assignments ─────────────────────────────
        [Header("Camera References (set by SceneBootstrapper)")]
        [Tooltip("The third-person follow camera.")]
        public Camera thirdPersonCamera;

        [Tooltip("The first-person camera mounted on the drone.")]
        public Camera firstPersonCamera;

        [Tooltip("The drone Transform to follow.")]
        public Transform target;

        [Header("Third-Person Follow Settings")]
        [Tooltip("How far behind the drone the camera sits.")]
        public float followDistance = 3.5f;

        [Tooltip("How high above the drone the camera is positioned.")]
        public float followHeight = 1.5f;

        [Tooltip("How quickly the camera catches up to the drone (higher = snappier).")]
        public float followSpeed = 6f;

        [Tooltip("How quickly the camera rotates to look at the drone.")]
        public float lookSpeed = 8f;

        // ── Public State ──────────────────────────────────────
        public bool IsFirstPerson { get; private set; }

        // ── Private ───────────────────────────────────────────
        private DroneInput _input;

        // ── Lifecycle ─────────────────────────────────────────

        private void Awake()
        {
            // Get DroneInput from the target drone (if it exists at Awake time)
            // We also try in Start in case target is assigned after Awake.
            TryFindInput();
        }

        private void Start()
        {
            TryFindInput();
            if (this.enabled)
            {
                SetMode(isFirstPerson: false);
            }
            else
            {
                if (thirdPersonCamera != null) thirdPersonCamera.enabled = false;
                if (firstPersonCamera != null) firstPersonCamera.gameObject.SetActive(false);
            }
        }

        private void TryFindInput()
        {
            if (target != null && _input == null)
                _input = target.GetComponent<DroneInput>();
        }

        private void Update()
        {
            // Re-check in case target was assigned after Start
            if (_input == null) TryFindInput();
            // NOTE: F-key camera toggle is now handled by SceneBootstrapper
            // to coordinate between TopDownCamera and TPS camera globally.
        }

        private void LateUpdate()
        {
            // LateUpdate runs after FixedUpdate + all scripts,
            // so the drone position is final before we move the camera.
            if (IsFirstPerson || target == null || thirdPersonCamera == null) return;

            UpdateThirdPersonCamera();
        }

        private void UpdateThirdPersonCamera()
        {
            // Desired position: behind drone (in drone's -forward) and above
            Vector3 desiredPosition =
                target.position
                - target.forward * followDistance
                + Vector3.up * followHeight;

            // Smooth position
            thirdPersonCamera.transform.position = Vector3.Lerp(
                thirdPersonCamera.transform.position,
                desiredPosition,
                Time.deltaTime * followSpeed);

            // Look at a point slightly above the drone's pivot
            Vector3 lookTarget = target.position + Vector3.up * 0.4f;
            Quaternion desiredRotation = Quaternion.LookRotation(
                lookTarget - thirdPersonCamera.transform.position);

            thirdPersonCamera.transform.rotation = Quaternion.Slerp(
                thirdPersonCamera.transform.rotation,
                desiredRotation,
                Time.deltaTime * lookSpeed);
        }

        // ── Public API ────────────────────────────────────────

        /// Switch between first-person and third-person camera.
        public void SetMode(bool isFirstPerson)
        {
            IsFirstPerson = isFirstPerson;

            // Enable/disable the Camera COMPONENT only (not the GameObject)
            // so the DroneCameraController (which may share the TPS GameObject) stays active.
            if (thirdPersonCamera != null)
            {
                thirdPersonCamera.targetDisplay = 0;
                thirdPersonCamera.enabled = this.enabled && !isFirstPerson;
            }

            if (firstPersonCamera != null)
            {
                firstPersonCamera.targetDisplay = 0;
                firstPersonCamera.gameObject.SetActive(this.enabled && isFirstPerson);
            }
        }

        /// Enable or disable this entire camera system.
        /// Called by SceneBootstrapper when toggling between TopDown and TPS.
        public void SetActive(bool active)
        {
            this.enabled = active;
            if (thirdPersonCamera != null)
            {
                thirdPersonCamera.targetDisplay = 0;
                thirdPersonCamera.enabled = active && !IsFirstPerson;
            }
            if (firstPersonCamera != null)
            {
                firstPersonCamera.targetDisplay = 0;
                firstPersonCamera.gameObject.SetActive(active && IsFirstPerson);
            }
        }

        /// Returns the currently active camera.
        public Camera ActiveCamera
            => IsFirstPerson ? firstPersonCamera : thirdPersonCamera;
    }
}
