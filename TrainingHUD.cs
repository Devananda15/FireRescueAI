// FireRescueAI – TrainingHUD.cs
// ============================================================
// On-screen training statistics overlay drawn with OnGUI.
// Visible only when DroneAgent.IsTrainingMode == true (T key).
//
// Works in both STUB mode (no ML-Agents) and FULL mode.
// In stub mode it shows zeroed stats with a warning.
// ============================================================

using UnityEngine;
using FireRescueAI.Fire;
using FireRescueAI.Victims;

namespace FireRescueAI.UI
{
    public class TrainingHUD : MonoBehaviour
    {
        // ── References ────────────────────────────────────────────────────
        private AI.DroneAgent  _agent;
        private VictimManager  _victimManager;
        private FireSystem     _fireSystem;

        // ── GUI styles ────────────────────────────────────────────────────
        private GUIStyle _boxStyle;
        private GUIStyle _labelStyle;
        private GUIStyle _headerStyle;
        private GUIStyle _warnStyle;
        private bool     _stylesReady;

        // ── Public setup ──────────────────────────────────────────────────
        public void Initialize(AI.DroneAgent agent, VictimManager vm, FireSystem fs)
        {
            _agent         = agent;
            _victimManager = vm;
            _fireSystem    = fs;
        }

        private void EnsureStyles()
        {
            if (_stylesReady) return;
            _stylesReady = true;

            _boxStyle = new GUIStyle(GUI.skin.box)
            {
                padding = new RectOffset(10, 10, 8, 8)
            };

            _labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize  = 13,
                fontStyle = FontStyle.Normal
            };
            _labelStyle.normal.textColor = new Color(0.9f, 0.9f, 0.9f);

            _headerStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize  = 14,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            _headerStyle.normal.textColor = new Color(0.3f, 1.0f, 0.5f);

            _warnStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize  = 12,
                fontStyle = FontStyle.Italic,
                wordWrap  = true
            };
            _warnStyle.normal.textColor = new Color(1.0f, 0.8f, 0.2f);
        }

        private void OnGUI()
        {
            EnsureStyles();

            if (_agent == null) return;
            // Only show RL training stats box when PPO training is active
            if (_agent.CurrentControlMode != Core.ControlMode.StandardPPO &&
                _agent.CurrentControlMode != Core.ControlMode.RecurrentPPO)
            {
                return;
            }

#if MLAGENTS_PRESENT
            bool communicatorOn = Unity.MLAgents.Academy.Instance.IsCommunicatorOn;
#else
            bool communicatorOn = false;
#endif

            int   victimCount = _victimManager != null ? _victimManager.RemainingCount : 0;
            int   fireCount   = _fireSystem    != null ? _fireSystem.ActiveFireCount   : 0;
            int   episodeNum  = _agent.CompletedEpisodes + 1;
            float reward      = _agent.GetCumulativeReward();

            const float PW = 280f;
            float ph = !communicatorOn ? 210f : 160f;

            // Offset below the main HUD left panel (y = 150) so they don't overlap
            GUI.color = new Color(0f, 0f, 0f, 0.75f);
            GUI.Box(new Rect(10, 150, PW, ph), "", _boxStyle);
            GUI.color = Color.white;

            float y = 158f;

            // Header
            string modeName = _agent.CurrentControlMode == Core.ControlMode.RecurrentPPO ? "RECURRENT PPO (LSTM)" : "STANDARD PPO";
            GUI.Label(new Rect(15, y, PW - 10, 22), $"• {modeName}", _headerStyle);
            y += 24f;

            string commText = communicatorOn ? "Python Link: CONNECTED (Training)" : "Python Link: NOT CONNECTED";
            Color prevColor = _labelStyle.normal.textColor;
            _labelStyle.normal.textColor = communicatorOn ? new Color(0.2f, 1f, 0.4f) : new Color(1f, 0.4f, 0.4f);
            GUI.Label(new Rect(18, y, PW - 20, 18), commText, _labelStyle);
            _labelStyle.normal.textColor = prevColor;
            y += 20f;

            if (!communicatorOn)
            {
                GUI.Label(new Rect(18, y, PW - 20, 42),
                    "Run train_recurrent_ppo.bat or train_standard_ppo.bat in terminal to train.",
                    _warnStyle);
                y += 44f;
            }

            int   totalVictims = _victimManager != null && _victimManager.TotalVictims > 0 ? _victimManager.TotalVictims : 5;
            GUI.Label(new Rect(18, y, PW - 20, 18), $"Episode:       {episodeNum}", _labelStyle); y += 20f;
            GUI.Label(new Rect(18, y, PW - 20, 18), $"Reward:        {reward:F2}",   _labelStyle); y += 20f;
            GUI.Label(new Rect(18, y, PW - 20, 18), $"Victims left:  {victimCount} / {totalVictims}", _labelStyle); y += 20f;
            GUI.Label(new Rect(18, y, PW - 20, 18), $"Active fires:  {fireCount}",   _labelStyle); y += 20f;
            GUI.Label(new Rect(18, y, PW - 20, 18), "Press [2] for Autonomous BFS Rescue", _labelStyle);
        }
    }
}
