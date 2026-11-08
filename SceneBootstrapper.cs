// FireRescueAI — SceneBootstrapper.cs
// ----------------------------------------------------------
// The central startup orchestrator for FireRescue AI.
//
// WHAT IT DOES AT STARTUP (in order):
//   1.  Disables the default Unity camera (we create our own)
//   2.  Reads the GameData ScriptableObject (mode, duration, etc.)
//   3.  Generates the indoor building layout (rooms, walls, exits)
//   4.  Creates the drone (visuals, controller, physics, AI agent)
//   5a. Sets up the Top-Down orthographic camera (default)
//   5b. Sets up the Third-Person follow camera (toggle with F)
//   5c. Sets up the Drone POV PiP camera
//   6.  Creates ambient and room point lighting
//   7.  Spawns victims with dynamic behaviors (Panic, Calm, Hide, Flee)
//   8.  Initialises FireSystem, SmokeSystem, DangerZoneManager
//   9.  Attaches RescueMechanic to the drone
//   10. Creates SimulationManager, EpisodeLogger, TrainingHUD
//   11. Creates the HUD overlay
//   12. Starts the mission
//
// HOTKEYS:
//   F key: Toggle Camera (Top-Down ↔ Third-Person)
//   T key: Toggle Autonomous AI Mode (Manual ↔ AI)
//   R key: Reset Episode
// ----------------------------------------------------------

using System.Collections.Generic;
using UnityEngine;
using FireRescueAI.AI;
using FireRescueAI.Core;
using FireRescueAI.Building;
using FireRescueAI.Drone;
using FireRescueAI.Fire;
using FireRescueAI.Modes;
using FireRescueAI.Rescue;
using FireRescueAI.Training;
using FireRescueAI.UI;
using FireRescueAI.Utilities;
using FireRescueAI.Victims;

namespace FireRescueAI.Utilities
{
    public class SceneBootstrapper : MonoBehaviour
    {
        // ---- Inspector -------------------------------------------------------
        [Header("Required")]
        [Tooltip("Drag the GameData ScriptableObject asset here. " +
                 "If left empty, a default instance is created at runtime.")]
        public GameData gameData;

        [Header("Mission Settings Override (used only if GameData is null)")]
        public float fallbackMissionDuration = 300f;

        [Header("Building Tweaks")]
        public float wallHeight = 4f;

        // ---- Private References ----------------------------------------------
        private GameObject             _simRoot;
        private BuildingLayout         _building;
        private GameObject             _droneGO;
        private DroneController        _droneCtrl;
        private DroneInput             _droneInput;
        private DroneCollisionMonitor  _droneCollision;
        private RescueMechanic         _rescueMechanic;
        private DroneAgent             _droneAgent;

        // Cameras
        private TopDownCameraController  _topDownCam;
        private DroneCameraController    _tpsCamCtrl;
        private DronePOVCamera           _povCam;
        private Camera                   _tpsCamObj;
        private bool                     _isTopDownActive = true;

        // Systems
        private MissionTimer       _timer;
        private SimulationManager  _simManager;
        private VisualModeManager  _visualManager;
        private VictimManager      _victimManager;
        private FireSystem         _fireSystem;
        private SmokeSystem        _smokeSystem;
        private DangerZoneManager  _dangerZoneManager;
        private HUDController      _hud;
        private TrainingHUD        _trainingHud;
        private EpisodeLogger      _episodeLogger;

        // ---- Lifecycle -------------------------------------------------------

        private void Awake()
        {
            Application.runInBackground = true;
            QualitySettings.vSyncCount = 0;
            Time.fixedDeltaTime = 0.02f;
            EnsureCameraAvailable();
        }

        private void Start()
        {
            Application.runInBackground = true;
            EnsureGameData();
            EnsureCameraAvailable();

            _simRoot = new GameObject("[SimulationRoot]");

            Step1_GenerateBuilding();
            Step2_CreateDrone();
            Step3a_SetupTopDownCamera();
            Step3b_SetupThirdPersonCamera();
            Step3c_SetupDronePOVCamera();
            Step4_SetupLighting();
            Step5_SpawnVictims();
            Step6_SetupFire();
            Step7_SetupRescue();
            Step8_SetupManagers();
            Step9_SetupHUD();
            Step10_StartMission();

            Debug.Log("[SceneBootstrapper] Scene initialized. Press T to toggle Autonomous AI Mode | Press F to toggle Camera View.");
        }

