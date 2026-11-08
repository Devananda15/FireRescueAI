// FireRescueAI — DroneAgent.cs  (v2 — Research Edition)
// ============================================================
// WHAT THIS FILE DOES:
//   Implements the ML-Agents reinforcement learning agent for
//   the rescue drone. Supports both Standard PPO (feedforward)
//   and Recurrent PPO (LSTM) — the only difference between
//   them is the YAML training config. The agent code is
//   identical; ML-Agents handles the LSTM internally.
//
// OBSERVATION SPACE (161 values, partial observation):
//   [0–146]  7×7 local sensor grid (3 features/cell = 147)
//             Cell features: [obstacle_level, fire_intensity, special]
//             special: 0=empty, 0.5=victim (if within sensor range),
//                      1.0=exit marker
//   [147]    Drone X position normalised to [-1, 1]
//   [148]    Drone Z position normalised to [-1, 1]
//   [149]    Remaining victims / 3
//   [150]    Active fire nodes / 7
//   [151]    Time remaining / total mission time
//   [152]    Carrying victim (0 or 1)
//   [153]    Normalised distance to nearest in-range victim (1=unknown)
//   [154]    Normalised distance to nearest exit
//   [155–160] Reserved zeros (future expansion)
//
// ACTION SPACE (5 discrete):
//   0=North(+Z)  1=South(-Z)  2=West(-X)  3=East(+X)  4=Grab
//
// REWARD EQUATION (per step):
//   R = -0.01 (time)
//     + 0.01  (if moving toward target)
//     + 3.0   (victim pickup)
//     + 10.0  (victim delivery)
//     + 20.0  (all rescued — episode win)
//     - 10.0  (drone in fire — episode fail)
//     - 0.5   (wall collision)
//     - 0.05  (useless grab)
//     - 0.05×fireIntensity (risk penalty)
//     + 0.005 (first visit to new cell — exploration)
// ============================================================

using System.Collections.Generic;
using UnityEngine;
using FireRescueAI.Building;
using FireRescueAI.Core;
using FireRescueAI.Drone;
using FireRescueAI.Fire;
using FireRescueAI.Rescue;
using FireRescueAI.Training;
using FireRescueAI.Utilities;
using FireRescueAI.Victims;

#if MLAGENTS_PRESENT
using Unity.MLAgents;
using Unity.MLAgents.Sensors;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Policies;
#endif

namespace FireRescueAI.AI
{
#if MLAGENTS_PRESENT

    /// <summary>
    /// Reinforcement learning agent. Attached to the Drone GameObject.
    /// Manages observations, action execution, and reward calculations.
    /// </summary>
    public class DroneAgent : Agent
    {
        // ---- Configuration ---------------------------------------------------
        [Header("Configuration")]
        [Tooltip("Optional reward config asset. If null, hardcoded defaults are used.")]
        public RewardConfig rewardConfig;

        [Tooltip("Which policy is being trained/evaluated (for logging).")]
        public string policyName = "StandardPPO";

        [Tooltip("Base random seed. Incremented each episode.")]
        public int baseSeed = 42;

        // ---- System references (set via SetReferences) -----------------------
        private DroneController   _droneCtrl;
        private BuildingLayout    _building;
        private VictimManager     _victimManager;
        private FireSystem        _fireSystem;
        private SmokeSystem       _smokeSystem;
        private RescueMechanic    _rescue;
        private MissionTimer      _timer;
        private SimulationManager _simManager;
        private EpisodeLogger     _logger;
        private DroneCollisionMonitor _collision;

        // ---- Observation constants -------------------------------------------
        private const int   GRID_HALF   = 3;    // 7×7 window
        private const float GRID_STEP   = 1.5f; // metres per cell
        private const int   WALL_LAYER  = 8;
        private const int   OBS_TOTAL   = 161;

        // ---- Episode bookkeeping ---------------------------------------------
        private int     _prevVictimsRescued;
        private float   _prevDistToTarget;
        private int     _episodeSeed;
        private Vector3 _prevPosition;
        private bool    _uavSurvivedEpisode;

        private readonly HashSet<Vector2Int> _visitedCells = new();
        private readonly HashSet<int>        _detectedVictimIds = new();
        private readonly HashSet<int>        _visitedRoomIds = new();

        public ControlMode CurrentControlMode { get; private set; } = ControlMode.Manual;
        public bool IsTrainingMode => CurrentControlMode != ControlMode.Manual;
        public string CurrentSubtask { get; private set; } = "Manual Flight (Standby)";
        public string ActiveControllerName => _activeControllerName;

        private string _activeControllerName = "Manual Keyboard (DroneInput)";
        private float  _lastLogTime          = 0f;
        private float  _lastObsLogTime       = 0f;
        private float  _lastDiagLogTime      = 0f;

        // ---- Reward shortcuts ------------------------------------------------
        private float R_Pickup    => rewardConfig != null ? rewardConfig.pickupReward     : 3.0f;
        private float R_Deliver   => rewardConfig != null ? rewardConfig.deliverReward    : 10.0f;
        private float R_Win       => rewardConfig != null ? rewardConfig.winBonus         : 20.0f;
        private float R_Time      => rewardConfig != null ? rewardConfig.timePenalty      : -0.01f;
        private float R_Dist      => rewardConfig != null ? rewardConfig.distanceShaping  : 0.5f;
        private float R_Fire      => rewardConfig != null ? rewardConfig.firePenalty      : -10.0f;
        private float R_Col       => rewardConfig != null ? rewardConfig.collisionPenalty : -0.5f;
        private float R_Grab      => rewardConfig != null ? rewardConfig.uselessGrabPenalty: -0.05f;
        private float R_Risk      => rewardConfig != null ? rewardConfig.riskMultiplier   : -0.05f;
        private float R_Explore   => rewardConfig != null ? rewardConfig.explorationBonus : 0.05f;
        private float FireThresh  => rewardConfig != null ? rewardConfig.fireDangerThreshold : 0.5f;
        private float SensorRange => rewardConfig != null ? rewardConfig.sensorRange    : 10.5f;

