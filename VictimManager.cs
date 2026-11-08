// FireRescueAI — VictimManager.cs
// Spawns, initializes, tracks, and resets all victims for episodes.

using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using FireRescueAI.Building;
using FireRescueAI.Core;
using FireRescueAI.Utilities;

namespace FireRescueAI.Victims
{
    public class VictimManager : MonoBehaviour
    {
        public List<VictimController> ActiveVictims { get; private set; } = new();
        private List<Vector3>  _exitPositions = new();
        // Cached for ResetAll()
        private List<RoomNode> _cachedRooms   = new();
        private List<Vector3>  _cachedExits   = new();

        public int TotalVictims => ActiveVictims.Count;
        public int RescuedCount => ActiveVictims.Count(v => v != null && v.CurrentState == VictimState.Rescued);
        public int RemainingCount => ActiveVictims.Count(v => v != null && v.gameObject.activeSelf && v.IsRescuable);

        private void OnEnable()  => GameEvents.OnVictimRescued += OnVictimRescued;
        private void OnDisable() => GameEvents.OnVictimRescued -= OnVictimRescued;

        public void Initialize(List<RoomNode> rooms, List<Vector3> exitPositions)
        {
            _cachedRooms   = rooms;
            _cachedExits   = exitPositions;
            _exitPositions = exitPositions;
            ActiveVictims.Clear();

            // Victim 0: Room 3 (Bedroom / North Annex) — PANIC near fire
            SpawnVictim(0, GetRoom(rooms, 3), new Vector3(13f, 1f, 9f), VictimState.Panic);
            // Victim 1: Room 1 (Corridor A) — CALM
            SpawnVictim(1, GetRoom(rooms, 1), new Vector3(5.5f, 1f, 0f), VictimState.Calm);
            // Victim 2: Room 2 (Living Room Hub) — HIDING in corner
            SpawnVictim(2, GetRoom(rooms, 2), new Vector3(10f, 1f, 3f), VictimState.Hide);
            // Victim 3: Room 6 (Corridor B) — TRAPPED
            SpawnVictim(3, GetRoom(rooms, 6), new Vector3(13f, 1f, -8f), VictimState.Trapped);
            // Victim 4: Room 2 (Living Room East side) — FLEE
            SpawnVictim(4, GetRoom(rooms, 2), new Vector3(16f, 1f, -2f), VictimState.Flee);
        }

        private void SpawnVictim(int id, RoomNode room, Vector3 pos, VictimState initialState)
        {
            var go = new GameObject($"Victim_{id}");
            go.transform.SetParent(transform);
            go.transform.position = pos;

            var vc = go.AddComponent<VictimController>();
            vc.Initialize(id, room, _cachedRooms, _exitPositions, initialState);
            ActiveVictims.Add(vc);
        }

        private static RoomNode GetRoom(List<RoomNode> rooms, int id)
            => rooms?.Find(r => r.id == id);

        public VictimController GetNearestRescuable(Vector3 pos)
        {
            VictimController nearest = null;
            float minDist = float.MaxValue;
            foreach (var v in ActiveVictims)
            {
                if (v == null || !v.gameObject.activeSelf || !v.IsRescuable) continue;
                float d = Vector3.Distance(pos, v.transform.position);
                if (d < minDist) { minDist = d; nearest = v; }
            }
            return (minDist < 35f) ? nearest : null;
        }

        private void OnVictimRescued(int id)
        {
            Debug.Log($"[VictimManager] Victim {id} rescued. Remaining: {RemainingCount}");
        }

        // ─────────────────────────────────────────────────────────────────────
        // RL Episode Reset
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Destroys all current victim GameObjects and re-spawns them at their
        /// original positions. Called by DroneAgent.OnEpisodeBegin().
        /// </summary>
        public void ResetAll(List<RoomNode> rooms, List<Vector3> exitPositions)
        {
            // Destroy existing victims
            foreach (var v in ActiveVictims)
                if (v != null) Destroy(v.gameObject);
            ActiveVictims.Clear();

            // Cache new references
            _cachedRooms   = rooms   ?? _cachedRooms;
            _cachedExits   = exitPositions ?? _cachedExits;
            _exitPositions = _cachedExits;

            // Re-spawn at original positions
            Initialize(_cachedRooms, _cachedExits);

            Debug.Log("[VictimManager] ResetAll: victims respawned.");
        }
    }
}