        private void Update()
        {
            HandleCameraToggle();
            HandleMissionRestart();
        }

        private void LateUpdate()
        {
            EnsureAtLeastOneCameraRendering();
        }

        // ---- Step 0: Ensure GameData & Camera ---------------------------------

        private void EnsureCameraAvailable()
        {
            if (_topDownCam != null && _topDownCam.IsActive) return;

            // Search for existing camera in scene (e.g. from Scene asset)
            var existingCam = Camera.main;
            if (existingCam == null)
            {
                existingCam = Object.FindAnyObjectByType<Camera>();
            }

            if (existingCam != null)
            {
                existingCam.tag = "MainCamera";
                existingCam.enabled = true;
                existingCam.gameObject.SetActive(true);
                existingCam.targetDisplay = 0; // Display 1
                existingCam.cullingMask = -1;  // All layers
                existingCam.rect = new Rect(0f, 0f, 1f, 1f);
                existingCam.depth = 0;

                _topDownCam = existingCam.GetComponent<TopDownCameraController>();
                if (_topDownCam == null)
                {
                    _topDownCam = existingCam.gameObject.AddComponent<TopDownCameraController>();
                }
                Debug.Log($"[CameraManager] Configured existing camera '{existingCam.gameObject.name}' as primary TopDown camera (Display 1, enabled=true).");
            }
            else
            {
                var tdGO = new GameObject("Camera_TopDown");
                var cam = tdGO.AddComponent<Camera>();
                cam.tag = "MainCamera";
                cam.enabled = true;
                cam.targetDisplay = 0;
                cam.cullingMask = -1;
                cam.rect = new Rect(0f, 0f, 1f, 1f);
                cam.depth = 0;

                if (tdGO.GetComponent<AudioListener>() == null)
                {
                    tdGO.AddComponent<AudioListener>();
                }

                _topDownCam = tdGO.AddComponent<TopDownCameraController>();
                Debug.Log("[CameraManager] Created new Camera_TopDown (Display 1, enabled=true).");
            }
        }

        private void EnsureGameData()
        {
            if (gameData != null) return;

            gameData = Resources.Load<GameData>("GameData");

            if (gameData == null)
            {
                gameData = ScriptableObject.CreateInstance<GameData>();
                gameData.missionDuration = fallbackMissionDuration;
                Debug.LogWarning("[SceneBootstrapper] No GameData found in Resources. Using defaults.");
            }
        }

        private void DisableDefaultCamera()
        {
            var defaultCam = Camera.main;
            if (defaultCam != null)
                defaultCam.gameObject.SetActive(false);
        }

        // ---- Step 1: Building Layout -----------------------------------------

        private void Step1_GenerateBuilding()
        {
            var buildingGO = new GameObject("[Building]");
            buildingGO.transform.SetParent(_simRoot.transform);

            _building = buildingGO.AddComponent<BuildingLayout>();
            _building.wallHeight = wallHeight;
            _building.Generate();

            Debug.Log($"[SceneBootstrapper] Building ready: {_building.Rooms.Count} rooms, {_building.ExitPositions.Count} exits.");
        }

        // ---- Step 2: Drone Entity --------------------------------------------

