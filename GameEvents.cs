// FireRescueAI — GameEvents.cs
// ----------------------------------------------------------
// A static event bus.
//
// WHY THIS PATTERN?
//   In a game with three visual modes sharing one simulation,
//   we don't want the fire system to know about the visual
//   renderer, or the drone to know about the HUD. Instead,
//   every system fires an event here, and any listener —
//   visual, audio, UI — can subscribe without coupling.
//
// HOW TO USE:
//   Subscribe:  GameEvents.OnFireSpread += MyHandler;
//   Unsubscribe: GameEvents.OnFireSpread -= MyHandler;
//   Publish:    GameEvents.PublishFireSpread(roomId);
//
// IMPORTANT: Call ClearAll() when unloading a scene to avoid
//            stale delegates causing null-reference errors.
// ----------------------------------------------------------

using System;
using UnityEngine;
using FireRescueAI.Core;

namespace FireRescueAI.Utilities
{
    public static class GameEvents
    {
        // ── Mission ───────────────────────────────────────────
        /// Fired once when a mission starts.
        public static event Action OnMissionStarted;

        /// Fired when a mission ends (success or failure).
        public static event Action<MissionResult> OnMissionEnded;

        // ── Timer ─────────────────────────────────────────────
        /// Fired when the mission countdown reaches zero.
        public static event Action OnTimerExpired;

        // ── Drone ─────────────────────────────────────────────
        /// Fired every Fixed Update with the drone's world position.
        public static event Action<Vector3> OnDroneMoved;

        /// Fired when the drone hits a wall or obstacle.
        public static event Action OnDroneCollision;

        /// Fired when the drone's sensor detects a victim (passes victim index).
        public static event Action<int> OnVictimDetected;

        // ── Fire (Milestone 2) ────────────────────────────────
        /// Fired when fire spreads into a new room (passes room id).
        public static event Action<int> OnFireSpread;

        /// Fired when a room reaches maximum fire level.
        public static event Action<int> OnRoomEngulfed;

        // ── Victims (Milestone 3) ─────────────────────────────
        /// Fired when a victim reaches a rescue exit (passes victim index).
        public static event Action<int> OnVictimRescued;

        /// Fired when a victim's behaviour state changes.
        public static event Action<int, VictimState> OnVictimStateChanged;

        // ── Publish helpers ───────────────────────────────────
        public static void PublishMissionStarted()              => OnMissionStarted?.Invoke();
        public static void PublishMissionEnded(MissionResult r) => OnMissionEnded?.Invoke(r);
        public static void PublishTimerExpired()                => OnTimerExpired?.Invoke();
        public static void PublishDroneMoved(Vector3 pos)       => OnDroneMoved?.Invoke(pos);
        public static void PublishDroneCollision()              => OnDroneCollision?.Invoke();
        public static void PublishVictimDetected(int id)        => OnVictimDetected?.Invoke(id);
        public static void PublishFireSpread(int roomId)        => OnFireSpread?.Invoke(roomId);
        public static void PublishRoomEngulfed(int roomId)      => OnRoomEngulfed?.Invoke(roomId);
        public static void PublishVictimRescued(int id)         => OnVictimRescued?.Invoke(id);
        public static void PublishVictimStateChanged(int id, VictimState s)
            => OnVictimStateChanged?.Invoke(id, s);

        /// <summary>
        /// Clears every subscriber from all events.
        /// Call this in OnDestroy or when unloading the scene
        /// to prevent "ghost" callbacks from old scripts.
        /// </summary>
        public static void ClearAll()
        {
            OnMissionStarted      = null;
            OnMissionEnded        = null;
            OnTimerExpired        = null;
            OnDroneMoved          = null;
            OnDroneCollision      = null;
            OnVictimDetected      = null;
            OnFireSpread          = null;
            OnRoomEngulfed        = null;
            OnVictimRescued       = null;
            OnVictimStateChanged  = null;
        }
    }
}
