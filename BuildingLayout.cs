// FireRescueAI — BuildingLayout.cs
// ----------------------------------------------------------
// Generates a realistic roofless indoor building at runtime.
// Walls are assigned to Layer 8 ("Walls") so the top-down
// orthographic camera can cull them and see the interior.
//
// BUILDING LAYOUT (top-down, X=East, Z=North):
//
//   [EXTERIOR GROUND — extends beyond all rooms]
//
//              [R3: North Annex / Bedroom]
//                        |
//   [R0:Spawn]--[R1:Corridor A]--[R2:Hub]--[R5:East Exit]
//                        |
//                   [R6:Corridor B]
//                        |
//                   [R4:South Exit]
//
// Each room has a distinct floor color and optional furniture.
// Walls block horizontal movement but are transparent from above.
// ----------------------------------------------------------

using UnityEngine;
using System.Collections.Generic;

namespace FireRescueAI.Building
{
    public class BuildingLayout : MonoBehaviour
    {
        // ── Inspector Settings ──────────────────────────────────────
        [Header("Building Dimensions")]
        [Tooltip("Height of every room (floor to ceiling).")]
        public float wallHeight = 3.5f;

        [Tooltip("Thickness of wall cubes.")]
        public float wallThickness = 0.35f;

        [Header("Doorways")]
        [Tooltip("Width of doorway openings.")]
        public float doorwayWidth = 2.0f;

        [Tooltip("Clear height of doorway openings.")]
        public float doorwayHeight = 2.4f;

        [Header("Override Materials (optional — leave blank for defaults)")]
        public Material wallMaterial;
        public Material floorMaterial;
        public Material exitFloorMaterial;

        // ── Public Data ─────────────────────────────────────────────
        public List<RoomNode> Rooms { get; private set; } = new List<RoomNode>();

        public Vector3 DroneSpawnPosition { get; private set; }

        /// World-space centres of exit rooms (rooms 4 and 5).
        public List<Vector3> ExitPositions { get; private set; } = new List<Vector3>();

        // Wall layer index — top-down camera culls this
        public const int WallLayer = 8;

        // ── Lifecycle ───────────────────────────────────────────────

        public void Generate()
        {
            RegisterRooms();
            GenerateGeometry();
            SpawnFurniture();
            SpawnExteriorGround();
            SpawnExitMarkers();
            SpawnRoomLabels();
        }

        // ──────────────────────────────────────────────────────────
        // STEP 1: Define room layout (pure data)
        // ──────────────────────────────────────────────────────────

        private void RegisterRooms()
        {
            Rooms.Clear();
            float halfH = wallHeight / 2f;

            // Room 0: Spawn / Entrance Hall
            var r0 = new RoomNode(0, "Entrance Hall",
                center: new Vector3(0f, halfH, 0f),
                size:   new Vector3(8f, wallHeight, 8f));
            r0.isSpawnRoom = true;

            // Room 1: Corridor A (horizontal)
            var r1 = new RoomNode(1, "Corridor A",
                center: new Vector3(5.5f, halfH, 0f),
                size:   new Vector3(3f, wallHeight, 4f));

            // Room 2: Main Hub / Living Room
            var r2 = new RoomNode(2, "Living Room",
                center: new Vector3(13f, halfH, 0f),
                size:   new Vector3(12f, wallHeight, 12f));

            // Room 3: North Annex / Bedroom
            var r3 = new RoomNode(3, "Bedroom",
                center: new Vector3(13f, halfH, 9f),
                size:   new Vector3(8f, wallHeight, 6f));

            // Room 4: South Exit
            var r4 = new RoomNode(4, "South Exit",
                center: new Vector3(13f, halfH, -14f),
                size:   new Vector3(8f, wallHeight, 8f));
            r4.isExitRoom = true;

            // Room 5: East Exit
            var r5 = new RoomNode(5, "East Exit",
                center: new Vector3(23f, halfH, 0f),
                size:   new Vector3(8f, wallHeight, 8f));
            r5.isExitRoom = true;

            // Room 6: Corridor B (vertical)
            var r6 = new RoomNode(6, "Corridor B",
                center: new Vector3(13f, halfH, -8f),
                size:   new Vector3(3f, wallHeight, 4f));

            Rooms.AddRange(new[] { r0, r1, r2, r3, r4, r5, r6 });

            // Connections
            Connect(r0, WallDirection.East,  r1, WallDirection.West);
            Connect(r1, WallDirection.East,  r2, WallDirection.West);
            Connect(r2, WallDirection.North, r3, WallDirection.South);
            Connect(r2, WallDirection.South, r6, WallDirection.North);
            Connect(r6, WallDirection.South, r4, WallDirection.North);
            Connect(r2, WallDirection.East,  r5, WallDirection.West);

            // Spawn + exits
            DroneSpawnPosition = new Vector3(r0.center.x, 1.2f, r0.center.z);

            ExitPositions.Clear();
            foreach (var room in Rooms)
                if (room.isExitRoom)
                    ExitPositions.Add(new Vector3(room.center.x, 0f, room.center.z));
        }

