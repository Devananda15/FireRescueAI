// FireRescueAI — VisualModeManager.cs
// ----------------------------------------------------------
// In Milestone 1: a placeholder that reads the selected
// VisualMode from GameData and logs it. The building is
// always generated in the same default style.
//
// In Milestone 4: this script will activate/deactivate the
// correct [VisualRoot_ModeA], [VisualRoot_ModeB], and
// [VisualRoot_ModeC] GameObjects in the scene, swap URP
// renderer assets, and reconfigure Cinemachine cameras.
//
// The Visual Mode Manager sits between the simulation layer
// (which never changes) and the presentation layer (which
// changes entirely based on the selected mode).
// ----------------------------------------------------------

using UnityEngine;
using FireRescueAI.Core;

namespace FireRescueAI.Modes
{
    public class VisualModeManager : MonoBehaviour
    {
        // ── References ────────────────────────────────────────
        [Header("GameData (set by SceneBootstrapper)")]
        public GameData gameData;

        // ── Visual Root GameObjects ───────────────────────────
        // These will be populated in Milestone 4.
        // Leave them unassigned for now.
        [Header("Visual Roots (Milestone 4 — leave null for now)")]
        public GameObject visualRootModeA;
        public GameObject visualRootModeB;
        public GameObject visualRootModeC;

        // ── Public State ──────────────────────────────────────
        public VisualMode ActiveMode { get; private set; }

        // ── Lifecycle ─────────────────────────────────────────

        public void Initialize(GameData data)
        {
            gameData = data;
            ApplyMode(data != null ? data.selectedVisualMode : VisualMode.ModeA_Realistic);
        }

        public void ApplyMode(VisualMode mode)
        {
            ActiveMode = mode;

            // ── Milestone 1: just log ─────────────────────────
            Debug.Log($"[VisualModeManager] Visual mode set to: {mode}. " +
                      "Full visual switching will be implemented in Milestone 4.");

            // ── Milestone 4: activate the correct root ────────
            // (Uncomment and extend this in Milestone 4)
            /*
            if (visualRootModeA != null) visualRootModeA.SetActive(mode == VisualMode.ModeA_Realistic);
            if (visualRootModeB != null) visualRootModeB.SetActive(mode == VisualMode.ModeB_Stylized);
            if (visualRootModeC != null) visualRootModeC.SetActive(mode == VisualMode.ModeC_TopDown);

            switch (mode)
            {
                case VisualMode.ModeA_Realistic: ApplyModeASettings(); break;
                case VisualMode.ModeB_Stylized:  ApplyModeBSettings(); break;
                case VisualMode.ModeC_TopDown:   ApplyModeCSettings(); break;
            }
            */
        }

        // ── Milestone 4 methods (stubs) ───────────────────────

        private void ApplyModeASettings()
        {
            // Swap to URP_ModeA_Renderer
            // Enable Cinemachine drone camera
            // Enable post-processing (bloom, vignette)
            // Apply dark industrial materials
        }

        private void ApplyModeBSettings()
        {
            // Swap to URP_ModeB_Renderer (no post-process)
            // Simple perspective camera
            // Apply bright flat materials
        }

        private void ApplyModeCSettings()
        {
            // Switch main camera to orthographic
            // Position camera directly above building
            // Apply flat top-down materials
        }
    }
}
