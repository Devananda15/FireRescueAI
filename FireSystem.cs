// FireRescueAI — FireSystem.cs
// Manages fire simulation: visual particles, spreading, intensity.

using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using FireRescueAI.Building;
using FireRescueAI.Utilities;

namespace FireRescueAI.Fire
{
    public class FireSystem : MonoBehaviour
    {
        [Header("Spread Settings")]
        public float spreadInterval    = 8f;
        public float spreadProbability = 0.30f;
        public int   maxFireNodes      = 4;

        private List<RoomNode> _rooms;
        private Dictionary<int, FireNode>    _fireNodes = new();
        private Dictionary<int, GameObject>  _fireVFX   = new();
        private float _spreadTimer;

        public List<FireNode> ActiveFires
            => _fireNodes.Values.Where(f => f.IsActive).ToList();

        public int ActiveFireCount => ActiveFires.Count;

        public void Initialize(List<RoomNode> rooms)
        {
            _rooms = rooms;

            // Start fire in room 3 (North Annex)
            AddFireNode(3, 0.65f);

            _spreadTimer = spreadInterval;
            Debug.Log("[FireSystem] Initialized. Fire started in Room 3.");
        }

        private void Update()
        {
            _spreadTimer -= Time.deltaTime;
            if (_spreadTimer <= 0f)
            {
                _spreadTimer = spreadInterval;
                TrySpreadFire();
            }

            // Update particle system emission rates
            foreach (var kvp in _fireNodes)
            {
                if (!_fireVFX.TryGetValue(kvp.Key, out var fxGO) || fxGO == null) continue;
                var ps = fxGO.GetComponent<ParticleSystem>();
                if (ps == null) continue;
                var em = ps.emission;
                em.rateOverTime = kvp.Value.intensity * 30f;

                var lt = fxGO.GetComponentInChildren<Light>();
                if (lt != null) lt.intensity = kvp.Value.intensity * 3f;
            }
        }

        private void TrySpreadFire()
        {
            if (ActiveFireCount >= maxFireNodes) return;

            foreach (var node in ActiveFires.ToList())
            {
                var room = _rooms?.Find(r => r.id == node.roomId);
                if (room == null) continue;

                foreach (var conn in room.connections)
                {
                    if (_fireNodes.ContainsKey(conn.connectedRoomId)) continue;
                    if (Random.value < spreadProbability * node.intensity)
                    {
                        AddFireNode(conn.connectedRoomId, 0.25f);
                        GameEvents.PublishFireSpread(conn.connectedRoomId);
                        Debug.Log($"[FireSystem] Fire spread to room {conn.connectedRoomId}.");
                        if (ActiveFireCount >= maxFireNodes) return;
                    }
                }
            }
        }

        private void AddFireNode(int roomId, float intensity)
        {
            if (_fireNodes.ContainsKey(roomId)) return;

            var node = new FireNode(roomId, intensity);
            _fireNodes[roomId] = node;

            var room = _rooms?.Find(r => r.id == roomId);
            if (room == null) return;

            // Create fire VFX
            var fxGO = new GameObject($"Fire_Room{roomId}");
            fxGO.transform.SetParent(transform);
            fxGO.transform.position = new Vector3(room.center.x, 0.3f, room.center.z);
            _fireVFX[roomId] = fxGO;

            // Particle system
            var ps     = fxGO.AddComponent<ParticleSystem>();
            var psr    = fxGO.GetComponent<ParticleSystemRenderer>();
            var pShader = Shader.Find("Universal Render Pipeline/Particles/Unlit") ??
                          Shader.Find("Particles/Standard Unlit") ??
                          Shader.Find("Sprites/Default");
            if (psr != null && pShader != null)
            {
                psr.material = new Material(pShader);
            }

            var main   = ps.main;
            main.startColor       = new ParticleSystem.MinMaxGradient(
                new Color(1f, 0.55f, 0.05f),
                new Color(1f, 0.15f, 0f));
            main.startSize        = new ParticleSystem.MinMaxCurve(0.5f, 1.2f);
            main.startSpeed       = new ParticleSystem.MinMaxCurve(0.5f, 1.5f);
            main.startLifetime    = new ParticleSystem.MinMaxCurve(0.8f, 1.5f);
            main.maxParticles     = 60;
            main.gravityModifier  = -0.1f;
            main.simulationSpace  = ParticleSystemSimulationSpace.World;

            var em = ps.emission;
            em.rateOverTime = intensity * 30f;

            var sh = ps.shape;
            sh.enabled     = true;
            sh.shapeType   = ParticleSystemShapeType.Circle;
            sh.radius      = 0.6f;

            // Point light for fire glow
            var lightGO = new GameObject("FireLight");
            lightGO.transform.SetParent(fxGO.transform);
            lightGO.transform.localPosition = Vector3.up * 0.5f;
            var lt = lightGO.AddComponent<Light>();
            lt.type      = LightType.Point;
            lt.color     = new Color(1f, 0.45f, 0.05f);
            lt.intensity = intensity * 3f;
            lt.range     = 5f;
            lt.shadows   = LightShadows.None;
        }

        public float GetFireIntensityAt(Vector3 worldPos)
        {
            float maxIntensity = 0f;
            foreach (var node in ActiveFires)
            {
                var room = _rooms?.Find(r => r.id == node.roomId);
                if (room == null) continue;
                float dist = Vector3.Distance(worldPos, room.center);
                if (dist < 6f)
                    maxIntensity = Mathf.Max(maxIntensity, node.intensity * (1f - dist / 6f));
            }
            return maxIntensity;
        }

        // ─────────────────────────────────────────────────────────────────────
        // RL Episode Reset
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Destroys all active fire VFX objects and restarts fire in Room 3.
        /// Called by DroneAgent.OnEpisodeBegin().
        /// </summary>
        public void ResetAll()
        {
            // Destroy existing fire GameObjects
            foreach (var kvp in _fireVFX)
            {
                if (kvp.Value != null)
                    Destroy(kvp.Value);
            }
            _fireVFX.Clear();
            _fireNodes.Clear();
            _spreadTimer = spreadInterval;

            // Restart with fire in Room 3 (same as Initialize)
            AddFireNode(3, 0.65f);

            Debug.Log("[FireSystem] ResetAll: fire restarted in Room 3.");
        }
    }
}