        private static void Connect(
            RoomNode a, WallDirection aSide,
            RoomNode b, WallDirection bSide)
        {
            a.connections.Add(new RoomConnection { connectedRoomId = b.id, wallSide = aSide });
            b.connections.Add(new RoomConnection { connectedRoomId = a.id, wallSide = bSide });
        }

        // ──────────────────────────────────────────────────────────
        // STEP 2: Generate Unity GameObjects
        // ──────────────────────────────────────────────────────────

        private void GenerateGeometry()
        {
            foreach (var room in Rooms)
                GenerateRoom(room);
        }

        private void GenerateRoom(RoomNode room)
        {
            var roomGO = new GameObject($"Room_{room.id}_{room.roomName}");
            roomGO.transform.SetParent(transform);
            room.generatedObject = roomGO;

            float cx = room.center.x;
            float cy = room.center.y;   // = wallHeight / 2
            float cz = room.center.z;
            float iw = room.interiorSize.x;
            float ih = room.interiorSize.y;
            float id = room.interiorSize.z;
            float wt = wallThickness;

            // ── Floor (room-specific color) ──────────────────────
            Material floorMat = GetRoomFloorMat(room.id);
            CreateSlab("Floor",
                center: new Vector3(cx, cy - ih / 2f, cz),
                size:   new Vector3(iw, 0.18f, id),
                parent: roomGO.transform,
                mat:    floorMat,
                addCollider: true,
                isWall: false);

            // ── NO CEILING — roofless building so top-down camera sees inside ──

            // ── Walls ────────────────────────────────────────────
            GenerateWall(room, WallDirection.North, roomGO.transform);
            GenerateWall(room, WallDirection.South, roomGO.transform);
            GenerateWall(room, WallDirection.East,  roomGO.transform);
            GenerateWall(room, WallDirection.West,  roomGO.transform);
        }

        // ──────────────────────────────────────────────────────────
        // Wall generation with doorway
        // ──────────────────────────────────────────────────────────

        private void GenerateWall(RoomNode room, WallDirection dir, Transform parent)
        {
            float cx = room.center.x;
            float cy = room.center.y;
            float cz = room.center.z;
            float iw = room.interiorSize.x;
            float ih = room.interiorSize.y;
            float id = room.interiorSize.z;
            float wt = wallThickness;

            Vector3 wallCenter;
            float   wallLength;
            bool    isZAligned;

            switch (dir)
            {
                case WallDirection.North:
                    wallCenter = new Vector3(cx, cy, cz + id / 2f + wt / 2f);
                    wallLength = iw;
                    isZAligned = false;
                    break;
                case WallDirection.South:
                    wallCenter = new Vector3(cx, cy, cz - id / 2f - wt / 2f);
                    wallLength = iw;
                    isZAligned = false;
                    break;
                case WallDirection.East:
                    wallCenter = new Vector3(cx + iw / 2f + wt / 2f, cy, cz);
                    wallLength = id + wt * 2f;
                    isZAligned = true;
                    break;
                case WallDirection.West:
                    wallCenter = new Vector3(cx - iw / 2f - wt / 2f, cy, cz);
                    wallLength = id + wt * 2f;
                    isZAligned = true;
                    break;
                default: return;
            }

            Vector3 fullWallSize = isZAligned
                ? new Vector3(wt, ih, wallLength)
                : new Vector3(wallLength, ih, wt);

            RoomConnection conn = room.GetConnection(dir);

            if (conn == null)
            {
                CreateSlab($"Wall_{dir}", wallCenter, fullWallSize, parent, GetWallMat(), true, isWall: true);
                return;
            }

            RoomNode other = Rooms.Find(r => r.id == conn.connectedRoomId);
            if (other != null && other.id < room.id)
                return; // other room generates this shared wall

            GenerateDoorwayWall(wallCenter, fullWallSize, wallLength, ih, wt, isZAligned, dir, parent);
        }

