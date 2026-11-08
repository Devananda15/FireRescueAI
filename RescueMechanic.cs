// FireRescueAI — RescueMechanic.cs
// Handles drone pickup and delivery of victims.
// Attach to the Drone GameObject (SceneBootstrapper does this at runtime).

using System.Collections.Generic;
using UnityEngine;
using FireRescueAI.Victims;
using FireRescueAI.Utilities;

namespace FireRescueAI.Rescue
{
    public class RescueMechanic : MonoBehaviour
    {
        [Header("Settings")]
        public float pickupRange   = 2.5f;
        public float deliveryRange = 3.0f;

        private VictimManager     _victimManager;
        private List<Vector3>     _exitPositions = new();
        private VictimController  _carried;
        private float             _warningCooldown;

        public bool             IsCarryingVictim => _carried != null;
        public VictimController CarriedVictim    => _carried;

        public VictimController NearestVictim
        {
            get
            {
                if (_victimManager == null) return null;
                var nearest = _victimManager.GetNearestRescuable(transform.position);
                if (nearest == null) return null;
                return Vector3.Distance(transform.position, nearest.transform.position) <= pickupRange
                    ? nearest : null;
            }
        }

        public void Initialize(VictimManager vm, List<Vector3> exits)
        {
            _victimManager = vm;
            _exitPositions = exits;
            _carried       = null;
        }

        public void ResetCarried()
        {
            _carried = null;
        }

        private void Update()
        {
            // E to pick up / interact
            if (UnityEngine.InputSystem.Keyboard.current != null &&
                UnityEngine.InputSystem.Keyboard.current.eKey.wasPressedThisFrame)
            {
                if (!IsCarryingVictim)
                    TryPickup();
            }

            // Update carried victim position
            if (IsCarryingVictim && _carried.gameObject.activeSelf)
            {
                _carried.transform.position = transform.position + new Vector3(0f, -0.6f, 0f);

                // Check if at exit
                CheckDelivery();
            }
            else if (IsCarryingVictim && !_carried.gameObject.activeSelf)
            {
                _carried = null;
            }
        }

        private void TryPickup()
        {
            var victim = NearestVictim;
            if (victim == null)
            {
                Debug.Log("[RescueMechanic] No rescuable victim in range.");
                return;
            }

            _carried = victim;
            // Disable victim's collider while carried
            var col = victim.GetComponent<Collider>();
            if (col != null) col.enabled = false;

            Debug.Log($"[RescueMechanic] Picked up Victim {victim.victimId}.");
            GameEvents.PublishDroneMoved(transform.position); // reuse event to trigger HUD refresh
        }

        /// <summary>
        /// Called by DroneAgent (AI) to attempt a pickup.
        /// Returns true if a victim was successfully picked up.
        /// </summary>
        public bool TryPickupNearest()
        {
            if (IsCarryingVictim) return false; // already carrying

            var victim = NearestVictim;
            if (victim == null) return false;

            _carried = victim;
            var col = victim.GetComponent<Collider>();
            if (col != null) col.enabled = false;

            Debug.Log($"[RescueMechanic] AI picked up Victim {victim.victimId}.");
            GameEvents.PublishDroneMoved(transform.position);
            return true;
        }

        private void CheckDelivery()
        {
            foreach (var exit in _exitPositions)
            {
                if (Vector3.Distance(transform.position, exit) <= deliveryRange)
                {
                    DeliverVictim();
                    return;
                }
            }
        }

        private void DeliverVictim()
        {
            if (_carried == null) return;
            var col = _carried.GetComponent<Collider>();
            if (col != null) col.enabled = true;
            _carried.Rescue();
            Debug.Log($"[RescueMechanic] Delivered Victim {_carried.victimId} at exit!");
            _carried = null;
        }
    }
}