        public void SetReferences(
            BuildingLayout    building,
            VictimManager     victimManager,
            FireSystem        fireSystem,
            RescueMechanic    rescue,
            MissionTimer      timer,
            SimulationManager simManager,
            SmokeSystem       smokeSystem = null)
        {
            _building      = building;
            _victimManager = victimManager;
            _fireSystem    = fireSystem;
            _rescue        = rescue;
            _timer         = timer;
            _simManager    = simManager;
            _smokeSystem   = smokeSystem;
        }

        // ---- Agent Lifecycle -------------------------------------------------

        public void SetControlMode(ControlMode mode)
        {
            CurrentControlMode = mode;
            if (_droneCtrl != null)
            {
                _droneCtrl.SetControlMode(mode);
            }

            var bp = GetComponent<BehaviorParameters>();
            var dr = GetComponent<DecisionRequester>();

            switch (mode)
            {
                case ControlMode.Manual:
                    CurrentSubtask = "Manual Flight (WASD)";
                    _activeControllerName = "Manual Keyboard (DroneInput)";
                    if (bp != null) bp.BehaviorType = BehaviorType.HeuristicOnly;
                    // Disable DecisionRequester during Manual flight so no redundant decisions or observations run
                    if (dr != null) dr.enabled = false;
                    break;

                case ControlMode.HeuristicAutonomous:
                    CurrentSubtask = "Heuristic Autonomous Search (BFS)";
                    _activeControllerName = "Autonomous BFS Controller";
                    if (bp != null) bp.BehaviorType = BehaviorType.HeuristicOnly;
                    if (dr != null) dr.enabled = true;
                    break;

                case ControlMode.StandardPPO:
                    CurrentSubtask = "Standard PPO Policy (ML-Agents)";
                    policyName = "StandardPPO";
                    _activeControllerName = "Standard PPO Policy (ML-Agents)";
                    if (_logger != null) _logger.policyName = policyName;
                    if (bp != null)
                    {
                        bp.BehaviorType = Academy.Instance.IsCommunicatorOn
                            ? BehaviorType.Default
                            : (bp.Model != null ? BehaviorType.InferenceOnly : BehaviorType.HeuristicOnly);
                    }
                    if (dr != null) dr.enabled = true;
                    break;

                case ControlMode.RecurrentPPO:
                    CurrentSubtask = "Recurrent PPO Policy with LSTM (ML-Agents)";
                    policyName = "RecurrentPPO";
                    _activeControllerName = "Recurrent PPO Policy (LSTM)";
                    if (_logger != null) _logger.policyName = policyName;
                    if (bp != null)
                    {
                        bp.BehaviorType = Academy.Instance.IsCommunicatorOn
                            ? BehaviorType.Default
                            : (bp.Model != null ? BehaviorType.InferenceOnly : BehaviorType.HeuristicOnly);
                    }
                    if (dr != null) dr.enabled = true;
                    break;
            }

            Debug.Log($"[ControlMode] Active Mode changed to: {mode} | Active Controller: {_activeControllerName} | DecisionRequester: {(dr != null && dr.enabled)}");
        }

        public override void Initialize()
        {
            _droneCtrl = GetComponent<DroneController>();
            _collision = GetComponent<DroneCollisionMonitor>();
            _logger    = FindFirstObjectByType<EpisodeLogger>();

            // Setup BehaviorParameters dynamically if missing
            var bp = GetComponent<BehaviorParameters>();
            if (bp == null) bp = gameObject.AddComponent<BehaviorParameters>();
            bp.BehaviorName = "DroneRescue";
            bp.BrainParameters.VectorObservationSize = OBS_TOTAL;
            bp.BrainParameters.ActionSpec = ActionSpec.MakeDiscrete(new int[] { 5 });

            // Setup DecisionRequester so decisions are requested regularly
            var dr = GetComponent<DecisionRequester>();
            if (dr == null) dr = gameObject.AddComponent<DecisionRequester>();
            dr.DecisionPeriod = 5;
            dr.TakeActionsBetweenDecisions = true;

            // Auto-enable training mode if Python ML-Agents communicator is active
            if (Academy.Instance.IsCommunicatorOn)
            {
                SetControlMode(ControlMode.StandardPPO);
                Debug.Log("[DroneAgent] ML-Agents Communicator active -> Autonomous Training Mode (StandardPPO) ON!");
            }
            else
            {
                SetControlMode(ControlMode.Manual);
            }

            // Fallback auto-find
            if (_building      == null) _building      = FindFirstObjectByType<BuildingLayout>();
            if (_victimManager == null) _victimManager = FindFirstObjectByType<VictimManager>();
            if (_fireSystem    == null) _fireSystem    = FindFirstObjectByType<FireSystem>();
            if (_smokeSystem   == null) _smokeSystem   = FindFirstObjectByType<SmokeSystem>();
            if (_rescue        == null) _rescue        = GetComponent<RescueMechanic>();
            if (_timer         == null) _timer         = FindFirstObjectByType<MissionTimer>();
            if (_simManager    == null) _simManager    = FindFirstObjectByType<SimulationManager>();

            if (_logger != null) _logger.policyName = policyName;
        }

        public override void OnEpisodeBegin()
        {
            _episodeSeed = baseSeed + CompletedEpisodes;
            UnityEngine.Random.InitState(_episodeSeed);

            if (_building != null && _droneCtrl != null)
                _droneCtrl.Teleport(_building.DroneSpawnPosition);

            var rb = GetComponent<Rigidbody>();
            if (rb != null) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }

            ResetAllSystems();

            _prevVictimsRescued  = 0;
            _prevDistToTarget    = float.MaxValue;
            _prevPosition        = transform.position;
            _uavSurvivedEpisode  = true;
            _visitedCells.Clear();
            _detectedVictimIds.Clear();
            _visitedRoomIds.Clear();
            _visitedRoomIds.Add(0); // Starting room (Room 0) marked as initial

            if (_logger != null) _logger.BeginEpisode(_episodeSeed);

            Debug.Log($"[DroneAgent] Episode {CompletedEpisodes + 1} | seed={_episodeSeed}");
        }

        // ---- Observations (161 total — partial observation enforced) ----------