        private void GenerateDoorwayWall(
            Vector3 wallCenter, Vector3 fullWallSize,
            float wallLength, float ih, float wt,
            bool isZAligned, WallDirection dir, Transform parent)
        {
            float openW   = Mathf.Min(doorwayWidth, wallLength * 0.65f);
            float sideW   = (wallLength - openW) / 2f;
            float lintelH = ih - doorwayHeight;

            Vector3 axis       = isZAligned ? Vector3.forward : Vector3.right;
            float   sideOffset = openW / 2f + sideW / 2f;

            // Left slab
            if (sideW > 0.05f)
            {
                Vector3 leftCenter = wallCenter - axis * sideOffset;
                Vector3 leftSize   = isZAligned
                    ? new Vector3(wt, ih, sideW)
                    : new Vector3(sideW, ih, wt);
                CreateSlab($"Wall_{dir}_L", leftCenter, leftSize, parent, GetWallMat(), true, isWall: true);
            }

            // Right slab
            if (sideW > 0.05f)
            {
                Vector3 rightCenter = wallCenter + axis * sideOffset;
                Vector3 rightSize   = isZAligned
                    ? new Vector3(wt, ih, sideW)
                    : new Vector3(sideW, ih, wt);
                CreateSlab($"Wall_{dir}_R", rightCenter, rightSize, parent, GetWallMat(), true, isWall: true);
            }

            // Doorway threshold on floor (visual indicator of open doorway from top-down perspective)
            Vector3 threshSize = isZAligned
                ? new Vector3(wt * 1.3f, 0.04f, openW)
                : new Vector3(openW, 0.04f, wt * 1.3f);
            Vector3 threshCenter = new Vector3(wallCenter.x, 0.02f, wallCenter.z);
            CreateSlab($"Door_Threshold_{dir}", threshCenter, threshSize, parent, GetThresholdMat(), false, isWall: false);
        }

        // ──────────────────────────────────────────────────────────
        // STEP 3: Furniture
        // ──────────────────────────────────────────────────────────

        private void SpawnFurniture()
        {
            // Entrance Hall (R0): reception desk, two chairs
            SpawnDesk("Reception Desk", new Vector3(-1.5f, 0f, 2f),   new Vector3(2f, 0.85f, 0.9f), new Color(0.55f, 0.38f, 0.22f));
            SpawnBox("Chair A",         new Vector3( 0.5f, 0f, 2.2f), new Vector3(0.5f, 0.5f, 0.5f), new Color(0.3f, 0.3f, 0.5f));
            SpawnBox("Chair B",         new Vector3(-0.5f, 0f, 2.2f), new Vector3(0.5f, 0.5f, 0.5f), new Color(0.3f, 0.3f, 0.5f));

            // Living Room (R2): sofa, coffee table, tv stand
            SpawnDesk("Sofa",           new Vector3(10f, 0f, 2f),  new Vector3(3f, 0.8f, 1f), new Color(0.4f, 0.35f, 0.55f));
            SpawnBox("Coffee Table",    new Vector3(10f, 0f, 0f),  new Vector3(1.8f, 0.45f, 1f), new Color(0.55f, 0.38f, 0.22f));
            SpawnDesk("TV Stand",       new Vector3(10f, 0f, -4f), new Vector3(2f, 0.6f, 0.6f), new Color(0.2f, 0.2f, 0.25f));
            SpawnBox("Bookshelf",       new Vector3(17f, 0f, 3f),  new Vector3(0.4f, 1.8f, 2f), new Color(0.45f, 0.32f, 0.18f));
            SpawnBox("Bookshelf2",      new Vector3(17f, 0f, -3f), new Vector3(0.4f, 1.8f, 2f), new Color(0.45f, 0.32f, 0.18f));

            // Bedroom (R3): bed, wardrobe, bedside table
            SpawnDesk("Bed",            new Vector3(14f, 0f, 9.5f),  new Vector3(1.8f, 0.6f, 3.2f), new Color(0.85f, 0.82f, 0.75f));
            SpawnBox("Bedside Table",   new Vector3(12f, 0f, 9f),    new Vector3(0.6f, 0.65f, 0.6f), new Color(0.55f, 0.38f, 0.22f));
            SpawnBox("Wardrobe",        new Vector3(10.5f, 0f, 10f), new Vector3(0.5f, 2.0f, 2.5f), new Color(0.35f, 0.25f, 0.15f));
            SpawnBox("Desk",            new Vector3(14.5f, 0f, 7f),  new Vector3(1.5f, 0.75f, 0.7f), new Color(0.55f, 0.42f, 0.25f));

            // South Exit (R4): exit benches
            SpawnBox("Exit Bench L",    new Vector3(10.5f, 0f, -14f), new Vector3(1f, 0.5f, 2f), new Color(0.3f, 0.6f, 0.3f));
            SpawnBox("Exit Bench R",    new Vector3(15.5f, 0f, -14f), new Vector3(1f, 0.5f, 2f), new Color(0.3f, 0.6f, 0.3f));

            // East Exit (R5): exit counter
            SpawnDesk("Exit Counter",   new Vector3(23f, 0f, 1.5f),  new Vector3(3f, 0.85f, 0.8f), new Color(0.3f, 0.6f, 0.3f));
        }

