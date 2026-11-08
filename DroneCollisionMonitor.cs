 // FireRescueAI — DroneCollisionMonitor.cs
// ----------------------------------------------------------
// Advanced Collision & Stuck Recovery System for UAV Search and Rescue.
// Detects collisions with walls, furniture, obstacles, and tracks contact normal.
// Automatically detects when the UAV is trapped/stuck against an obstacle,
// triggers controlled safe recovery impulses without overriding the RL policy,
// and records all interventions separately for scientific evaluation.
// ----------------------------------------------------------

using UnityEngine;
using FireRescueAI.Utilities;

namespace FireRescueAI.Drone
{
    public class DroneCollisionMonitor : MonoBehaviour
    {
        // ---- Inspector Settings -----------------------------------------------
        [Header("Collision Settings")]
        [Tooltip("Any collision impulse above this force triggers a warning.")]
        public float impactThreshold = 0.4f;

        [Tooltip("Seconds after a collision before the warning clears.")]
        public float warningDuration = 1.0f;

        [Header("Stuck Detection & Recovery Settings")]
        [Tooltip("Consecutive physics steps with near-zero displacement to declare stuck.")]
        public int stuckThresholdSteps = 10;

        [Tooltip("Minimum horizontal displacement (m) required over stuck window to count as moving.")]
        public float minMeaningfulDisplacement = 0.12f;

        [Tooltip("Impulse force magnitude applied to safely separate drone from obstacle.")]
        public float recoveryImpulseForce = 3.5f;

        [Tooltip("Layer mask for solid obstacles and walls.")]
        public LayerMask obstacleLayerMask = ~0;

        // ---- Public Read-Only Properties --------------------------------------

        /// True while the drone is currently touching an obstacle.
        public bool IsColliding { get; private set; }

        /// True for exactly one frame after a collision begins (used for RL collision penalty).
        public bool CollidedThisFrame { get; private set; }

        /// Total number of collision events since mission start.
        public int CollisionCount { get; private set; }

        /// Magnitude of the most recent impact.
        public float LastImpactForce { get; private set; }

        /// World normal vector pointing away from the collided obstacle surface.
        public Vector3 LastContactNormal { get; private set; } = Vector3.up;

        /// World position of the most recent collision contact point.
        public Vector3 LastContactPoint { get; private set; }

        /// Name of the most recently collided obstacle.
        public string LastObstacleName { get; private set; } = "None";

        /// True when the drone is detected as stuck against a wall or obstacle.
        public bool IsStuck { get; private set; }

        /// Total recovery interventions triggered during the episode.
        public int TotalRecoveryInterventions { get; private set; }

        /// Successfully resolved recoveries (drone restored to free flight).
        public int SuccessfulRecoveries { get; private set; }

        /// Failed recoveries (drone still obstructed after maneuver).
        public int FailedRecoveries { get; private set; }

        // ---- Private State ---------------------------------------------------
        private float   _warningTimer;
        private int     _consecutiveStuckSteps;
        private Vector3 _stuckSamplePos;
        private float   _lastRecoveryTime = -10f;
        private bool    _awaitingRecoveryResolution;
        private Vector3 _preRecoveryPos;
        private bool    _isTouchingObstacle;

        // ---- Lifecycle -------------------------------------------------------

        private void Start()
        {
            _stuckSamplePos = transform.position;
        }

        private void FixedUpdate()
        {
            TrackStuckCondition();
        }

        private void LateUpdate()
        {
            CollidedThisFrame = false;

            if (IsColliding && _warningTimer > 0f)
            {
                _warningTimer -= Time.deltaTime;
                if (_warningTimer <= 0f)
                    IsColliding = false;
            }
        }

        // ---- Collision Callbacks ---------------------------------------------

        private void OnCollisionEnter(Collision collision)
        {
            _isTouchingObstacle = true;
            RecordCollisionContact(collision);

            float force = collision.relativeVelocity.magnitude;
            if (force >= impactThreshold)
            {
                LastImpactForce   = force;
                CollisionCount++;
                IsColliding       = true;
                CollidedThisFrame = true;
                _warningTimer     = warningDuration;

                GameEvents.PublishDroneCollision();
                Debug.Log($"[DroneCollision] Hit '{collision.gameObject.name}' with force {force:F2} (Total: {CollisionCount})");
            }
        }

        private void OnCollisionStay(Collision collision)
        {
            _isTouchingObstacle = true;
            if (IsColliding) _warningTimer = warningDuration;
            RecordCollisionContact(collision);
        }

        private void OnCollisionExit(Collision collision)
        {
            _isTouchingObstacle = false;
        }

        private void RecordCollisionContact(Collision collision)
        {
            if (collision.contactCount > 0)
            {
                ContactPoint contact = collision.GetContact(0);
                LastContactNormal = contact.normal;
                LastContactPoint  = contact.point;
                LastObstacleName  = collision.gameObject.name;
            }
        }

        // ---- Stuck Detection Logic -------------------------------------------

