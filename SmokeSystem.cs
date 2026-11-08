// FireRescueAI — SmokeSystem.cs
// Simulates volumetric smoke distribution above burning rooms,
// dynamically attenuating UAV optical/infrared sensor range and
// visibility for human victims based on real-time smoke density.

using System.Collections.Generic;
using UnityEngine;
using FireRescueAI.Building;

namespace FireRescueAI.Fire
{
    public class SmokeSystem : MonoBehaviour
    {
        private FireSystem _fire;
        private List<RoomNode> _rooms;
        private Dictionary<int, ParticleSystem> _smokePSMap = new();

        public void Initialize(FireSystem fireSystem, List<RoomNode> rooms)
        {
            _fire  = fireSystem;
            _rooms = rooms;
        }

        private void EnsureReferences()
        {
            if (_fire == null)
                _fire = Object.FindFirstObjectByType<FireSystem>();
            if (_rooms == null || _rooms.Count == 0)
            {
                var layout = Object.FindFirstObjectByType<BuildingLayout>();
                if (layout != null) _rooms = layout.Rooms;
            }
        }

        private void Update()
        {
            EnsureReferences();
            if (_fire == null) return;

            var activeRoomIds = new HashSet<int>();

            foreach (var node in _fire.ActiveFires)
            {
                activeRoomIds.Add(node.roomId);

                if (!_smokePSMap.ContainsKey(node.roomId) || _smokePSMap[node.roomId] == null)
                    CreateSmokePS(node.roomId);

                var ps = _smokePSMap[node.roomId];
                if (ps == null) continue;
                var em = ps.emission;
                // Smoke emission scales dynamically with burning intensity
                em.rateOverTime = Mathf.Max(2f, node.intensity * 8f);
            }

            // Remove or stop smoke from rooms that are no longer burning
            var toRemove = new List<int>();
            foreach (var kvp in _smokePSMap)
            {
                if (!activeRoomIds.Contains(kvp.Key))
                {
                    if (kvp.Value != null)
                        Destroy(kvp.Value.gameObject);
                    toRemove.Add(kvp.Key);
                }
            }
            foreach (var id in toRemove) _smokePSMap.Remove(id);
        }

        private void CreateSmokePS(int roomId)
        {
            var room = _rooms?.Find(r => r.id == roomId);
            if (room == null) return;

            var go = new GameObject($"Smoke_Room{roomId}");
            go.transform.SetParent(transform);
            go.transform.position = new Vector3(room.center.x, 1.4f, room.center.z);

            var ps   = go.AddComponent<ParticleSystem>();
            var psr  = go.GetComponent<ParticleSystemRenderer>();
            var pShader = Shader.Find("Universal Render Pipeline/Particles/Unlit") ??
                          Shader.Find("Particles/Standard Unlit") ??
                          Shader.Find("Sprites/Default");
            if (psr != null && pShader != null)
            {
                psr.material = new Material(pShader);
            }

            var main = ps.main;
            main.startColor    = new ParticleSystem.MinMaxGradient(
                new Color(0.25f, 0.25f, 0.28f, 0.65f),
                new Color(0.12f, 0.12f, 0.14f, 0.45f));
            main.startSize     = new ParticleSystem.MinMaxCurve(1.8f, 3.5f);
            main.startSpeed    = new ParticleSystem.MinMaxCurve(0.2f, 0.5f);
            main.startLifetime = new ParticleSystem.MinMaxCurve(3.5f, 6.0f);
            main.maxParticles  = 50;
            main.gravityModifier = -0.04f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var em = ps.emission;
            em.rateOverTime = 4f;

            var sh = ps.shape;
            sh.enabled   = true;
            sh.shapeType = ParticleSystemShapeType.Circle;
            sh.radius    = Mathf.Min(room.interiorSize.x, room.interiorSize.z) * 0.35f;

            _smokePSMap[roomId] = ps;
        }

        /// <summary>
        /// Calculates the continuous smoke density at any world position [0 = clear, 1 = fully occluded].
        /// Used by DroneAgent to compute sensor visibility attenuation and by victims for health/fear.
        /// </summary>
        public float GetSmokeDensityAt(Vector3 worldPos)
        {
            EnsureReferences();
            if (_fire == null) return 0f;

            float maxDensity = 0f;
            foreach (var node in _fire.ActiveFires)
            {
                var room = _rooms?.Find(r => r.id == node.roomId);
                if (room == null) continue;

                float dist = Vector3.Distance(worldPos, room.center);
                float radius = Mathf.Max(6f, Mathf.Max(room.interiorSize.x, room.interiorSize.z) * 0.75f);

                if (dist < radius)
                {
                    float factor = 1f - (dist / radius);
                    float density = node.intensity * factor;
                    if (density > maxDensity) maxDensity = density;
                }
            }
            return Mathf.Clamp01(maxDensity);
        }

        /// <summary>
        /// Cleans up all smoke particle instances on episode reset.
        /// </summary>
        public void ResetAll()
        {
            foreach (var kvp in _smokePSMap)
            {
                if (kvp.Value != null)
                    Destroy(kvp.Value.gameObject);
            }
            _smokePSMap.Clear();
        }
    }
}