        private void SpawnDesk(string name, Vector3 worldPos, Vector3 size, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(transform);
            go.transform.position   = worldPos + Vector3.up * (size.y / 2f);
            go.transform.localScale = size;
            go.isStatic             = false;
            go.layer                = 0; // default — visible to all cameras
            var col = go.GetComponent<BoxCollider>();
            if (col != null) col.enabled = true; // furniture blocks drone
            ApplyMat(go, color);
        }

        private void SpawnBox(string name, Vector3 worldPos, Vector3 size, Color color)
            => SpawnDesk(name, worldPos, size, color); // same logic, separate method for clarity

        // ──────────────────────────────────────────────────────────
        // STEP 4: Exterior ground
        // ──────────────────────────────────────────────────────────

        private void SpawnExteriorGround()
        {
            // Large flat slab extending beyond all rooms — represents outdoors/ground
            var go = CreateSlab("ExteriorGround",
                center: new Vector3(11f, -0.1f, -3f),
                size:   new Vector3(60f, 0.12f, 55f),
                parent: transform,
                mat:    CreateDefaultMat(new Color(0.24f, 0.28f, 0.20f)), // dark grass/asphalt
                addCollider: true,
                isWall: false);
        }

        // ──────────────────────────────────────────────────────────
        // STEP 5: Exit markers (bright arrows on exit room floors)
        // ──────────────────────────────────────────────────────────

        private void SpawnExitMarkers()
        {
            // South exit arrow (pointing south, on floor)
            SpawnExitArrow(new Vector3(13f, 0.02f, -14f), 0f);
            // East exit arrow (pointing east)
            SpawnExitArrow(new Vector3(23f, 0.02f, 0f),  90f);
        }

        private void SpawnExitArrow(Vector3 pos, float rotY)
        {
            // Simple bright green quad on the floor
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "ExitMarker";
            go.transform.SetParent(transform);
            go.transform.position = pos;
            go.transform.rotation = Quaternion.Euler(90f, rotY, 0f);
            go.transform.localScale = new Vector3(3f, 3f, 1f);
            Destroy(go.GetComponent<MeshCollider>());
            go.isStatic = false;
            go.layer    = 0;
            ApplyMat(go, new Color(0.1f, 1f, 0.2f, 0.9f));
        }

        // ──────────────────────────────────────────────────────────
        // STEP 6: Room tactical labels (viewable from overhead)
        // ──────────────────────────────────────────────────────────

