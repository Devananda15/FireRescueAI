// FireRescueAI — DangerZoneManager.cs
// Renders transparent red overlays on floors of rooms with active fire.

using System.Collections.Generic;
using UnityEngine;
using FireRescueAI.Building;

namespace FireRescueAI.Fire
{
    public class DangerZoneManager : MonoBehaviour
    {
        private FireSystem     _fire;
        private List<RoomNode> _rooms;
        private Dictionary<int, GameObject> _overlays = new();
        private Material       _dangerMat;
        private float          _updateTimer;
        private const float    UPDATE_INTERVAL = 2f;

        public void Initialize(FireSystem fireSystem, List<RoomNode> rooms)
        {
            _fire  = fireSystem;
            _rooms = rooms;

            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            _dangerMat = new Material(shader ?? Shader.Find("Hidden/InternalErrorShader"));
            _dangerMat.color = new Color(1f, 0.05f, 0.05f, 0.30f);

            // Enable transparency
            if (_dangerMat.HasProperty("_Surface"))
            {
                _dangerMat.SetFloat("_Surface", 1f);      // Transparent
                _dangerMat.SetFloat("_Blend", 0f);
                _dangerMat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                _dangerMat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                _dangerMat.SetFloat("_ZWrite", 0f);
                _dangerMat.renderQueue = 3000;
                _dangerMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            }
        }

        private void Update()
        {
            _updateTimer -= Time.deltaTime;
            if (_updateTimer > 0f) return;
            _updateTimer = UPDATE_INTERVAL;

            if (_fire == null) return;
            UpdateOverlays();
        }

        private void UpdateOverlays()
        {
            var activeIds = new HashSet<int>();
            foreach (var node in _fire.ActiveFires)
                activeIds.Add(node.roomId);

            // Add new overlays
            foreach (var id in activeIds)
            {
                if (_overlays.ContainsKey(id)) continue;
                var room = _rooms?.Find(r => r.id == id);
                if (room == null) continue;

                var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
                go.name = $"DangerZone_Room{id}";
                go.transform.SetParent(transform);
                go.transform.position  = new Vector3(room.center.x, 0.05f, room.center.z);
                go.transform.rotation  = Quaternion.Euler(90f, 0f, 0f);
                go.transform.localScale = new Vector3(room.interiorSize.x, room.interiorSize.z, 1f);
                Destroy(go.GetComponent<Collider>());
                go.GetComponent<Renderer>().material = _dangerMat;
                go.isStatic = false;
                _overlays[id] = go;
            }

            // Remove overlays whose rooms no longer have fire
            var toRemove = new List<int>();
            foreach (var kvp in _overlays)
            {
                if (!activeIds.Contains(kvp.Key))
                {
                    if (kvp.Value != null) Destroy(kvp.Value);
                    toRemove.Add(kvp.Key);
                }
            }
            foreach (var id in toRemove) _overlays.Remove(id);
        }

        public bool IsDroneInDangerZone(Vector3 dronePos)
        {
            if (_fire == null) return false;
            foreach (var node in _fire.ActiveFires)
            {
                var room = _rooms?.Find(r => r.id == node.roomId);
                if (room != null && room.GetInteriorBounds().Contains(dronePos))
                    return true;
            }
            return false;
        }

        public void ResetAll()
        {
            foreach (var kvp in _overlays)
            {
                if (kvp.Value != null) Destroy(kvp.Value);
            }
            _overlays.Clear();
        }
    }
}
