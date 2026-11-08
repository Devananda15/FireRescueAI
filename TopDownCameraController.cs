// FireRescueAI — TopDownCameraController.cs
// Orthographic overhead camera that sees THROUGH walls using a culling mask.
// Walls are on Layer 8 ("Walls"). This camera ignores Layer 8 so the interior
// is always visible even though the walls block the 3D side views.
// A second PiP camera is managed by SceneBootstrapper for the drone POV.

using UnityEngine;
using UnityEngine.InputSystem;

namespace FireRescueAI.Drone
{
    public class TopDownCameraController : MonoBehaviour
    {
        [Header("Follow Settings")]
        public Transform target;
        public float followSpeed  = 6f;
        public float heightAbove  = 26f;   // high enough to see whole building

        [Header("Zoom")]
        public float minOrthoSize = 8f;
        public float maxOrthoSize = 35f;
        public float zoomSpeed    = 3f;

        [Header("Camera Modes")]
        [Tooltip("If true, camera stays centred on the entire building layout for research overview.")]
        public bool lockOnBuilding = true;
        public Vector3 buildingCenter = new Vector3(11.5f, 0f, -2.5f);

        private Camera _cam;

        public bool IsActive => _cam != null && _cam.enabled && gameObject.activeInHierarchy;

        private void Awake()
        {
            _cam = GetComponent<Camera>();
            if (_cam == null) _cam = gameObject.AddComponent<Camera>();
            _cam.targetDisplay = 0;
        }

        public void Initialize(Transform droneTarget)
        {
            target = droneTarget;

            if (_cam == null) _cam = GetComponent<Camera>();
            if (_cam == null) _cam = gameObject.AddComponent<Camera>();

            _cam.orthographic     = true;
            _cam.orthographicSize = 17.5f;       // Fits the 31m x 30m building layout with clear margins
            _cam.nearClipPlane    = 0.3f;
            _cam.farClipPlane     = 200f;
            _cam.clearFlags       = CameraClearFlags.SolidColor;
            _cam.backgroundColor  = new Color(0.07f, 0.09f, 0.07f);
            _cam.tag              = "MainCamera";
            _cam.depth            = 0;           // render before PiP
            _cam.targetDisplay    = 0;           // Display 1
            _cam.rect             = new Rect(0f, 0f, 1f, 1f);

            // Render all layers, including Layer 8 (Walls), so interior boundaries and doorways are visible
            _cam.cullingMask = -1;

            // Look straight down
            transform.rotation = Quaternion.Euler(90f, 0f, 0f);

            Vector3 startPos = lockOnBuilding
                ? new Vector3(buildingCenter.x, heightAbove, buildingCenter.z)
                : (target != null ? new Vector3(target.position.x, target.position.y + heightAbove, target.position.z)
                                  : new Vector3(buildingCenter.x, heightAbove, buildingCenter.z));
            transform.position = startPos;
        }

        public void SetActive(bool active)
        {
            if (_cam != null) _cam.enabled = active;
            this.enabled = active;
        }

        private void LateUpdate()
        {
            if (_cam == null) return;

            // Follow drone or stay centred on building overview
            Vector3 targetXZ = lockOnBuilding || target == null
                ? buildingCenter
                : new Vector3(target.position.x, 0f, target.position.z);

            Vector3 desired = new Vector3(targetXZ.x, heightAbove, targetXZ.z);
            transform.position = Vector3.Lerp(transform.position, desired,
                                              Time.deltaTime * followSpeed);

            // Scroll-wheel zoom via New Input System
            var mouse = Mouse.current;
            if (mouse != null)
            {
                float scroll = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(scroll) > 0.01f)
                {
                    _cam.orthographicSize = Mathf.Clamp(
                        _cam.orthographicSize - Mathf.Sign(scroll) * zoomSpeed,
                        minOrthoSize, maxOrthoSize);
                }
            }

            // B key: toggle lock-on-building vs follow-drone
            var kb = Keyboard.current;
            if (kb != null && kb.bKey.wasPressedThisFrame)
                lockOnBuilding = !lockOnBuilding;
        }
    }
}