        private void Step2_CreateDrone()
        {
            _droneGO = new GameObject("Drone");
            _droneGO.transform.SetParent(_simRoot.transform);
            _droneGO.transform.position = _building.DroneSpawnPosition;
            _droneGO.tag = "Player";

            // Physics Rigidbody
            var rb = _droneGO.AddComponent<Rigidbody>();
            rb.useGravity      = false;
            rb.linearDamping   = 1f;
            rb.angularDamping  = 10f;
            rb.constraints     = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
            rb.interpolation   = RigidbodyInterpolation.Interpolate;

            // Bounding Collider with zero-friction smooth physics material to prevent wall jamming
            var smoothMat = new PhysicsMaterial("DroneSmoothPhysicMat")
            {
                dynamicFriction = 0f,
                staticFriction = 0f,
                bounciness = 0.2f,
                frictionCombine = PhysicsMaterialCombine.Minimum,
                bounceCombine = PhysicsMaterialCombine.Maximum
            };
            var col = _droneGO.AddComponent<SphereCollider>();
            col.radius = 0.40f;
            col.material = smoothMat;

            // Visual mesh
            var visuals = new GameObject("DroneVisuals");
            visuals.transform.SetParent(_droneGO.transform);
            visuals.transform.localPosition = Vector3.zero;
            BuildDronePrimitiveMesh(visuals.transform);

            // Drone control and sensors
            _droneInput     = _droneGO.AddComponent<DroneInput>();
            _droneCtrl      = _droneGO.AddComponent<DroneController>();
            _droneCollision = _droneGO.AddComponent<DroneCollisionMonitor>();

#if MLAGENTS_PRESENT
            // Pre-configure ML-Agents components before DroneAgent initializes
            var bp = _droneGO.AddComponent<Unity.MLAgents.Policies.BehaviorParameters>();
            bp.BehaviorName = "DroneRescue";
            bp.BrainParameters.VectorObservationSize = 161;
            bp.BrainParameters.ActionSpec = Unity.MLAgents.Actuators.ActionSpec.MakeDiscrete(5);
            bp.BehaviorType = Unity.MLAgents.Policies.BehaviorType.HeuristicOnly;

            // Reinforcement Learning Agent MUST be added BEFORE DecisionRequester
            // so DecisionRequester's [RequireComponent(typeof(Agent))] binds to DroneAgent
            // instead of automatically adding an un-overridden base Agent instance.
            _droneAgent = _droneGO.AddComponent<DroneAgent>();
            _droneAgent.MaxStep = 1500;

            var dr = _droneGO.AddComponent<Unity.MLAgents.DecisionRequester>();
            dr.DecisionPeriod = 5;
            dr.TakeActionsBetweenDecisions = true;

            // Clean up any accidental base Agent instances if created by Unity serialization
            foreach (var a in _droneGO.GetComponents<Unity.MLAgents.Agent>())
            {
                if (a.GetType() == typeof(Unity.MLAgents.Agent))
                {
                    DestroyImmediate(a);
                }
            }
#else
            _droneAgent = _droneGO.AddComponent<DroneAgent>();
#endif

            Debug.Log("[SceneBootstrapper] Drone created at " + _building.DroneSpawnPosition);
        }

        private void BuildDronePrimitiveMesh(Transform parent)
        {
            Material darkMat  = MakeMaterial(new Color(0.12f, 0.12f, 0.14f));
            Material armMat   = MakeMaterial(new Color(0.18f, 0.18f, 0.20f));
            Material rotorMat = MakeMaterial(new Color(0.08f, 0.55f, 0.90f));

            CreatePart("Body", PrimitiveType.Cylinder, parent, Vector3.zero, new Vector3(0.55f, 0.07f, 0.55f), darkMat);

            float[] armAngles = { 45f, 135f, 225f, 315f };
            float   armRadius = 0.38f;
            float   armLen    = 0.32f;

            for (int i = 0; i < 4; i++)
            {
                float rad = armAngles[i] * Mathf.Deg2Rad;
                float ax  = Mathf.Sin(rad);
                float az  = Mathf.Cos(rad);

                var arm = CreatePart($"Arm_{i}", PrimitiveType.Cylinder, parent,
                    new Vector3(ax * armRadius * 0.5f, 0f, az * armRadius * 0.5f),
                    new Vector3(0.045f, armLen, 0.045f), armMat);
                arm.transform.localRotation = Quaternion.Euler(0f, armAngles[i], 90f);

                CreatePart($"Rotor_{i}", PrimitiveType.Cylinder, parent,
                    new Vector3(ax * armRadius, 0.04f, az * armRadius),
                    new Vector3(0.22f, 0.025f, 0.22f), rotorMat);
            }

            var ledMat = new Material(GetURPShader());
            if (ledMat.shader.name.Contains("Universal"))
            {
                ledMat.color = Color.cyan;
                ledMat.EnableKeyword("_EMISSION");
                ledMat.SetColor("_EmissionColor", Color.cyan * 3f);
            }
            else
            {
                ledMat.color = Color.cyan;
            }

            CreatePart("LED", PrimitiveType.Sphere, parent, new Vector3(0f, 0.10f, 0.18f), new Vector3(0.07f, 0.07f, 0.07f), ledMat);
        }

