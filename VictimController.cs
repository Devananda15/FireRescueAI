// FireRescueAI — VictimController.cs
// Human Victim Behavioral Model with Dynamic State Machine,
// Fire/Smoke Environmental Awareness, Doorway-Aware Navigation,
// Obstacle Avoidance, and Drone Interaction (Follow / Rescue).

using System.Collections.Generic;
using UnityEngine;
using FireRescueAI.Building;
using FireRescueAI.Core;
using FireRescueAI.Drone;
using FireRescueAI.Fire;
using FireRescueAI.Utilities;

namespace FireRescueAI.Victims
{
    public class VictimController : MonoBehaviour
    {
        public int         victimId;
        public VictimState state = VictimState.Calm;
        public VictimState CurrentState => state;
        public Vector3     Velocity     { get; private set; }
        public float       CurrentThreat { get; private set; }

        private Vector3    _lastPos;

        // ---- Movement and Navigation ----
        private RoomNode         _currentRoom;
        private List<RoomNode>   _allRooms;
        private List<Vector3>    _exitPositions = new();
        private Bounds           _roomBounds;
        private Vector3          _wanderTarget;
        private float            _wanderTimer;
        private float            _moveSpeed = 0.6f;
        private Vector3          _hidePosition;
        private bool             _hideSet;
        private Transform        _droneTransform;

        // ---- Environmental Awareness ----
        private FireSystem       _fireSystem;
        private SmokeSystem      _smokeSystem;
        private float            _hazardCheckTimer;
        private const float      HAZARD_CHECK_INTERVAL = 0.35f;
        private float            _calmDownTimer;

        // ---- Visuals and UI ----
        private Renderer         _bodyRenderer;
        private TMPro.TextMeshPro _label;

        private static readonly Color[] VictimColors = {
            new Color(1f,   0.50f, 0.05f), // Orange (Victim 0)
            new Color(1f,   0.88f, 0.15f), // Yellow (Victim 1)
            new Color(0.9f, 0.25f, 0.85f), // Magenta (Victim 2)
            new Color(0.1f, 0.80f, 1.00f), // Cyan (Victim 3)
            new Color(0.3f, 0.95f, 0.35f)  // Lime Green (Victim 4)
        };

        // Trapped victims ARE rescuable! (Only rescued victims cannot be rescued again)
        public bool IsRescuable => state != VictimState.Rescued;

        private void Awake()
        {
            // Build capsule visual representation
            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "VictimBody";
            body.transform.SetParent(transform);
            body.transform.localPosition = new Vector3(0, 0.5f, 0);
            body.transform.localScale    = new Vector3(0.45f, 0.5f, 0.45f);
            Destroy(body.GetComponent<Collider>());

            // Material
            Color col = victimId < VictimColors.Length ? VictimColors[victimId] : Color.white;
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (shader != null)
            {
                var mat = new Material(shader) { color = col };
                _bodyRenderer = body.GetComponent<Renderer>();
                _bodyRenderer.material = mat;
            }

            // Parent collider for physical interaction & drone proximity
            var caps = gameObject.AddComponent<CapsuleCollider>();
            caps.height = 1.0f;
            caps.radius = 0.25f;
            caps.center = new Vector3(0, 0.5f, 0);

            // Overhead 3D World Label (always faces camera)
            var labelGO = new GameObject("Label");
            labelGO.transform.SetParent(transform);
            labelGO.transform.localPosition = new Vector3(0, 1.8f, 0);
            labelGO.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

            _label = labelGO.AddComponent<TMPro.TextMeshPro>();
            _label.text      = GetStateLabel();
            _label.fontSize  = 2.8f;
            _label.alignment = TMPro.TextAlignmentOptions.Center;
            _label.color     = Color.white;
            _label.transform.localScale = Vector3.one * 0.45f;

            _wanderTarget = transform.position;
            _hidePosition = transform.position;
        }

        private void Start()
        {
            _lastPos = transform.position;
            FindGlobalReferences();
            UpdateVisuals();
            UpdateLabel();
        }