        public override void CollectObservations(VectorSensor sensor)
        {
            Vector3 dronePos = transform.position;

            // LOCAL SENSOR WINDOW: 7×7 grid × 3 features = 147
            for (int dz = -GRID_HALF; dz <= GRID_HALF; dz++)
            {
                for (int dx = -GRID_HALF; dx <= GRID_HALF; dx++)
                {
                    Vector3 cellWorld = dronePos + new Vector3(dx * GRID_STEP, 0f, dz * GRID_STEP);

                    float obs = 0f;
                    var hits = Physics.OverlapSphere(cellWorld, GRID_STEP * 0.45f);
                    foreach (var h in hits)
                    {
                        if (h.gameObject.layer == WALL_LAYER) { obs = 1f; break; }
                        if (!h.isTrigger && h.GetComponent<Rigidbody>() == null)
                            obs = Mathf.Max(obs, 0.5f);
                    }

                    float fire = _fireSystem != null
                        ? _fireSystem.GetFireIntensityAt(cellWorld) : 0f;

                    float special = 0f;
                    float cellDistToDrone = Vector3.Distance(cellWorld, dronePos);

                    if (cellDistToDrone <= SensorRange && _victimManager != null)
                    {
                        foreach (var v in _victimManager.ActiveVictims)
                        {
                            if (v == null || !v.gameObject.activeSelf) continue;
                            if (Vector3.Distance(cellWorld, v.transform.position) < GRID_STEP * 0.6f)
                            {
                                // Detection subject to solid wall line-of-sight and smoke attenuation
                                if (CanDetectVictim(dronePos, v.transform.position))
                                {
                                    special = 0.5f;
                                    break;
                                }
                            }
                        }
                    }
                    if (_building != null)
                    {
                        foreach (var exit in _building.ExitPositions)
                        {
                            if (Vector3.Distance(cellWorld, exit) < GRID_STEP * 0.8f)
                            { special = 1.0f; break; }
                        }
                    }

                    sensor.AddObservation(obs);
                    sensor.AddObservation(fire);
                    sensor.AddObservation(special);
                }
            }

            // GLOBAL STATE (8 values: [147-154])
            sensor.AddObservation(dronePos.x / 30f);
            sensor.AddObservation(dronePos.z / 25f);
            sensor.AddObservation(_victimManager != null
                ? _victimManager.RemainingCount / Mathf.Max(1f, _victimManager.TotalVictims) : 0f);
            sensor.AddObservation(_fireSystem != null
                ? _fireSystem.ActiveFireCount / 7f : 0f);
            sensor.AddObservation(_timer != null
                ? _timer.TimeRemaining / Mathf.Max(1f, _timer.missionDuration) : 1f);
            sensor.AddObservation(_rescue != null && _rescue.IsCarryingVictim ? 1f : 0f);

            sensor.AddObservation(GetNormDistToNearestInRangeVictim());
            sensor.AddObservation(GetNormDistToNearestExit());

            // HAZARD AWARENESS & HUMAN BEHAVIOUR OBSERVATIONS (6 values: [155-160])
            // [155] Local atmospheric smoke density at drone position [0, 1]
            float localSmoke = _smokeSystem != null ? _smokeSystem.GetSmokeDensityAt(dronePos) : 0f;
            sensor.AddObservation(Mathf.Clamp01(localSmoke));

            // [156–160] Temporal & Behavioral Observations of Nearest In-Sight Victim
            VictimController nearestDet = GetNearestDetectedVictim(dronePos);
            if (nearestDet != null)
            {
                // [156] Victim Behavioral State (normalized: Calm=0.14, Panic=0.29, Flee=0.43, Hide=0.57, Trapped=0.71, Follow=0.86)
                float stateVal = ((int)nearestDet.CurrentState + 1f) / 7.0f;
                sensor.AddObservation(stateVal);

                // [157] Victim Velocity X (normalized)
                float vx = Mathf.Clamp(nearestDet.Velocity.x / 2.0f, -1.0f, 1.0f);
                sensor.AddObservation(vx);

                // [158] Victim Velocity Z (normalized)
                float vz = Mathf.Clamp(nearestDet.Velocity.z / 2.0f, -1.0f, 1.0f);
                sensor.AddObservation(vz);

                // [159] Victim Threat / Fear level [0, 1] (urgency cue for prioritization)
                sensor.AddObservation(Mathf.Clamp01(nearestDet.CurrentThreat));

                // [160] Relative Bearing Angle to Detected Victim [-1, 1]
                Vector3 toVictim = nearestDet.transform.position - dronePos;
                float bearing = Mathf.Atan2(toVictim.x, toVictim.z) / Mathf.PI;
                sensor.AddObservation(Mathf.Clamp(bearing, -1.0f, 1.0f));
            }
            else
            {
                // Unobserved / out of line-of-sight / occluded by smoke: emit neutral zeros
                sensor.AddObservation(0f); // [156] State: unobserved
                sensor.AddObservation(0f); // [157] Vx: 0
                sensor.AddObservation(0f); // [158] Vz: 0
                sensor.AddObservation(0f); // [159] Threat: 0
                sensor.AddObservation(0f); // [160] Bearing: 0
            }

            // Verification log confirming observations are populated with live simulation state
            if (Time.time - _lastObsLogTime >= 3.0f)
            {
                _lastObsLogTime = Time.time;
                Debug.Log($"[DroneAgent] CollectObservations: Successfully populated 161/161 values | ActiveMode: {CurrentControlMode} | DronePos: ({dronePos.x:F1}, {dronePos.z:F1}) | Remaining Victims: {_victimManager?.RemainingCount}");
            }
        }

        // ---- Actions & Heuristic Navigation ----------------------------------

        public override void OnActionReceived(ActionBuffers actions)
        {
            int action = actions.DiscreteActions[0];
            if (action < 0 || action > 4) action = 0;

            if (CurrentControlMode == ControlMode.Manual)
            {
                // In Manual mode, human player controls the drone via DroneInput.
                // Do not apply actions or overrides.
                return;
            }

            // Check stuck condition and apply physical separation recovery
            if (_collision != null && _collision.IsStuck)
            {
                var rb = _droneCtrl != null ? _droneCtrl.GetComponent<Rigidbody>() : GetComponent<Rigidbody>();
                Vector2 escapeDir = _collision.ExecuteRecovery(rb, transform);

                // For RL modes (StandardPPO / RecurrentPPO):
                // Apply stuck penalty (-0.2f) to discourage the policy from entering stuck states,
                // while the physical separation pulse pops the drone off the wall.
                AddReward(-0.2f);

                // If in Heuristic Autonomous mode, steer along the escape direction
                if (CurrentControlMode == ControlMode.HeuristicAutonomous && _droneCtrl != null)
                {
                    _droneCtrl.SetOverrideInput(escapeDir, 0f, 0f);
                    return;
                }
            }

            ExecuteAction(action, _activeControllerName);
            ApplyStepRewards();
        }