        private void TrackStuckCondition()
        {
            // Evaluate displacement every 5 physics steps
            if (Time.frameCount % 5 == 0)
            {
                float disp = Vector3.Distance(transform.position, _stuckSamplePos);
                _stuckSamplePos = transform.position;

                bool obstructed = _isTouchingObstacle || IsColliding;
                if (obstructed && disp < minMeaningfulDisplacement)
                {
                    _consecutiveStuckSteps += 5;
                    if (_consecutiveStuckSteps >= stuckThresholdSteps && !IsStuck)
                    {
                        IsStuck = true;
                        Debug.LogWarning($"[DroneCollisionMonitor] UAV STUCK against '{LastObstacleName}'! " +
                                         $"Displacement: {disp:F3}m over {_consecutiveStuckSteps} steps.");
                    }
                }
                else
                {
                    if (disp >= minMeaningfulDisplacement * 1.5f)
                    {
                        if (IsStuck)
                        {
                            IsStuck = false;
                            if (_awaitingRecoveryResolution)
                            {
                                SuccessfulRecoveries++;
                                _awaitingRecoveryResolution = false;
                                Debug.Log($"[DroneCollisionMonitor] Recovery SUCCESSFUL! Restored free flight. (Total successes: {SuccessfulRecoveries})");
                            }
                        }
                        _consecutiveStuckSteps = 0;
                    }
                }

                // Check for failed recovery resolution timeout (1.5 seconds)
                if (_awaitingRecoveryResolution && Time.time - _lastRecoveryTime > 1.5f)
                {
                    if (Vector3.Distance(transform.position, _preRecoveryPos) < minMeaningfulDisplacement * 2f)
                    {
                        FailedRecoveries++;
                        Debug.LogWarning($"[DroneCollisionMonitor] Recovery attempt timed out / constrained. (Total fails: {FailedRecoveries})");
                    }
                    _awaitingRecoveryResolution = false;
                }
            }
        }

        // ---- Active Recovery Maneuver ----------------------------------------

        /// <summary>
        /// Executes a physical separation impulse away from the obstacle surface,
        /// stops forward momentum into the obstacle, and returns the clearest open cardinal direction.
        /// Does NOT replace the RL policy permanently—only provides a transient escape separation.
        /// </summary>
        public Vector2 ExecuteRecovery(Rigidbody rb, Transform droneTransform)
        {
            TotalRecoveryInterventions++;
            _lastRecoveryTime           = Time.time;
            _awaitingRecoveryResolution = true;
            _preRecoveryPos             = droneTransform.position;

            // 1. Calculate pushback direction away from the collision surface normal
            Vector3 pushback = LastContactNormal;
            pushback.y = 0f;

            if (pushback.sqrMagnitude < 0.05f)
            {
                // Fallback: move backwards from drone's current facing
                pushback = -droneTransform.forward;
                pushback.y = 0f;
            }
            pushback.Normalize();

            // 2. Clear current velocity into the wall and apply separation velocity
            if (rb != null)
            {
                rb.linearVelocity = pushback * recoveryImpulseForce;
                rb.angularVelocity = Vector3.zero;
            }

            // 3. Scan 4 cardinal directions to identify the most open escape path
            Vector2 bestAltDir = FindClearestEscapeDirection(droneTransform.position);

            Debug.Log($"[DroneCollisionMonitor] INTERVENTION #{TotalRecoveryInterventions}: " +
                      $"Applied pushback ({pushback.x:F2}, {pushback.z:F2}) away from '{LastObstacleName}'. " +
                      $"Recommended escape direction: ({bestAltDir.x}, {bestAltDir.y}).");

            _consecutiveStuckSteps = 0;
            IsStuck = false;

            return bestAltDir;
        }

        /// <summary>
        /// Raycasts in 4 cardinal directions to determine which direction has the greatest obstacle-free clearance.
        /// </summary>
        public Vector2 FindClearestEscapeDirection(Vector3 dronePos)
        {
            Vector3[] directions = {
                Vector3.forward, // North (0, 1)
                Vector3.back,    // South (0, -1)
                Vector3.left,    // West  (-1, 0)
                Vector3.right    // East  (1, 0)
            };

            Vector2[] actionDirs = {
                new Vector2( 0f,  1f),
                new Vector2( 0f, -1f),
                new Vector2(-1f,  0f),
                new Vector2( 1f,  0f)
            };

            float maxClearance = -1f;
            int bestIdx = 0;

            for (int i = 0; i < 4; i++)
            {
                float clearance = 5.0f;
                if (Physics.SphereCast(dronePos, 0.40f, directions[i], out RaycastHit hit, 5.0f, obstacleLayerMask))
                {
                    clearance = hit.distance;
                }

                if (clearance > maxClearance)
                {
                    maxClearance = clearance;
                    bestIdx = i;
                }
            }

            return actionDirs[bestIdx];
        }

        // ---- Reset -----------------------------------------------------------
        public void ResetStats()
        {
            CollisionCount              = 0;
            LastImpactForce             = 0f;
            IsColliding                 = false;
            CollidedThisFrame           = false;
            IsStuck                     = false;
            _consecutiveStuckSteps      = 0;
            _warningTimer               = 0f;
            _lastRecoveryTime           = -10f;
            _awaitingRecoveryResolution = false;
            TotalRecoveryInterventions  = 0;
            SuccessfulRecoveries        = 0;
            FailedRecoveries            = 0;
            LastObstacleName            = "None";
            LastContactNormal           = Vector3.up;
        }
    }
}