        public void Initialize(int id, RoomNode startingRoom, List<RoomNode> allRooms, List<Vector3> exits, VictimState initialState)
        {
            victimId       = id;
            _lastPos       = transform.position;
            _currentRoom   = startingRoom;
            _allRooms      = allRooms;
            _exitPositions = exits != null ? new List<Vector3>(exits) : new List<Vector3>();

            if (startingRoom != null)
            {
                _roomBounds = startingRoom.GetInteriorBounds();
                _wanderTarget = startingRoom.center;
                _hidePosition = CalculateRoomCorner(startingRoom);
                _hideSet = true;
            }

            SetState(initialState);
            FindGlobalReferences();
            UpdateVisuals();
            UpdateLabel();
        }

        private void FindGlobalReferences()
        {
            if (_fireSystem == null)   _fireSystem   = Object.FindFirstObjectByType<FireSystem>();
            if (_smokeSystem == null)  _smokeSystem  = Object.FindFirstObjectByType<SmokeSystem>();
            if (_droneTransform == null)
            {
                var droneCtrl = Object.FindFirstObjectByType<DroneController>();
                if (droneCtrl != null) _droneTransform = droneCtrl.transform;
            }
        }

        private void Update()
        {
            if (state == VictimState.Rescued) return;

            // Track movement velocity vector
            if (Time.deltaTime > 0.0001f)
            {
                Velocity = (transform.position - _lastPos) / Time.deltaTime;
                _lastPos = transform.position;
            }

            // Keep label facing straight up for overhead camera
            if (_label != null)
                _label.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

            // 1. Periodic Environmental Threat Evaluation
            _hazardCheckTimer -= Time.deltaTime;
            if (_hazardCheckTimer <= 0f)
            {
                _hazardCheckTimer = HAZARD_CHECK_INTERVAL;
                CheckEnvironmentalHazards();
                UpdateCurrentRoom();
            }

            // 2. Execute Current Behavioral State
            UpdateStateBehaviour();

            // 3. Exit Escape Check: if in an exit room and near the exit zone, escape safely!
            CheckExitEscape();
        }

        // ──────────────────────────────────────────────────────────
        // ENVIRONMENTAL AWARENESS & DYNAMIC STATE TRANSITIONS
        // ──────────────────────────────────────────────────────────

        private void CheckEnvironmentalHazards()
        {
            float fire = _fireSystem != null ? _fireSystem.GetFireIntensityAt(transform.position) : 0f;
            float smoke = _smokeSystem != null ? _smokeSystem.GetSmokeDensityAt(transform.position) : 0f;

            // Total threat combines radiant heat (fire) and toxic smoke density
            float threat = fire * 1.0f + smoke * 0.55f;
            CurrentThreat = Mathf.Clamp01(threat);

            // Check drone proximity for human-agent interaction
            float distToDrone = _droneTransform != null
                ? Vector3.Distance(transform.position, _droneTransform.position)
                : float.MaxValue;

            // Severe Threat: Trapped by intense fire/smoke
            if (threat >= 0.70f)
            {
                if (state != VictimState.Trapped && state != VictimState.Follow)
                {
                    SetState(VictimState.Trapped);
                    Debug.Log($"[Victim {victimId+1}] Trapped by extreme hazard (fire: {fire:F2}, smoke: {smoke:F2})!");
                }
                return;
            }

            // Drone interaction: if drone is very close (< 2.8m) and victim is not trapped
            if (distToDrone <= 2.8f && (state == VictimState.Calm || state == VictimState.Panic || state == VictimState.Flee))
            {
                // Drone leads/guides victim
                if (state != VictimState.Follow)
                {
                    SetState(VictimState.Follow);
                    Debug.Log($"[Victim {victimId+1}] UAV arrived! Following drone to safety.");
                    return;
                }
            }

            // Moderate Threat: Smoke or spreading fire nearby
            if (threat >= 0.25f)
            {
                _calmDownTimer = 6f; // reset calm timer

                if (state == VictimState.Calm || state == VictimState.Hide)
                {
                    // 70% chance to flee toward safety, 30% chance to panic
                    if (Random.value < 0.70f)
                    {
                        SetState(VictimState.Flee);
                        Debug.Log($"[Victim {victimId+1}] Detected hazard ({threat:F2}) -> Fleeing toward doorway!");
                    }
                    else
                    {
                        SetState(VictimState.Panic);
                        Debug.Log($"[Victim {victimId+1}] Detected hazard ({threat:F2}) -> Panicking!");
                    }
                }
                else if (state == VictimState.Trapped && threat < 0.60f)
                {
                    // Fire intensity lowered or room partially cleared: attempt to flee
                    SetState(VictimState.Flee);
                }
            }
            else // Low threat / safe area
            {
                if (state == VictimState.Panic)
                {
                    _calmDownTimer -= HAZARD_CHECK_INTERVAL;
                    if (_calmDownTimer <= 0f)
                    {
                        SetState(VictimState.Calm);
                        Debug.Log($"[Victim {victimId+1}] Danger passed. Calmed down.");
                    }
                }
            }
        }

