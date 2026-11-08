// FireRescueAI — RoomNode.cs
// ----------------------------------------------------------
// A plain C# data class that represents a single room in
// the building. It is NOT a MonoBehaviour — it has no
// Unity lifecycle methods.
//
// Two purposes:
//  1. Layout data: where is this room, how big, what doors?
//  2. Simulation state: how much fire/smoke is in it right now?
//
// BuildingLayout.cs uses this to generate geometry.
// FireSpreadSystem.cs (Milestone 2) uses it for fire logic.
// VictimAgent.cs (Milestone 3) uses it for pathfinding.
// ----------------------------------------------------------

using UnityEngine;
using System.Collections.Generic;

namespace FireRescueAI.Building
{
    // ── Which side of a room does a doorway face? ────────────
    public enum WallDirection { North, South, East, West }

    // ── One connection between two rooms ─────────────────────
    [System.Serializable]
    public class RoomConnection
    {
        /// The id of the room on the other side of this doorway.
        public int connectedRoomId;

        /// Which wall of THIS room the doorway is on.
        public WallDirection wallSide;

        // Width and height of the opening (used by BuildingLayout).
        public float doorwayWidth  = 2.2f;
        public float doorwayHeight = 2.8f;
    }

    // ── The room itself ──────────────────────────────────────
    [System.Serializable]
    public class RoomNode
    {
        // ─ Identity ─────────────────────────────────────────
        public int    id;
        public string roomName;

        // ─ Layout (set at startup, never changes) ───────────
        /// World-space centre of this room (Y = half the wall height).
        public Vector3 center;

        /// Interior dimensions: (width along X, height, depth along Z).
        public Vector3 interiorSize;

        /// True if the drone starts here.
        public bool isSpawnRoom = false;

        /// True if victims can escape through this room.
        public bool isExitRoom = false;

        /// All doorways leading out of this room.
        public List<RoomConnection> connections = new List<RoomConnection>();

        // ─ Runtime simulation state (not serialized) ────────
        /// 0 = no fire, 100 = fully engulfed (used in Milestone 2).
        [System.NonSerialized] public float fireLevel = 0f;

        /// 0 = clear, 100 = opaque smoke (used in Milestone 2).
        [System.NonSerialized] public float smokeLevel = 0f;

        /// Can the drone or victims currently enter this room?
        [System.NonSerialized] public bool isAccessible = true;

        /// Reference to the room's geometry parent GameObject.
        [System.NonSerialized] public GameObject generatedObject;

        // ─ Constructor ──────────────────────────────────────
        public RoomNode(int id, string name, Vector3 center, Vector3 size)
        {
            this.id           = id;
            this.roomName     = name;
            this.center       = center;
            this.interiorSize = size;
        }

        // ─ Helpers ──────────────────────────────────────────

        /// Returns a Bounds object covering the interior of this room.
        public Bounds GetInteriorBounds()
            => new Bounds(center, interiorSize);

        /// Returns true if the given world position is inside this room.
        public bool Contains(Vector3 worldPos)
            => GetInteriorBounds().Contains(worldPos);

        /// Convenience: find a connection by wall side.
        public RoomConnection GetConnection(WallDirection dir)
            => connections.Find(c => c.wallSide == dir);

        public override string ToString()
            => $"Room[{id}] \"{roomName}\" @ {center} size {interiorSize}";
    }
}