        private void SpawnRoomLabels()
        {
            foreach (var room in Rooms)
            {
                var labelGO = new GameObject($"Label_Room{room.id}");
                labelGO.transform.SetParent(transform);

                // Offset towards top of the room so it doesn't collide with center furniture
                float zOffset = room.interiorSize.z * 0.30f;
                labelGO.transform.position = new Vector3(room.center.x, 0.05f, room.center.z + zOffset);
                labelGO.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

                var tmp = labelGO.AddComponent<TMPro.TextMeshPro>();
                tmp.text = $"<size=80%><b>{room.roomName.ToUpper()}</b></size>\n<size=55%><color=#88BBEE>ROOM {room.id}</color></size>";
                tmp.fontSize = 3.2f;
                tmp.alignment = TMPro.TextAlignmentOptions.Center;
                tmp.color = new Color(0.92f, 0.95f, 1.0f, 0.85f);
                tmp.transform.localScale = Vector3.one * 0.35f;
            }
        }

        // ──────────────────────────────────────────────────────────
        // Cube slab factory
        // ──────────────────────────────────────────────────────────

        private GameObject CreateSlab(
            string name,
            Vector3 center,
            Vector3 size,
            Transform parent,
            Material mat,
            bool addCollider,
            bool isWall)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent);
            go.transform.position   = center;
            go.transform.localScale = size;
            go.isStatic             = false;

            // Walls go on Layer 8 so the top-down camera can cull them
            go.layer = isWall ? WallLayer : 0;

            var autoBox = go.GetComponent<BoxCollider>();
            if (!addCollider && autoBox != null)
                Destroy(autoBox);

            var rend = go.GetComponent<MeshRenderer>();
            if (rend != null)
                rend.sharedMaterial = mat ?? CreateDefaultMat(Color.grey);

            return go;
        }

        private void ApplyMat(GameObject go, Color color)
        {
            var rend = go.GetComponent<Renderer>();
            if (rend != null) rend.material = CreateDefaultMat(color);
        }

        // ──────────────────────────────────────────────────────────
        // Material helpers
        // ──────────────────────────────────────────────────────────

        // Per-room floor color lookup
        private Material GetRoomFloorMat(int roomId)
        {
            Color c = roomId switch
            {
                0 => new Color(0.55f, 0.50f, 0.42f), // Entrance: warm tan tiles
                1 => new Color(0.40f, 0.40f, 0.38f), // Corridor A: plain grey
                2 => new Color(0.42f, 0.38f, 0.55f), // Living Room: purple-grey carpet
                3 => new Color(0.55f, 0.48f, 0.40f), // Bedroom: warm wood
                4 => new Color(0.20f, 0.70f, 0.30f), // South Exit: bright green
                5 => new Color(0.20f, 0.70f, 0.30f), // East Exit: bright green
                6 => new Color(0.40f, 0.40f, 0.38f), // Corridor B: plain grey
                _ => new Color(0.40f, 0.38f, 0.36f)
            };

            if (exitFloorMaterial != null && (roomId == 4 || roomId == 5))
                return exitFloorMaterial;
            if (floorMaterial != null && roomId != 4 && roomId != 5)
                return floorMaterial;

            return CreateDefaultMat(c);
        }

        private Material _wallMatCache;
        private Material _threshMatCache;

        private Material GetWallMat()
        {
            if (wallMaterial != null) return wallMaterial;
            if (_wallMatCache == null)
                _wallMatCache = CreateDefaultMat(new Color(0.24f, 0.28f, 0.35f)); // modern slate grey
            return _wallMatCache;
        }

        private Material GetThresholdMat()
        {
            if (_threshMatCache == null)
                _threshMatCache = CreateDefaultMat(new Color(0.38f, 0.40f, 0.46f)); // doorway transition strip
            return _threshMatCache;
        }

        private static Material CreateDefaultMat(Color color)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit")
                      ?? Shader.Find("Standard");

            if (shader == null)
            {
                Debug.LogWarning("[BuildingLayout] No shader found — using error shader.");
                return new Material(Shader.Find("Hidden/InternalErrorShader"));
            }

            var mat = new Material(shader);
            mat.color = color;
            return mat;
        }

        // ──────────────────────────────────────────────────────────
        // Public utilities
        // ──────────────────────────────────────────────────────────

        public RoomNode GetRoomAt(Vector3 worldPos)
        {
            foreach (var room in Rooms)
                if (room.Contains(worldPos)) return room;
            return null;
        }

        public RoomNode GetRoom(int id)
            => Rooms.Find(r => r.id == id);
    }
}