        private void UpdateCurrentRoom()
        {
            if (_allRooms == null) return;
            foreach (var r in _allRooms)
            {
                if (r.GetInteriorBounds().Contains(transform.position))
                {
                    if (_currentRoom != r)
                    {
                        _currentRoom = r;
                        _roomBounds  = r.GetInteriorBounds();
                    }
                    break;
                }
            }
        }

        // ──────────────────────────────────────────────────────────
        // STATE BEHAVIOURS
        // ──────────────────────────────────────────────────────────

        private void UpdateStateBehaviour()
        {
            switch (state)
            {
                case VictimState.Calm:
                    _moveSpeed = 0.55f;
                    WanderInsideRoom(3.5f, 6.0f);
                    break;

                case VictimState.Panic:
                    _moveSpeed = 1.50f;
                    // Erratic rapid movements with short intervals
                    WanderInsideRoom(0.5f, 1.2f);
                    break;

                case VictimState.Flee:
                    _moveSpeed = 1.25f;
                    ExecuteFleeNavigation();
                    break;

                case VictimState.Hide:
                    _moveSpeed = 0.40f;
                    if (!_hideSet && _currentRoom != null)
                    {
                        _hidePosition = CalculateRoomCorner(_currentRoom);
                        _hideSet = true;
                    }
                    MoveTowardWithAvoidance(_hidePosition);
                    break;

                case VictimState.Trapped:
                    // Stationary — waiting for UAV pickup
                    _moveSpeed = 0f;
                    break;

                case VictimState.Follow:
                    _moveSpeed = 1.35f;
                    ExecuteFollowDrone();
                    break;

                case VictimState.Rescued:
                    _moveSpeed = 0f;
                    break;
            }
        }

        private void WanderInsideRoom(float minInterval, float maxInterval)
        {
            _wanderTimer -= Time.deltaTime;
            if (_wanderTimer <= 0f)
            {
                _wanderTimer = Random.Range(minInterval, maxInterval);

                Bounds b = _currentRoom != null ? _currentRoom.GetInteriorBounds() : _roomBounds;
                float halfW = Mathf.Max(0.5f, b.extents.x - 0.7f);
                float halfD = Mathf.Max(0.5f, b.extents.z - 0.7f);

                _wanderTarget = new Vector3(
                    b.center.x + Random.Range(-halfW, halfW),
                    transform.position.y,
                    b.center.z + Random.Range(-halfD, halfD));
            }

            MoveTowardWithAvoidance(_wanderTarget);
        }

        private void ExecuteFleeNavigation()
        {
            // If already in an exit room, move directly to exit zone
            if (_currentRoom != null && _currentRoom.isExitRoom)
            {
                Vector3 exitPos = GetNearestExitWorldPos();
                MoveTowardWithAvoidance(exitPos);
                return;
            }

            // Otherwise, find the best doorway leading toward an exit
            Vector3 doorwayTarget = GetBestDoorwayTarget();
            MoveTowardWithAvoidance(doorwayTarget);
        }