        private void ExecuteAction(int action, string sourceController)
        {
            Vector2 move = Vector2.zero;
            switch (action)
            {
                case 0: move = new Vector2( 0f,  1f); break; // North (+Z)
                case 1: move = new Vector2( 0f, -1f); break; // South (-Z)
                case 2: move = new Vector2(-1f,  0f); break; // West (-X)
                case 3: move = new Vector2( 1f,  0f); break; // East (+X)
                case 4: break;                              // Grab / Hover (zero horizontal move)
            }

            if (action < 4)
            {
                if (_droneCtrl != null) _droneCtrl.SetOverrideInput(move, 0f, 0f);
            }
            else
            {
                if (_droneCtrl != null) _droneCtrl.SetOverrideInput(Vector2.zero, 0f, 0f);
                ExecuteGrab();
            }

            if (Time.time - _lastDiagLogTime >= 2.0f)
            {
                _lastDiagLogTime = Time.time;
                var r = GetRoomContaining(transform.position);
                string rName = r != null ? r.roomName : "Corridor/Transit";
                int colCount = _collision != null ? _collision.CollisionCount : 0;
                int totalV = _victimManager != null && _victimManager.TotalVictims > 0 ? _victimManager.TotalVictims : 5;
                Debug.Log($"[PPO-DIAG] Mode: {CurrentControlMode} | Step: {StepCount} | Pos: ({transform.position.x:F1}, {transform.position.z:F1}) | Room: {rName} | Action: {ActionToString(action)} | Collisions: {colCount} | Rescued: {_prevVictimsRescued}/{totalV}");
            }

            ThrottledLog($"[DroneAgent] Mode: {CurrentControlMode} | Action: {ActionToString(action)} | Controller: {sourceController} | Subtask: {CurrentSubtask}");
        }

        public override void Heuristic(in ActionBuffers actionsOut)
        {
            var d = actionsOut.DiscreteActions;

            // In Heuristic Autonomous Mode, compute action via the BFS room pathfinder
            if (CurrentControlMode == ControlMode.HeuristicAutonomous)
            {
                d[0] = DetermineHeuristicAction();
                return;
            }

            // In Manual or un-connected RL modes, check player keyboard input, or default to 4 (hover)
            d[0] = GetManualAction();
        }

