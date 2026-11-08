// FireRescueAI — GameData.cs
// ----------------------------------------------------------
// A ScriptableObject that stores the player's selected mode
// and mission settings. Because it is a ScriptableObject
// (an asset, not a scene object), its values survive scene
// transitions — the Main Menu writes to it, and the game
// scene reads from it.
//
// HOW TO CREATE THE ASSET:
//   Right-click in the Project window →
//   Create → FireRescueAI → GameData
// ----------------------------------------------------------

using UnityEngine;
using FireRescueAI.Core;

namespace FireRescueAI.Core
{
    [CreateAssetMenu(
        fileName = "GameData",
        menuName  = "FireRescueAI/GameData",
        order     = 1)]
    public class GameData : ScriptableObject
    {
        // ── Player Choices (set from Main Menu) ──────────────
        [Header("Visual Mode")]
        [Tooltip("Which visual skin the game scene will load.")]
        public VisualMode selectedVisualMode = VisualMode.ModeA_Realistic;

        [Header("Control Mode")]
        [Tooltip("Who controls the drone.")]
        public ControlMode selectedControlMode = ControlMode.Manual;

        // ── Mission Tuning ───────────────────────────────────
        [Header("Mission Settings")]
        [Tooltip("Total mission duration in seconds (default: 5 minutes).")]
        public float missionDuration = 300f;

        [Tooltip("How many victims are spawned per episode.")]
        [Range(1, 10)]
        public int victimCount = 5;

        // ── Runtime State (read-only; written by SimulationManager) ─
        [Header("Runtime State — written by game, not by you")]
        public int victimsRescued = 0;
        public float lastMissionTime = 0f;
        public MissionResult lastResult = MissionResult.InProgress;

        // ── Helpers ──────────────────────────────────────────

        /// <summary>
        /// Resets the runtime stats before each new episode.
        /// Call this at the start of every mission.
        /// </summary>
        public void ResetRuntimeState()
        {
            victimsRescued = 0;
            lastMissionTime = 0f;
            lastResult = MissionResult.InProgress;
        }
    }
}