        private void ExecuteFollowDrone()
        {
            if (_droneTransform == null)
            {
                FindGlobalReferences();
                if (_droneTransform == null) return;
            }

            Vector3 dronePos = _droneTransform.position;
            dronePos.y = transform.position.y;
            float dist = Vector3.Distance(transform.position, dronePos);

            // Follow behind drone at safe trailing distance (1.6m - 2.0m)
            if (dist > 1.8f)
            {
                MoveTowardWithAvoidance(dronePos);
            }
        }

        private void CheckExitEscape()
        {
            if (state == VictimState.Rescued) return;

            if (_currentRoom != null && _currentRoom.isExitRoom)
            {
                foreach (var exit in _exitPositions)
                {
                    if (Vector3.Distance(transform.position, exit) <= 2.2f)
                    {
                        Debug.Log($"[Victim {victimId+1}] Reached exit safely! Rescued.");
                        Rescue();
                        return;
                    }
                }
            }
        }

        // ──────────────────────────────────────────────────────────
        // DOORWAY & PATHWAY SELECTION
        // ──────────────────────────────────────────────────────────

        private Vector3 GetBestDoorwayTarget()
        {
            if (_currentRoom == null || _currentRoom.connections == null || _currentRoom.connections.Count == 0)
                return transform.position;

            Vector3 nearestExit = GetNearestExitWorldPos();
            RoomConnection bestConn = null;
            float bestScore = float.MinValue;

            foreach (var conn in _currentRoom.connections)
            {
                Vector3 doorPos = GetDoorwayWorldPos(_currentRoom, conn);
                float distToExit = Vector3.Distance(doorPos, nearestExit);

                // Check fire intensity at doorway / connected room
                float fireHazard = _fireSystem != null ? _fireSystem.GetFireIntensityAt(doorPos) : 0f;

                // Score: closer to exit is better, high fire is heavily penalised
                float score = -distToExit - (fireHazard * 25f);

                if (score > bestScore)
                {
                    bestScore = score;
                    bestConn  = conn;
                }
            }

            if (bestConn != null)
            {
                Vector3 doorPos = GetDoorwayWorldPos(_currentRoom, bestConn);

                // If very close to doorway threshold, push target slightly into adjacent room
                if (Vector3.Distance(transform.position, doorPos) < 1.2f)
                {
                    Vector3 pushDir = GetDoorwayPassDirection(bestConn.wallSide);
                    return doorPos + pushDir * 1.5f;
                }
                return doorPos;
            }

            return transform.position;
        }

        private static Vector3 GetDoorwayWorldPos(RoomNode room, RoomConnection conn)
        {
            Vector3 c = room.center;
            Vector3 s = room.interiorSize;
            float y = 0f;

            return conn.wallSide switch
            {
                WallDirection.North => new Vector3(c.x, y, c.z + s.z / 2f),
                WallDirection.South => new Vector3(c.x, y, c.z - s.z / 2f),
                WallDirection.East  => new Vector3(c.x + s.x / 2f, y, c.z),
                WallDirection.West  => new Vector3(c.x - s.x / 2f, y, c.z),
                _                   => c
            };
        }

        private static Vector3 GetDoorwayPassDirection(WallDirection side)
        {
            return side switch
            {
                WallDirection.North => Vector3.forward,
                WallDirection.South => Vector3.back,
                WallDirection.East  => Vector3.right,
                WallDirection.West  => Vector3.left,
                _                   => Vector3.forward
            };
        }

        private Vector3 GetNearestExitWorldPos()
        {
            if (_exitPositions == null || _exitPositions.Count == 0)
                return transform.position;

            Vector3 nearest = _exitPositions[0];
            float minDist = Vector3.Distance(transform.position, nearest);
            for (int i = 1; i < _exitPositions.Count; i++)
            {
                float d = Vector3.Distance(transform.position, _exitPositions[i]);
                if (d < minDist)
                {
                    minDist = d;
                    nearest = _exitPositions[i];
                }
            }
            return nearest;
        }

        private static Vector3 CalculateRoomCorner(RoomNode room)
        {
            Bounds b = room.GetInteriorBounds();
            // Pick corner furthest from doorway
            return new Vector3(b.min.x + 1.0f, 0f, b.max.z - 1.0f);
        }

