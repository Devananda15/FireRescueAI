// FireRescueAI — DronePOVCamera.cs
// ----------------------------------------------------------
// Manages the drone's first-person (POV) camera shown as a
// Picture-in-Picture (PiP) rectangle in one corner of the screen.
// ----------------------------------------------------------

using UnityEngine;
using UnityEngine.InputSystem;

namespace FireRescueAI.Drone
{
    public class DronePOVCamera : MonoBehaviour
    {
        public enum POVMode { Forward, Downward, Chase }

        [Header("POV Settings")]
        public Transform droneTarget;
        public POVMode   currentMode = POVMode.Forward;
        public float     chaseDistance = 3.5f;
        public float     chaseHeight   = 1.5f;
        public float     followSpeed   = 8f;

        private Camera _cam;

        // PiP viewport: bottom-right corner, 28% wide × 22% tall
        private static readonly Rect PipRect = new Rect(0.72f, 0.02f, 0.27f, 0.22f);

        public void Initialize(Transform drone)
        {
            droneTarget = drone;

            _cam = GetComponent<Camera>();
            if (_cam == null) _cam = gameObject.AddComponent<Camera>();

            // PiP viewport (normalized screen coords)
            _cam.rect          = PipRect;
            _cam.fieldOfView   = 75f;
            _cam.nearClipPlane = 0.05f;
            _cam.farClipPlane  = 200f;
            _cam.clearFlags    = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0.05f, 0.05f, 0.08f);
            _cam.depth         = 1;   // render ON TOP of the main top-down camera
            _cam.targetDisplay = 0;   // Display 1
            _cam.enabled       = true;

            // This camera renders ALL layers (including walls — it's the real 3D view)
            _cam.cullingMask = ~0;

            // Start at drone position
            if (droneTarget != null)
                transform.position = droneTarget.position + Vector3.forward * 0.3f + Vector3.up * 0.1f;
        }

        public void SetFullScreen(bool fullScreen)
        {
            if (_cam == null) _cam = GetComponent<Camera>();
            if (_cam == null) return;

            if (fullScreen)
            {
                _cam.rect = new Rect(0f, 0f, 1f, 1f);
                _cam.depth = 0;
            }
            else
            {
                _cam.rect = PipRect;
                _cam.depth = 1;
            }
        }

        public void SetActive(bool active)
        {
            if (_cam != null) _cam.enabled = active;
            this.enabled = active;
        }

        public bool IsActive => _cam != null && _cam.enabled && gameObject.activeInHierarchy;

        private void Update()
        {
            // V key: cycle POV modes (Forward ↔ Downward ↔ Chase)
            var kb = Keyboard.current;
            if (kb != null && kb.vKey.wasPressedThisFrame)
            {
                currentMode = (POVMode)(((int)currentMode + 1) % 3);
                Debug.Log($"[DronePOVCamera] Mode: {currentMode}");
            }
        }

        private void LateUpdate()
        {
            if (droneTarget == null || _cam == null || !_cam.enabled) return;

            switch (currentMode)
            {
                case POVMode.Forward:
                    // Mounted on drone nose, looking forward-and-slightly-down
                    transform.position = droneTarget.position
                        + droneTarget.forward * 0.3f
                        + Vector3.up * 0.08f;
                    transform.rotation = Quaternion.Slerp(
                        transform.rotation,
                        Quaternion.LookRotation(droneTarget.forward + Vector3.down * 0.15f),
                        Time.deltaTime * followSpeed * 2f);
                    break;

                case POVMode.Downward:
                    // Looking straight down from drone — good for navigation
                    transform.position = droneTarget.position + Vector3.up * 0.5f;
                    transform.rotation = Quaternion.Euler(85f, droneTarget.eulerAngles.y, 0f);
                    break;

                case POVMode.Chase:
                    // Third-person follow behind drone
                    Vector3 desired = droneTarget.position
                        - droneTarget.forward * chaseDistance
                        + Vector3.up * chaseHeight;
                    transform.position = Vector3.Lerp(transform.position, desired,
                                                       Time.deltaTime * followSpeed);
                    Vector3 lookAt = droneTarget.position + Vector3.up * 0.3f;
                    transform.rotation = Quaternion.Slerp(
                        transform.rotation,
                        Quaternion.LookRotation(lookAt - transform.position),
                        Time.deltaTime * followSpeed);
                    break;
            }
        }
    }
}