        private GameObject CreatePart(string name, PrimitiveType type, Transform parent, Vector3 localPos, Vector3 localScale, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent);
            go.transform.localPosition = localPos;
            go.transform.localScale    = localScale;
            var c = go.GetComponent<Collider>();
            if (c != null) Destroy(c);
            go.GetComponent<Renderer>().material = mat;
            return go;
        }

        // ---- Step 3: Cameras -------------------------------------------------

        private void Step3a_SetupTopDownCamera()
        {
            EnsureCameraAvailable();
            if (_topDownCam != null)
            {
                if (_simRoot != null) _topDownCam.transform.SetParent(_simRoot.transform);
                _topDownCam.Initialize(_droneGO.transform);
                _topDownCam.SetActive(true);
                Debug.Log($"[CameraManager] Step3a: TopDownCamera active on Display 1. Position: {_topDownCam.transform.position}, Orthographic: 17.5");
            }
        }

        private void Step3b_SetupThirdPersonCamera()
        {
            var tpsGO = new GameObject("Camera_ThirdPerson");
            if (_simRoot != null) tpsGO.transform.SetParent(_simRoot.transform);
            _tpsCamObj = tpsGO.AddComponent<Camera>();
            _tpsCamObj.clearFlags      = CameraClearFlags.SolidColor;
            _tpsCamObj.backgroundColor = new Color(0.08f, 0.08f, 0.10f);
            _tpsCamObj.fieldOfView     = 65f;
            _tpsCamObj.nearClipPlane   = 0.1f;
            _tpsCamObj.farClipPlane    = 200f;
            _tpsCamObj.targetDisplay   = 0;
            _tpsCamObj.depth           = 0;
            _tpsCamObj.cullingMask     = -1;
            _tpsCamObj.enabled         = false;

            tpsGO.transform.position = _building.DroneSpawnPosition + new Vector3(0f, 1.5f, -3f);
            tpsGO.transform.LookAt(_building.DroneSpawnPosition);

            _tpsCamCtrl = tpsGO.AddComponent<DroneCameraController>();
            _tpsCamCtrl.target            = _droneGO.transform;
            _tpsCamCtrl.thirdPersonCamera = _tpsCamObj;

            var fpsGO = new GameObject("Camera_FirstPerson");
            var fpsCam = fpsGO.AddComponent<Camera>();
            fpsCam.fieldOfView   = 80f;
            fpsCam.nearClipPlane = 0.05f;
            fpsCam.farClipPlane  = 200f;
            fpsCam.targetDisplay = 0;
            fpsCam.cullingMask   = -1;
            fpsCam.enabled       = false;
            fpsGO.transform.SetParent(_droneGO.transform);
            fpsGO.transform.localPosition = new Vector3(0f, 0.08f, 0.28f);
            fpsGO.transform.localRotation = Quaternion.identity;
            fpsGO.SetActive(false);
            _tpsCamCtrl.firstPersonCamera = fpsCam;
            _tpsCamCtrl.SetActive(false);
            Debug.Log("[CameraManager] Step3b: DroneCameraController (Third-Person / First-Person) initialized.");
        }

        private void Step3c_SetupDronePOVCamera()
        {
            var povGO = new GameObject("Camera_DronePOV");
            if (_simRoot != null) povGO.transform.SetParent(_simRoot.transform);
            _povCam = povGO.AddComponent<DronePOVCamera>();
            _povCam.Initialize(_droneGO.transform);
            _povCam.SetActive(true);
            Debug.Log("[CameraManager] Step3c: DronePOVCamera initialized as Picture-in-Picture (PiP, depth=1, Display 1).");
        }

        // ---- Step 4: Lighting ------------------------------------------------

