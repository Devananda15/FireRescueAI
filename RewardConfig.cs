// FireRescueAI – RewardConfig.cs
// ============================================================
// ScriptableObject that holds all configurable reward values.
// Create via: Assets → Create → FireRescueAI → RewardConfig
// Assign to DroneAgent in the Inspector (or it uses defaults).
// ============================================================

using UnityEngine;

namespace FireRescueAI.Training
{
    [CreateAssetMenu(menuName = "FireRescueAI/RewardConfig", fileName = "RewardConfig")]
    public class RewardConfig : ScriptableObject
    {
        [Header("Positive Rewards")]
        [Tooltip("Reward for successfully picking up a victim.")]
        public float pickupReward    = 3.0f;

        [Tooltip("Reward for delivering a victim to an exit.")]
        public float deliverReward   = 10.0f;

        [Tooltip("Bonus reward when ALL victims are rescued (episode win).")]
        public float winBonus        = 20.0f;

        [Tooltip("Reward per step when moving toward the current target.")]
        public float distanceShaping = 0.5f;

        [Tooltip("Reward for first-visit to a previously unexplored cell.")]
        public float explorationBonus = 0.05f;

        [Header("Negative Rewards (Penalties)")]
        [Tooltip("Penalty per time step (encourages fast rescue).")]
        public float timePenalty     = -0.01f;

        [Tooltip("Penalty when drone enters fire (episode ends).")]
        public float firePenalty     = -10.0f;

        [Tooltip("Penalty on wall/obstacle collision.")]
        public float collisionPenalty = -0.5f;

        [Tooltip("Penalty for useless rescue attempt (no victim in range).")]
        public float uselessGrabPenalty = -0.05f;

        [Tooltip("Penalty multiplier for local fire risk. Penalty = riskMultiplier × fireIntensity.")]
        public float riskMultiplier  = -0.05f;

        [Tooltip("Penalty when a victim dies while not rescued.")]
        public float victimDeathPenalty = -5.0f;

        [Header("Thresholds")]
        [Tooltip("Fire intensity above which the drone is considered in danger (triggers fire penalty).")]
        public float fireDangerThreshold = 0.5f;

        [Tooltip("Sensor range for victim detection (metres). Victims outside this range are hidden.")]
        public float sensorRange = 10.5f;  // = 7 cells × 1.5 m
    }
}