        // ──────────────────────────────────────────────────────────
        // OBSTACLE AVOIDANCE & MOVEMENT
        // ──────────────────────────────────────────────────────────

        private void MoveTowardWithAvoidance(Vector3 targetPos)
        {
            targetPos.y = transform.position.y;
            Vector3 toTarget = targetPos - transform.position;
            float dist = toTarget.magnitude;
            if (dist < 0.10f) return;

            Vector3 moveDir = toTarget / dist;

            // SphereCast ahead to detect walls and obstacles
            if (Physics.SphereCast(transform.position + Vector3.up * 0.4f, 0.22f, moveDir, out RaycastHit hit, 0.7f))
            {
                // Only avoid solid obstacles (not triggers or the drone)
                if (!hit.collider.isTrigger && !hit.collider.CompareTag("Player"))
                {
                    Vector3 normal = hit.normal;
                    normal.y = 0f;
                    Vector3 slide = Vector3.ProjectOnPlane(moveDir, normal).normalized;
                    if (slide.sqrMagnitude > 0.01f)
                    {
                        moveDir = Vector3.Lerp(moveDir, slide, 0.75f).normalized;
                    }
                }
            }

            // Move
            transform.position += moveDir * (_moveSpeed * Time.deltaTime);

            // Orient body toward travel direction
            if (moveDir.sqrMagnitude > 0.01f)
            {
                Quaternion targetRot = Quaternion.LookRotation(moveDir, Vector3.up);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Time.deltaTime * 8f);
            }
        }

        // ──────────────────────────────────────────────────────────
        // PUBLIC API & RESCUE LIFECYCLE
        // ──────────────────────────────────────────────────────────

        public void SetState(VictimState newState)
        {
            if (state == newState) return;
            state = newState;
            UpdateLabel();
            GameEvents.PublishVictimStateChanged(victimId, state);
        }

        public void SetRoomBounds(Bounds b)
        {
            _roomBounds = b;
            _wanderTarget = new Vector3(
                Mathf.Clamp(transform.position.x, b.min.x + 0.5f, b.max.x - 0.5f),
                transform.position.y,
                Mathf.Clamp(transform.position.z, b.min.z + 0.5f, b.max.z - 0.5f));
        }

        public void SetNearestExitDirection(Vector3 dir)
        {
            // Maintained for backward compatibility
        }

        public void Rescue()
        {
            if (state == VictimState.Rescued) return;
            SetState(VictimState.Rescued);
            GameEvents.PublishVictimRescued(victimId);
            Invoke(nameof(Deactivate), 0.6f);
        }

        private void Deactivate() => gameObject.SetActive(false);

        private void UpdateVisuals()
        {
            if (_bodyRenderer != null)
            {
                Color col = victimId < VictimColors.Length ? VictimColors[victimId] : Color.white;
                _bodyRenderer.material.color = col;
            }
        }

        private void UpdateLabel()
        {
            if (_label == null) return;
            _label.text  = GetStateLabel();
            _label.color = state switch {
                VictimState.Trapped => new Color(1f, 0.2f, 0.2f),
                VictimState.Panic   => new Color(1f, 0.9f, 0.1f),
                VictimState.Flee    => new Color(1f, 0.55f, 0.05f),
                VictimState.Follow  => new Color(0.2f, 1f, 0.4f),
                VictimState.Hide    => new Color(0.4f, 0.7f, 1f),
                VictimState.Rescued => new Color(0.2f, 1f, 0.2f),
                _                   => Color.white
            };
        }

        private string GetStateLabel()
        {
            return state switch {
                VictimState.Calm    => $"V{victimId+1} CALM",
                VictimState.Panic   => $"V{victimId+1} PANIC!",
                VictimState.Flee    => $"V{victimId+1} FLEEING",
                VictimState.Hide    => $"V{victimId+1} HIDING",
                VictimState.Trapped => $"V{victimId+1} TRAPPED!",
                VictimState.Follow  => $"V{victimId+1} FOLLOWING",
                VictimState.Rescued => $"V{victimId+1} RESCUED",
                _                   => $"V{victimId+1}"
            };
        }
    }
}
