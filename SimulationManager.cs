// FireRescueAI — SimulationManager.cs
// ----------------------------------------------------------
// The central coordinator for one rescue episode.
//
// Responsibilities:
//   • Hold references to all major systems (timer, building, drone, fire, victims)
//   • Start and stop episodes cleanly
//   • Listen for timer-expired events and trigger mission failure
//   • Track how many victims have been rescued
//   • Reset entire episode state for RL training and manual restarts
// ----------------------------------------------------------

using UnityEngine;
using FireRescueAI.Core;
using FireRescueAI.Building;
using FireRescueAI.Drone;
using FireRescueAI.Fire;
using FireRescueAI.Rescue;
using FireRescueAI.Utilities;
using FireRescueAI.Victims;

namespace FireRescueAI.Core
{
    public class SimulationManager : MonoBehaviour
    {
        // ---- System References (set by SceneBootstrapper) ---------------------
        [Header("System References")]
        public MissionTimer    missionTimer;
        public BuildingLayout  buildingLayout;
        public DroneController droneController;
        public GameData        gameData;
        public VictimManager   victimManager;
        public FireSystem      fireSystem;
        public SmokeSystem     smokeSystem;
        public DangerZoneManager dangerZoneManager;

        // ---- Public State ----------------------------------------------------
        public bool          MissionActive  { get; private set; }
        public MissionResult CurrentResult { get; private set; } = MissionResult.InProgress;
        public int           VictimsRescued { get; private set; }

        // ---- Lifecycle -------------------------------------------------------

        private void OnEnable()
        {
            GameEvents.OnTimerExpired   += HandleTimerExpired;
            GameEvents.OnVictimRescued  += HandleVictimRescued;
        }

        private void OnDisable()
        {
            GameEvents.OnTimerExpired   -= HandleTimerExpired;
            GameEvents.OnVictimRescued  -= HandleVictimRescued;
        }

        private void OnDestroy()
        {
            GameEvents.ClearAll();
        }

        // ---- Initialisation --------------------------------------------------

        public void Initialize(
            MissionTimer    timer,
            BuildingLayout  layout,
            DroneController drone,
            GameData        data,
            VictimManager   vm = null,
            FireSystem      fs = null,
            SmokeSystem     ss = null,
            DangerZoneManager dz = null)
        {
            missionTimer      = timer;
            buildingLayout    = layout;
            droneController   = drone;
            gameData          = data;
            victimManager     = vm;
            fireSystem        = fs;
            smokeSystem       = ss;
            dangerZoneManager = dz;

            if (missionTimer != null && data != null)
                missionTimer.missionDuration = data.missionDuration;
        }

        public void StartMission()
        {
            if (MissionActive) return;

            MissionActive   = true;
            VictimsRescued  = 0;
            CurrentResult   = MissionResult.InProgress;

            if (gameData != null) gameData.ResetRuntimeState();
            if (missionTimer != null) missionTimer.StartTimer();

            GameEvents.PublishMissionStarted();
            Debug.Log("[SimulationManager] Mission started.");
        }

        public void EndMission(MissionResult result)
        {
            if (!MissionActive) return;

            MissionActive  = false;
            CurrentResult  = result;

            if (missionTimer != null) missionTimer.StopTimer();

            if (gameData != null)
            {
                gameData.lastResult       = result;
                gameData.victimsRescued   = VictimsRescued;
                gameData.lastMissionTime  =
                    missionTimer != null
                    ? missionTimer.missionDuration - missionTimer.TimeRemaining
                    : 0f;
            }

            GameEvents.PublishMissionEnded(result);
            Debug.Log($"[SimulationManager] Mission ended: {result}");
        }

        // ---- Event Handlers --------------------------------------------------

        private void HandleTimerExpired()
        {
            EndMission(MissionResult.Failed_TimeOut);
        }

        private void HandleVictimRescued(int victimId)
        {
            VictimsRescued++;

            int totalVictims = (victimManager != null && victimManager.ActiveVictims.Count > 0)
                ? victimManager.ActiveVictims.Count
                : (gameData != null ? gameData.victimCount : 5);

            if (VictimsRescued >= totalVictims && totalVictims > 0)
                EndMission(MissionResult.Success);
        }

        // ---- Episode Reset (RL Training & Manual Restart) --------------------

        /// Resets the entire episode without reloading the scene.
        public void ResetEpisode()
        {
            EndMission(MissionResult.InProgress);
            MissionActive = false;

            // 1. Reset drone position and carried victim
            if (droneController != null && buildingLayout != null)
            {
                droneController.Teleport(buildingLayout.DroneSpawnPosition);
                var rescue = droneController.GetComponent<RescueMechanic>();
                if (rescue != null) rescue.ResetCarried();
            }

            // 2. Reset fire, smoke & hazard overlays
            if (fireSystem != null)
                fireSystem.ResetAll();
            if (smokeSystem != null)
                smokeSystem.ResetAll();
            if (dangerZoneManager != null)
                dangerZoneManager.ResetAll();

            // 3. Reset victims to initial positions & states
            if (victimManager != null && buildingLayout != null)
                victimManager.ResetAll(buildingLayout.Rooms, buildingLayout.ExitPositions);

            // 4. Restart timer and mission state
            StartMission();
        }
    }
}