        private int GetManualAction()
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null)
            {
                if      (kb.wKey.isPressed) return 0; // North (+Z)
                else if (kb.sKey.isPressed) return 1; // South (-Z)
                else if (kb.aKey.isPressed) return 2; // West (-X)
                else if (kb.dKey.isPressed) return 3; // East (+X)
                else if (kb.eKey.isPressed) return 4; // Rescue Grab
            }
            return 4; // Safe default: Hover/hold position (never drives into walls when unpressed)
        }

        private RoomNode GetRoomContaining(Vector3 pos)
        {
            if (_building == null || _building.Rooms == null) return null;
            foreach (var r in _building.Rooms)
            {
                if (Mathf.Abs(pos.x - r.center.x) <= r.interiorSize.x * 0.5f + 0.35f &&
                    Mathf.Abs(pos.z - r.center.z) <= r.interiorSize.z * 0.5f + 0.35f)
                {
                    return r;
                }
            }
            RoomNode closest = null;
            float minDist = float.MaxValue;
            foreach (var r in _building.Rooms)
            {
                float d = Vector3.Distance(pos, r.center);
                if (d < minDist) { minDist = d; closest = r; }
            }
            return closest;
        }

        private Vector3 GetDoorwayPosition(RoomNode fromRoom, RoomNode toRoom)
        {
            if (fromRoom == null || toRoom == null) return Vector3.zero;
            var conn = fromRoom.connections.Find(c => c.connectedRoomId == toRoom.id);
            float y = 1.2f;
            if (conn == null) return new Vector3((fromRoom.center.x + toRoom.center.x) * 0.5f, y, (fromRoom.center.z + toRoom.center.z) * 0.5f);

            Vector3 doorCenter;
            switch (conn.wallSide)
            {
                case WallDirection.East:
                    doorCenter = new Vector3(fromRoom.center.x + fromRoom.interiorSize.x * 0.5f, y, (fromRoom.center.z + toRoom.center.z) * 0.5f);
                    break;
                case WallDirection.West:
                    doorCenter = new Vector3(fromRoom.center.x - fromRoom.interiorSize.x * 0.5f, y, (fromRoom.center.z + toRoom.center.z) * 0.5f);
                    break;
                case WallDirection.North:
                    doorCenter = new Vector3((fromRoom.center.x + toRoom.center.x) * 0.5f, y, fromRoom.center.z + fromRoom.interiorSize.z * 0.5f);
                    break;
                case WallDirection.South:
                    doorCenter = new Vector3((fromRoom.center.x + toRoom.center.x) * 0.5f, y, fromRoom.center.z - fromRoom.interiorSize.z * 0.5f);
                    break;
                default:
                    doorCenter = new Vector3((fromRoom.center.x + toRoom.center.x) * 0.5f, y, (fromRoom.center.z + toRoom.center.z) * 0.5f);
                    break;
            }

            Vector3 throughDir = (toRoom.center - fromRoom.center).normalized;
            return doorCenter + new Vector3(throughDir.x * 0.6f, 0f, throughDir.z * 0.6f);
        }

        private List<int> FindRoomPath(int startId, int targetId)
        {
            if (_building == null || _building.Rooms == null) return null;
            if (startId == targetId) return new List<int>();

            var queue = new Queue<int>();
            var visited = new HashSet<int>();
            var parent = new Dictionary<int, int>();

            queue.Enqueue(startId);
            visited.Add(startId);

            bool found = false;
            while (queue.Count > 0)
            {
                int curr = queue.Dequeue();
                if (curr == targetId) { found = true; break; }

                var roomNode = _building.Rooms.Find(r => r.id == curr);
                if (roomNode == null) continue;

                foreach (var conn in roomNode.connections)
                {
                    if (!visited.Contains(conn.connectedRoomId))
                    {
                        visited.Add(conn.connectedRoomId);
                        parent[conn.connectedRoomId] = curr;
                        queue.Enqueue(conn.connectedRoomId);
                    }
                }
            }

            if (!found) return null;

            var path = new List<int>();
            int trace = targetId;
            while (trace != startId)
            {
                path.Insert(0, trace);
                trace = parent[trace];
            }
            return path;
        }

        private int DetermineHeuristicAction()
        {
            Vector3 dronePos = transform.position;
            RoomNode currentRoom = GetRoomContaining(dronePos);
            if (currentRoom == null && _building != null && _building.Rooms.Count > 0)
                currentRoom = _building.Rooms[0];

            int selectedAction = 0;

            // ── SCENARIO A: CARRYING VICTIM -> DELIVER TO EXIT ──────────
            if (_rescue != null && _rescue.IsCarryingVictim)
            {
                Vector3 exitPos = GetNearestExitPosition();
                RoomNode exitRoom = GetRoomContaining(exitPos);

                if (currentRoom != null && exitRoom != null && currentRoom.id == exitRoom.id)
                {
                    CurrentSubtask = $"Delivering {(_rescue.CarriedVictim != null ? "Victim " + _rescue.CarriedVictim.victimId : "Victim")} at {exitRoom.roomName}";
                    selectedAction = ChooseActionTowards(exitPos, dronePos);
                }
                else
                {
                    var path = (currentRoom != null && exitRoom != null) ? FindRoomPath(currentRoom.id, exitRoom.id) : null;
                    if (path != null && path.Count > 0)
                    {
                        RoomNode nextRoom = _building.Rooms.Find(r => r.id == path[0]);
                        Vector3 doorwayPos = GetDoorwayPosition(currentRoom, nextRoom);
                        CurrentSubtask = $"Transporting victim to {exitRoom.roomName} via {nextRoom.roomName}";
                        selectedAction = ChooseActionTowards(doorwayPos, dronePos);
                    }
                    else
                    {
                        CurrentSubtask = $"Delivering victim to nearest exit";
                        selectedAction = ChooseActionTowards(exitPos, dronePos);
                    }
                }

                ThrottledLog($"[AutonomousAI] Action: {ActionToString(selectedAction)} | Subtask: {CurrentSubtask}");
                return selectedAction;
            }

            // ── SCENARIO B: UNRESCUED VICTIM IN RANGE -> GRAB ───────────
            VictimController nearestVictim = GetNearestUnrescuedVictim();
            if (nearestVictim != null)
            {
                float dist = Vector3.Distance(dronePos, nearestVictim.transform.position);
                if (dist <= 2.2f)
                {
                    CurrentSubtask = $"Executing Rescue Grab on Victim {nearestVictim.victimId}";
                    Debug.Log($"[AutonomousAI] ACTION 4: Grabbing Victim {nearestVictim.victimId} (Dist: {dist:F2}m)");
                    return 4; // Rescue Grab Action
                }
            }

            // ── SCENARIO C: DETECTED OR KNOWN TARGET VICTIM ──────────────
            VictimController detectedVictim = GetNearestDetectedVictim(dronePos);
            if (detectedVictim != null)
            {
                if (!_detectedVictimIds.Contains(detectedVictim.victimId))
                {
                    _detectedVictimIds.Add(detectedVictim.victimId);
                    RoomNode vRoom = GetRoomContaining(detectedVictim.transform.position);
                    Debug.Log($"[AutonomousAI] SENSORS DETECTED Victim {detectedVictim.victimId} in {(vRoom != null ? vRoom.roomName : "Building")} (Dist: {Vector3.Distance(dronePos, detectedVictim.transform.position):F1}m)");
                }

                RoomNode victimRoom = GetRoomContaining(detectedVictim.transform.position);
                if (currentRoom != null && victimRoom != null && currentRoom.id == victimRoom.id)
                {
                    CurrentSubtask = $"Approaching detected Victim {detectedVictim.victimId} in {currentRoom.roomName}";
                    selectedAction = ChooseActionTowards(detectedVictim.transform.position, dronePos);
                }
                else
                {
                    var path = (currentRoom != null && victimRoom != null) ? FindRoomPath(currentRoom.id, victimRoom.id) : null;
                    if (path != null && path.Count > 0)
                    {
                        RoomNode nextRoom = _building.Rooms.Find(r => r.id == path[0]);
                        Vector3 doorwayPos = GetDoorwayPosition(currentRoom, nextRoom);
                        CurrentSubtask = $"Navigating to {victimRoom.roomName} (Victim {detectedVictim.victimId}) via {nextRoom.roomName}";
                        selectedAction = ChooseActionTowards(doorwayPos, dronePos);
                    }
                    else
                    {
                        CurrentSubtask = $"Approaching Victim {detectedVictim.victimId}";
                        selectedAction = ChooseActionTowards(detectedVictim.transform.position, dronePos);
                    }
                }

                ThrottledLog($"[AutonomousAI] Action: {ActionToString(selectedAction)} | Subtask: {CurrentSubtask}");
                return selectedAction;
            }

            // ── SCENARIO D: SEARCH / EXPLORE ROOMS FOR REMAINING VICTIMS ─
            if (nearestVictim != null)
            {
                RoomNode targetRoom = GetRoomContaining(nearestVictim.transform.position);
                if (currentRoom != null && targetRoom != null && currentRoom.id == targetRoom.id)
                {
                    CurrentSubtask = $"Searching {currentRoom.roomName} for Victim {nearestVictim.victimId}";
                    selectedAction = ChooseActionTowards(nearestVictim.transform.position, dronePos);
                }
                else
                {
                    var path = (currentRoom != null && targetRoom != null) ? FindRoomPath(currentRoom.id, targetRoom.id) : null;
                    if (path != null && path.Count > 0)
                    {
                        RoomNode nextRoom = _building.Rooms.Find(r => r.id == path[0]);
                        Vector3 doorwayPos = GetDoorwayPosition(currentRoom, nextRoom);
                        CurrentSubtask = $"Searching: moving from {currentRoom.roomName} -> {nextRoom.roomName} towards {targetRoom.roomName}";
                        selectedAction = ChooseActionTowards(doorwayPos, dronePos);
                    }
                    else
                    {
                        CurrentSubtask = $"Searching for victims";
                        selectedAction = ChooseActionTowards(nearestVictim.transform.position, dronePos);
                    }
                }

                ThrottledLog($"[AutonomousAI] Action: {ActionToString(selectedAction)} | Subtask: {CurrentSubtask}");
                return selectedAction;
            }

            CurrentSubtask = "All victims rescued / Mission Area Clear";
            return 0;
        }

        private int ChooseActionTowards(Vector3 targetPos, Vector3 dronePos)
        {
            Vector3 diff = targetPos - dronePos;
            diff.y = 0f;

            if (diff.sqrMagnitude < 0.15f) return 4; // Already close enough: hover / interact

            bool zPreferred = Mathf.Abs(diff.z) >= Mathf.Abs(diff.x);

            int primaryAction = zPreferred
                ? (diff.z > 0f ? 0 : 1)   // 0 = North (+Z), 1 = South (-Z)
                : (diff.x > 0f ? 3 : 2);  // 3 = East (+X), 2 = West (-X)

            int secondaryAction = zPreferred
                ? (diff.x > 0f ? 3 : 2)
                : (diff.z > 0f ? 0 : 1);

            Vector3 primaryDir = ActionToDirection(primaryAction);
            if (!Physics.SphereCast(dronePos, 0.40f, primaryDir, out RaycastHit hit, 0.85f, 1 << WALL_LAYER))
            {
                return primaryAction;
            }

            Vector3 secondaryDir = ActionToDirection(secondaryAction);
            if (!Physics.SphereCast(dronePos, 0.40f, secondaryDir, out hit, 0.85f, 1 << WALL_LAYER))
            {
                return secondaryAction;
            }

            return primaryAction; // Fallback
        }

        private static Vector3 ActionToDirection(int action)
        {
            switch (action)
            {
                case 0: return Vector3.forward;  // North (+Z)
                case 1: return Vector3.back;     // South (-Z)
                case 2: return Vector3.left;     // West (-X)
                case 3: return Vector3.right;    // East (+X)
                default: return Vector3.zero;
            }
        }

        private static string ActionToString(int action)
        {
            switch (action)
            {
                case 0: return "North (+Z)";
                case 1: return "South (-Z)";
                case 2: return "West (-X)";
                case 3: return "East (+X)";
                case 4: return "Grab Interaction";
                default: return "Idle";
            }
        }

        private void ThrottledLog(string message)
        {
            if (Time.time - _lastLogTime >= 2.5f)
            {
                _lastLogTime = Time.time;
                Debug.Log(message);
            }
        }

        private Vector3 GetNearestExitPosition()
        {
            if (_building == null || _building.ExitPositions == null || _building.ExitPositions.Count == 0)
                return transform.position;

            Vector3 nearest = _building.ExitPositions[0];
            float minDist = Vector3.Distance(transform.position, nearest);
            for (int i = 1; i < _building.ExitPositions.Count; i++)
            {
                float d = Vector3.Distance(transform.position, _building.ExitPositions[i]);
                if (d < minDist) { minDist = d; nearest = _building.ExitPositions[i]; }
            }
            return nearest;
        }

        private VictimController GetNearestUnrescuedVictim()
        {
            if (_victimManager == null) return null;
            VictimController nearest = null;
            float minDist = float.MaxValue;
            foreach (var v in _victimManager.ActiveVictims)
            {
                if (v == null || v.CurrentState == Core.VictimState.Rescued) continue;
                float d = Vector3.Distance(transform.position, v.transform.position);
                if (d < minDist) { minDist = d; nearest = v; }
            }
            return nearest;
        }

        private VictimController GetNearestDetectedVictim(Vector3 dronePos)
        {
            if (_victimManager == null) return null;
            VictimController nearest = null;
            float minDist = float.MaxValue;
            foreach (var v in _victimManager.ActiveVictims)
            {
                if (v == null || !v.gameObject.activeSelf || v.CurrentState == Core.VictimState.Rescued) continue;
                if (CanDetectVictim(dronePos, v.transform.position))
                {
                    float d = Vector3.Distance(dronePos, v.transform.position);
                    if (d < minDist)
                    {
                        minDist = d;
                        nearest = v;
                    }
                }
            }
            return nearest;
        }

        // ---- Per-Step Reward Calculation -------------------------------------

        private void ApplyStepRewards()
        {
            AddReward(R_Time);
            _logger?.LogTimePenalty();
            _logger?.LogStep();

            float stepDist = Vector3.Distance(transform.position, _prevPosition);
            _logger?.LogStep(stepDist);
            _prevPosition = transform.position;

            // Stationary penalty: discourages hovering in place or remaining stuck
            if (stepDist < 0.04f)
            {
                AddReward(-0.015f);
            }

            // Potential-based progress reward: eliminates looping/oscillation exploit
            float distNow = GetDistanceToCurrentTarget();
            if (_prevDistToTarget < float.MaxValue * 0.5f)
            {
                float progress = Mathf.Clamp(_prevDistToTarget - distNow, -0.3f, 0.3f);
                AddReward(progress * R_Dist);
                if (progress > 0.01f) _logger?.LogDistanceReward();
            }
            _prevDistToTarget = distNow;

            var cell = WorldToCell(transform.position);
            if (!_visitedCells.Contains(cell))
            {
                _visitedCells.Add(cell);
                AddReward(R_Explore);
                _logger?.LogExploration();
            }

            int totalVictims = _victimManager != null && _victimManager.TotalVictims > 0 ? _victimManager.TotalVictims : 5;
            int rescuedNow = totalVictims - (_victimManager != null ? _victimManager.RemainingCount : 0);
            if (rescuedNow > _prevVictimsRescued)
            {
                int delta = rescuedNow - _prevVictimsRescued;
                AddReward(R_Deliver * delta);
                _logger?.LogDeliver();
                _prevDistToTarget = GetDistanceToCurrentTarget(); // reset distance baseline
                Debug.Log($"[AutonomousAI] VICTIM DELIVERED AT EXIT! Rescued: {rescuedNow}/{totalVictims} victims. Remaining: {_victimManager?.RemainingCount}");
            }
            _prevVictimsRescued = rescuedNow;

            // Room Discovery Reward: Breaks spawn room trap & guides exploration through doorways
            var curRoom = GetRoomContaining(transform.position);
            if (curRoom != null && !_visitedRoomIds.Contains(curRoom.id))
            {
                _visitedRoomIds.Add(curRoom.id);
                AddReward(1.5f);
                Debug.Log($"[DroneAgent] DISCOVERED NEW ROOM: {curRoom.roomName} (ID {curRoom.id})! Room Bonus +1.5");
            }

            // Victim Sensor Discovery Reward: Encourages finding victims in new rooms
            if (_victimManager != null)
            {
                foreach (var v in _victimManager.ActiveVictims)
                {
                    if (v == null || !v.gameObject.activeSelf || v.CurrentState == Core.VictimState.Rescued) continue;
                    if (!_detectedVictimIds.Contains(v.victimId) && CanDetectVictim(transform.position, v.transform.position))
                    {
                        _detectedVictimIds.Add(v.victimId);
                        AddReward(2.0f);
                        Debug.Log($"[DroneAgent] SENSORS DETECTED Victim {v.victimId}! Discovery Bonus +2.0");
                    }
                }
            }

            if (_collision != null && _collision.CollidedThisFrame)
            {
                AddReward(R_Col);
                _logger?.LogCollision();
            }

            if (_fireSystem != null)
            {
                float fireHere = _fireSystem.GetFireIntensityAt(transform.position);
                if (fireHere > 0f)
                {
                    AddReward(R_Risk * fireHere);
                    _logger?.LogRisk(fireHere);
                }
                if (fireHere >= FireThresh)
                {
                    AddReward(R_Fire);
                    _logger?.LogFirePenalty();
                    _uavSurvivedEpisode = false;
                    EndEpisodeWithLog(false);
                    return;
                }
            }

            if (rescuedNow >= totalVictims && totalVictims > 0)
            {
                AddReward(R_Win);
                EndEpisodeWithLog(true);
                return;
            }

            if (_timer != null && _timer.TimeRemaining <= 0f)
            {
                EndEpisodeWithLog(false);
            }
        }

        private void ExecuteGrab()
        {
            if (_rescue == null) return;

            if (_rescue.IsCarryingVictim)
            {
                AddReward(R_Grab);
                _logger?.LogUselessGrab();
                return;
            }

            bool pickedUp = _rescue.TryPickupNearest();
            if (pickedUp)
            {
                // Urgency bonus: prioritizing trapped or high-threat victims
                var carried = _rescue.CarriedVictim;
                float urgencyBonus = (carried != null && (carried.CurrentState == Core.VictimState.Trapped || carried.CurrentThreat >= 0.6f))
                    ? 1.5f : 0f;

                AddReward(R_Pickup + urgencyBonus);
                _logger?.LogPickup();
                _prevDistToTarget = GetDistanceToCurrentTarget(); // target switches to exit
                Debug.Log($"[AutonomousAI] RESCUE PICKUP SUCCESS: Secured Victim {(carried != null ? carried.victimId.ToString() : "")}");
            }
            else
            {
                AddReward(R_Grab);
                _logger?.LogUselessGrab();
            }
        }

        private void EndEpisodeWithLog(bool allRescued)
        {
            float missionTime = _timer != null
                ? _timer.missionDuration - _timer.TimeRemaining : 0f;
            int total = _victimManager != null && _victimManager.TotalVictims > 0 ? _victimManager.TotalVictims : 5;
            int rescued = total - (_victimManager != null ? _victimManager.RemainingCount : 0);

            _logger?.EndEpisode(
                GetCumulativeReward(),
                rescued,
                total,
                _uavSurvivedEpisode,
                missionTime,
                StepCount);

            EndEpisode();
        }

        private void Update()
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            bool tPressed = (kb != null && kb.tKey.wasPressedThisFrame);

            var dInput = GetComponent<DroneInput>();
            if (dInput != null && dInput.ToggleModeThisFrame)
            {
                tPressed = true;
            }

            if (tPressed)
            {
                if (CurrentControlMode == ControlMode.Manual)
                {
                    ControlMode nextMode = Academy.Instance.IsCommunicatorOn
                        ? ControlMode.StandardPPO
                        : ControlMode.HeuristicAutonomous;

                    SetControlMode(nextMode);
                }
                else
                {
                    SetControlMode(ControlMode.Manual);
                }
            }

            // Direct mode selection hotkeys (1: Manual, 2: Heuristic Autonomous, 3: Standard PPO, 4: Recurrent PPO)
            if (kb != null)
            {
                if (kb.digit1Key.wasPressedThisFrame)      SetControlMode(ControlMode.Manual);
                else if (kb.digit2Key.wasPressedThisFrame) SetControlMode(ControlMode.HeuristicAutonomous);
                else if (kb.digit3Key.wasPressedThisFrame) SetControlMode(ControlMode.StandardPPO);
                else if (kb.digit4Key.wasPressedThisFrame) SetControlMode(ControlMode.RecurrentPPO);
            }
        }

        private void FixedUpdate()
        {
            // ML-Agents' DecisionRequester automatically drives decisions every 5 physics steps
            // through Heuristic() -> OnActionReceived(), which applies actions to DroneController.
            // As a robust fail-safe: if DecisionRequester is disabled or missing while in HeuristicAutonomous,
            // drive the autonomous BFS heuristic directly at 10Hz.
            var dr = GetComponent<DecisionRequester>();
            if (CurrentControlMode == ControlMode.HeuristicAutonomous && (dr == null || !dr.enabled))
            {
                if (Time.fixedTime - _lastLogTime >= 0.1f)
                {
                    int action = DetermineHeuristicAction();
                    ExecuteAction(action, "Autonomous BFS Controller (Fail-safe)");
                    ApplyStepRewards();
                }
            }
        }

        private void ResetAllSystems()
        {
            if (_fireSystem    != null) _fireSystem.ResetAll();
            if (_smokeSystem   != null) _smokeSystem.ResetAll();
            if (_victimManager != null && _building != null)
                _victimManager.ResetAll(_building.Rooms, _building.ExitPositions);
            if (_simManager    != null) _simManager.ResetEpisode();
            if (_timer         != null) { _timer.ResetTimer(); _timer.StartTimer(); }
        }

        /// <summary>
        /// Validates direct physical sensor detection of a victim, accounting for:
        /// 1. Wall line-of-sight occlusion (cannot see through solid walls on Layer 8).
        /// 2. Smoke scattering (thick smoke reduces detection range from 10.5m down to 3.0m).
        /// </summary>
        public bool CanDetectVictim(Vector3 dronePos, Vector3 victimPos)
        {
            float dist = Vector3.Distance(dronePos, victimPos);
            if (dist > SensorRange) return false;

            // 1. Line-of-sight: solid walls (Layer 8) occlude visual/infrared detection
            Vector3 rayStart  = dronePos + Vector3.up * 0.2f;
            Vector3 rayTarget = victimPos + Vector3.up * 0.5f;
            Vector3 rayDir    = rayTarget - rayStart;
            if (Physics.Raycast(rayStart, rayDir.normalized, out RaycastHit hit, dist, 1 << WALL_LAYER))
            {
                return false;
            }

            // 2. Smoke attenuation: optical scattering degrades max detection range from 10.5m down to 3.0m
            float droneSmoke  = _smokeSystem != null ? _smokeSystem.GetSmokeDensityAt(dronePos) : 0f;
            float victimSmoke = _smokeSystem != null ? _smokeSystem.GetSmokeDensityAt(victimPos) : 0f;
            float avgSmoke    = (droneSmoke + victimSmoke) * 0.5f;

            float effectiveRange = Mathf.Lerp(SensorRange, 3.0f, avgSmoke);
            return dist <= effectiveRange;
        }

        private float GetDistanceToCurrentTarget()
        {
            if (_rescue != null && _rescue.IsCarryingVictim)
            {
                return GetRawDistToNearestExit();
            }

            // If a victim is directly detected within line-of-sight, prioritize closing in on it
            float distToDetectedVictim = GetRawDistToNearestInRangeVictim();
            if (distToDetectedVictim < SensorRange)
            {
                return distToDetectedVictim;
            }

            // Doorway guidance: when searching between rooms, guide towards the doorway to unrescued victims
            Vector3 dronePos = transform.position;
            RoomNode currentRoom = GetRoomContaining(dronePos);
            VictimController targetVictim = GetNearestUnrescuedVictim();

            if (currentRoom != null && targetVictim != null)
            {
                RoomNode targetRoom = GetRoomContaining(targetVictim.transform.position);
                if (targetRoom != null && targetRoom.id != currentRoom.id)
                {
                    var path = FindRoomPath(currentRoom.id, targetRoom.id);
                    if (path != null && path.Count > 0)
                    {
                        RoomNode nextRoom = _building != null && _building.Rooms != null ? _building.Rooms.Find(r => r.id == path[0]) : null;
                        if (nextRoom != null)
                        {
                            Vector3 doorwayPos = GetDoorwayPosition(currentRoom, nextRoom);
                            return Vector3.Distance(dronePos, doorwayPos);
                        }
                    }
                }
                else
                {
                    return Vector3.Distance(dronePos, targetVictim.transform.position);
                }
            }

            return distToDetectedVictim;
        }

        private float GetRawDistToNearestInRangeVictim()
        {
            if (_victimManager == null) return SensorRange;
            float min = SensorRange;
            foreach (var v in _victimManager.ActiveVictims)
            {
                if (v == null || !v.gameObject.activeSelf) continue;
                // Only know distance if actually detected within partial observation sensor horizon
                if (CanDetectVictim(transform.position, v.transform.position))
                {
                    float d = Vector3.Distance(transform.position, v.transform.position);
                    if (d < min) min = d;
                }
            }
            return min;
        }

        private float GetRawDistToNearestExit()
        {
            if (_building == null || _building.ExitPositions.Count == 0) return 50f;
            float min = float.MaxValue;
            foreach (var e in _building.ExitPositions)
            {
                float d = Vector3.Distance(transform.position, e);
                if (d < min) min = d;
            }
            return min;
        }

        private float GetNormDistToNearestInRangeVictim()
            => Mathf.Clamp01(GetRawDistToNearestInRangeVictim() / SensorRange);
        private float GetNormDistToNearestExit()
            => Mathf.Clamp01(GetRawDistToNearestExit() / 40f);

        private static Vector2Int WorldToCell(Vector3 pos)
            => new Vector2Int(Mathf.RoundToInt(pos.x / GRID_STEP),
                              Mathf.RoundToInt(pos.z / GRID_STEP));
    }

#else

    public class DroneAgent : MonoBehaviour
    {
        public RewardConfig rewardConfig;
        public string policyName = "StandardPPO";
        public int    baseSeed   = 42;

        public bool  IsTrainingMode    { get; private set; } = false;
        public int   CompletedEpisodes => 0;
        public float GetCumulativeReward() => 0f;

        private DroneController _droneCtrl;

        public void SetReferences(
            BuildingLayout building, VictimManager victimManager,
            FireSystem fireSystem, RescueMechanic rescue,
            MissionTimer timer, SimulationManager simManager,
            SmokeSystem smokeSystem = null)
        {
            _droneCtrl = GetComponent<DroneController>();
        }

        private void Update()
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb == null) return;
            if (kb.tKey.wasPressedThisFrame)
            {
                IsTrainingMode = !IsTrainingMode;
                if (!IsTrainingMode && _droneCtrl != null) _droneCtrl.ClearOverride();
                Debug.Log($"[DroneAgent STUB] T -> {(IsTrainingMode ? "AI" : "Manual")}");
            }
            if (!IsTrainingMode && _droneCtrl != null) _droneCtrl.ClearOverride();
        }
    }
#endif
}