        private void Step4_SetupLighting()
        {
            var dirLight = Object.FindFirstObjectByType<Light>();
            if (dirLight == null || dirLight.type != LightType.Directional)
            {
                var lightGO = new GameObject("DirectionalLight");
                dirLight    = lightGO.AddComponent<Light>();
                dirLight.type = LightType.Directional;
            }

            dirLight.color     = new Color(1.00f, 0.95f, 0.88f);
            dirLight.intensity = 1.5f;
            dirLight.shadows   = LightShadows.None;
            dirLight.transform.rotation = Quaternion.Euler(45f, -30f, 0f);

            RenderSettings.ambientMode  = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.50f, 0.50f, 0.55f);

            AddRoomLight(new Vector3( 0f, 3f,   0f));
            AddRoomLight(new Vector3(13f, 3f,   0f));
            AddRoomLight(new Vector3(13f, 3f,   9f));
            AddRoomLight(new Vector3(23f, 3f,   0f));
            AddRoomLight(new Vector3(13f, 3f, -14f));
        }

        private void AddRoomLight(Vector3 position)
        {
            var go = new GameObject("RoomLight");
            go.transform.SetParent(_simRoot.transform);
            go.transform.position = position;
            var lt = go.AddComponent<Light>();
            lt.type      = LightType.Point;
            lt.color     = new Color(1.0f, 0.95f, 0.85f);
            lt.intensity = 2f;
            lt.range     = 12f;
            lt.shadows   = LightShadows.None;
        }

        // ---- Step 5: Victims -------------------------------------------------

        private void Step5_SpawnVictims()
        {
            var vmGO = new GameObject("[VictimManager]");
            vmGO.transform.SetParent(_simRoot.transform);
            _victimManager = vmGO.AddComponent<VictimManager>();
            _victimManager.Initialize(_building.Rooms, _building.ExitPositions);
        }

        // ---- Step 6: Fire and Hazards ----------------------------------------

        private void Step6_SetupFire()
        {
            var fireGO = new GameObject("[FireSystem]");
            fireGO.transform.SetParent(_simRoot.transform);
            _fireSystem = fireGO.AddComponent<FireSystem>();
            _fireSystem.Initialize(_building.Rooms);

            var smokeGO = new GameObject("[SmokeSystem]");
            smokeGO.transform.SetParent(_simRoot.transform);
            _smokeSystem = smokeGO.AddComponent<SmokeSystem>();
            _smokeSystem.Initialize(_fireSystem, _building.Rooms);

            var dzGO = new GameObject("[DangerZoneManager]");
            dzGO.transform.SetParent(_simRoot.transform);
            _dangerZoneManager = dzGO.AddComponent<DangerZoneManager>();
            _dangerZoneManager.Initialize(_fireSystem, _building.Rooms);
        }

        // ---- Step 7: Rescue Mechanic -----------------------------------------

        private void Step7_SetupRescue()
        {
            _rescueMechanic = _droneGO.AddComponent<RescueMechanic>();
            _rescueMechanic.Initialize(_victimManager, _building.ExitPositions);
        }

        // ---- Step 8: System Coordination & Training HUD ----------------------

        private void Step8_SetupManagers()
        {
            // Mission Timer
            var timerGO = new GameObject("[MissionTimer]");
            timerGO.transform.SetParent(_simRoot.transform);
            _timer = timerGO.AddComponent<MissionTimer>();
            _timer.missionDuration = gameData.missionDuration;

            // Simulation Coordinator
            var simGO = new GameObject("[SimulationManager]");
            simGO.transform.SetParent(_simRoot.transform);
            _simManager = simGO.AddComponent<SimulationManager>();
            _simManager.Initialize(_timer, _building, _droneCtrl, gameData, _victimManager, _fireSystem, _smokeSystem, _dangerZoneManager);

            // Visual Mode Manager
            var visualGO = new GameObject("[VisualModeManager]");
            visualGO.transform.SetParent(_simRoot.transform);
            _visualManager = visualGO.AddComponent<VisualModeManager>();
            _visualManager.Initialize(gameData);

            // Episode Research Logger
            var logGO = new GameObject("[EpisodeLogger]");
            logGO.transform.SetParent(_simRoot.transform);
            _episodeLogger = logGO.AddComponent<EpisodeLogger>();
            _episodeLogger.policyName = "StandardPPO";

            // Connect DroneAgent to simulation systems
            if (_droneAgent != null)
            {
                _droneAgent.SetReferences(
                    _building,
                    _victimManager,
                    _fireSystem,
                    _rescueMechanic,
                    _timer,
                    _simManager,
                    _smokeSystem);
            }

            // Training HUD Overlay
            var tHudGO = new GameObject("[TrainingHUD]");
            _trainingHud = tHudGO.AddComponent<TrainingHUD>();
            _trainingHud.Initialize(_droneAgent, _victimManager, _fireSystem);
        }

        // ---- Step 9: Main HUD ------------------------------------------------

        private void Step9_SetupHUD()
        {
            var hudGO = new GameObject("[HUD]");
            _hud = hudGO.AddComponent<HUDController>();
            _hud.Initialize(
                _timer,
                _droneCtrl,
                _droneCollision,
                _simManager,
                gameData.selectedVisualMode,
                gameData,
                _fireSystem,
                _smokeSystem,
                _victimManager,
                _rescueMechanic,
                _dangerZoneManager,
                _building,
                _droneAgent);
        }

        // ---- Step 10: Start Mission ------------------------------------------

        private void Step10_StartMission()
        {
            _simManager.StartMission();
        }

        // ---- Update Helpers --------------------------------------------------

        private void HandleCameraToggle()
        {
            if (UnityEngine.InputSystem.Keyboard.current == null) return;
            if (!UnityEngine.InputSystem.Keyboard.current.fKey.wasPressedThisFrame) return;

            _isTopDownActive = !_isTopDownActive;
            if (_isTopDownActive)
            {
                // Top-Down Overview: Fullscreen Top-Down overview with Drone POV PiP in bottom-right
                _topDownCam?.SetActive(true);
                _tpsCamCtrl?.SetActive(false);
                if (_povCam != null)
                {
                    _povCam.SetFullScreen(false); // PiP
                    _povCam.SetActive(true);
                }
                Debug.Log("[CameraManager] Camera View: TOP-DOWN OVERVIEW (Main: Top-Down Overhead, PiP: Drone POV).");
            }
            else
            {
                // Drone POV / 3D Perspective: Fullscreen 3D Drone POV / TPS follow
                _topDownCam?.SetActive(false);
                _tpsCamCtrl?.SetActive(true);
                if (_povCam != null)
                {
                    _povCam.SetFullScreen(true); // Fullscreen
                    _povCam.SetActive(true);
                }
                Debug.Log("[CameraManager] Camera View: DRONE 3D PERSPECTIVE (Fullscreen Drone 3D View).");
            }

            EnsureAtLeastOneCameraRendering();
        }

        private void EnsureAtLeastOneCameraRendering()
        {
            bool topDownActive = _topDownCam != null && _topDownCam.IsActive;
            bool tpsActive     = _tpsCamObj != null && _tpsCamObj.enabled && _tpsCamObj.gameObject.activeInHierarchy;
            bool povActive     = _povCam != null && _povCam.IsActive;

            if (!topDownActive && !tpsActive && !povActive)
            {
                Debug.LogWarning("[CameraManager] Watchdog Alert: No cameras were rendering! Automatically restoring TopDownCamera.");
                if (_topDownCam != null)
                {
                    _topDownCam.SetActive(true);
                }
                else
                {
                    EnsureCameraAvailable();
                }
            }
        }

        private void HandleMissionRestart()
        {
            if (UnityEngine.InputSystem.Keyboard.current == null) return;
            if (!UnityEngine.InputSystem.Keyboard.current.rKey.wasPressedThisFrame) return;
            if (_simManager != null) _simManager.ResetEpisode();
        }

        // ---- Materials -------------------------------------------------------

        private static Shader GetURPShader()
            => Shader.Find("Universal Render Pipeline/Lit")
            ?? Shader.Find("Standard")
            ?? Shader.Find("Hidden/InternalErrorShader");

        private static Material MakeMaterial(Color color)
        {
            var mat = new Material(GetURPShader());
            mat.color = color;
            return mat;
        }
    }
}
