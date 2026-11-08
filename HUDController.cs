// FireRescueAI — HUDController.cs
// ----------------------------------------------------------
// Draws the in-game HUD using Unity's immediate-mode OnGUI()
// API. This requires no Canvas setup in the Editor — it just
// works when the component is attached to any active object.
//
// INFORMATION DISPLAYED:
//   Top bar      — FIRERESCUE AI title + mission timer + drone status
//   Top-right    — Speed, altitude, camera mode indicator
//   Left panel   — Victims rescued X/3 + fire count
//   Bottom bar   — Control hints (pickup, camera toggle, restart)
//   Centre       — Danger zone / collision warning (red flash)
//   Centre       — Mission result overlay when finished
// ----------------------------------------------------------

using UnityEngine;
using FireRescueAI.AI;
using FireRescueAI.Building;
using FireRescueAI.Core;
using FireRescueAI.Drone;
using FireRescueAI.Fire;
using FireRescueAI.Rescue;
using FireRescueAI.Utilities;
using FireRescueAI.Victims;

namespace FireRescueAI.UI
{
    public class HUDController : MonoBehaviour
    {
        // ── References (set by Initialize()) ─────────────────────────
        private MissionTimer          _timer;
        private DroneController       _drone;
        private DroneCollisionMonitor _collision;
        private SimulationManager     _sim;
        private VisualMode            _visualMode;
        private GameData              _gameData;
        private DroneAgent            _agent;

        // New systems (can be null for backward compat)
        private FireSystem         _fire;
        private SmokeSystem        _smoke;
        private VictimManager      _victims;
        private RescueMechanic     _rescue;
        private DangerZoneManager  _danger;
        private BuildingLayout     _building;

        // ── GUI Styles ────────────────────────────────────────────────
        private GUIStyle _labelStyle;
        private GUIStyle _timerStyle;
        private GUIStyle _warnStyle;
        private GUIStyle _resultStyle;
        private bool     _stylesReady;

        // ── Lifecycle ─────────────────────────────────────────────────

        /// Called by SceneBootstrapper after all systems are created.
        public void Initialize(
            MissionTimer          timer,
            DroneController       drone,
            DroneCollisionMonitor collision,
            SimulationManager     sim,
            VisualMode            mode,
            GameData              gameData      = null,
            FireSystem            fire          = null,
            SmokeSystem           smoke         = null,
            VictimManager         victims       = null,
            RescueMechanic        rescue        = null,
            DangerZoneManager     danger        = null,
            BuildingLayout        building      = null,
            DroneAgent            agent         = null)
        {
            _timer      = timer;
            _drone      = drone;
            _collision  = collision;
            _sim        = sim;
            _visualMode = mode;
            _gameData   = gameData;
            _fire       = fire;
            _smoke      = smoke;
            _victims    = victims;
            _rescue     = rescue;
            _danger     = danger;
            _building   = building;
            _agent      = agent != null ? agent : (_drone != null ? _drone.GetComponent<DroneAgent>() : null);
        }

        private void BuildStyles()
        {
            if (_stylesReady) return;

            _labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize  = 16,
                fontStyle = FontStyle.Bold
            };
            _labelStyle.normal.textColor = Color.white;

            _timerStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize  = 28,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            _timerStyle.normal.textColor = Color.white;

            _warnStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize  = 22,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            _warnStyle.normal.textColor = Color.red;

            _resultStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize  = 36,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };

            _stylesReady = true;
        }

        private void OnGUI()
        {
            BuildStyles();

            int sw = Screen.width;
            int sh = Screen.height;

            // ── Top bar (semi-transparent black) ─────────────────────
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(new Rect(0, 0, sw, 38), Texture2D.whiteTexture);
            GUI.color = Color.white;

            // FIRERESCUE AI title (top-left)
            var titleStyle = new GUIStyle(_labelStyle) { fontSize = 17 };
            titleStyle.normal.textColor = new Color(0.30f, 0.85f, 1.0f); // cyan
            GUI.Label(new Rect(10, 8, 170, 24), "FIRERESCUE AI", titleStyle);

            // Control Mode Badge (top-left beside title)
            ControlMode cMode = _drone != null ? _drone.CurrentControlMode : ControlMode.Manual;
            var modeStyle = new GUIStyle(_labelStyle) { fontSize = 13, fontStyle = FontStyle.Bold };
            string modeText;
            switch (cMode)
            {
                case ControlMode.HeuristicAutonomous:
                    modeStyle.normal.textColor = new Color(0.2f, 1f, 0.4f); // bright green
                    modeText = "MODE: HEURISTIC AUTONOMOUS [T: Manual]";
                    break;
                case ControlMode.StandardPPO:
                    modeStyle.normal.textColor = new Color(0.3f, 0.85f, 1f); // cyan
                    modeText = "MODE: STANDARD PPO [T: Manual]";
                    break;
                case ControlMode.RecurrentPPO:
                    modeStyle.normal.textColor = new Color(1f, 0.4f, 0.9f); // magenta
                    modeText = "MODE: RECURRENT PPO [T: Manual]";
                    break;
                default:
                    modeStyle.normal.textColor = new Color(1f, 0.85f, 0.2f); // gold/amber
                    modeText = "MODE: MANUAL [T: Autonomous AI]";
                    break;
            }
            GUI.Label(new Rect(185, 9, 340, 22), modeText, modeStyle);

            // Mission timer (top-centre)
            string timerText = _timer != null ? _timer.GetFormattedTime() : "--:--";
            _timerStyle.normal.textColor =
                (_timer != null && _timer.TimeRemaining < 30f) ? Color.red : Color.white;
            GUI.Label(new Rect(sw / 2f - 60, 2, 120, 36), timerText, _timerStyle);

            // Drone status (top-right)
            if (_drone != null)
            {
                string dStatus = _drone.Speed > 0.3f ? "FLYING" : "HOVERING";
                var smallLabel = new GUIStyle(_labelStyle) { fontSize = 14 };
                smallLabel.normal.textColor = Color.white;
                GUI.Label(new Rect(sw - 230, 4,  220, 20),
                    $"Status: {dStatus}", smallLabel);
                GUI.Label(new Rect(sw - 230, 20, 220, 18),
                    $"Speed: {_drone.Speed:F1} m/s   Alt: {_drone.Altitude:F1} m", smallLabel);
            }

            // ── Left status panel ─────────────────────────────────────
            GUI.color = new Color(0f, 0f, 0f, 0.40f);
            GUI.DrawTexture(new Rect(0, 38, 250, 105), Texture2D.whiteTexture);
            GUI.color = Color.white;

            int rescued = _victims != null
                ? (_victims.ActiveVictims.Count - _victims.RemainingCount)
                : (_sim != null ? _sim.VictimsRescued : 0);
            int total   = (_victims != null && _victims.TotalVictims > 0)
                ? _victims.TotalVictims
                : (_gameData != null ? _gameData.victimCount : 5);

            var victimStyle = new GUIStyle(_labelStyle) { fontSize = 14 };
            victimStyle.normal.textColor =
                rescued >= total ? Color.green : new Color(1f, 0.85f, 0.3f);
            GUI.Label(new Rect(10, 42, 235, 20),
                $"Victims: {rescued}/{total} rescued", victimStyle);

            // Victim in range indicator
            if (_rescue != null && _rescue.NearestVictim != null)
            {
                var hintStyle2 = new GUIStyle(_labelStyle) { fontSize = 12 };
                hintStyle2.normal.textColor = new Color(0.3f, 1f, 0.4f);
                GUI.Label(new Rect(10, 60, 235, 18), "  [E] Rescue nearby victim", hintStyle2);
            }
            else if (_rescue != null && _rescue.IsCarryingVictim)
            {
                var hintStyle2 = new GUIStyle(_labelStyle) { fontSize = 12 };
                hintStyle2.normal.textColor = new Color(0.3f, 0.8f, 1f);
                GUI.Label(new Rect(10, 60, 235, 18), "  Carrying victim — fly to EXIT", hintStyle2);
            }

            // Fire & Smoke status
            int fireCount = _fire != null ? _fire.ActiveFireCount : 0;
            float localSmoke = (_smoke != null && _drone != null)
                ? _smoke.GetSmokeDensityAt(_drone.transform.position) : 0f;

            var fireStyle = new GUIStyle(_labelStyle) { fontSize = 13 };
            fireStyle.normal.textColor = fireCount > 0 ? new Color(1f, 0.4f, 0.1f) : Color.green;
            GUI.Label(new Rect(10, 78, 235, 18),
                $"Fire Rooms: {fireCount} | Smoke: {(localSmoke > 0.05f ? $"{localSmoke*100f:F0}%" : "Clear")}", fireStyle);

            // AI Subtask indicator
            if (cMode != ControlMode.Manual && _agent != null)
            {
                var subtaskStyle = new GUIStyle(_labelStyle) { fontSize = 11, fontStyle = FontStyle.Italic };
                subtaskStyle.normal.textColor = new Color(0.3f, 0.95f, 0.9f);
                GUI.Label(new Rect(10, 96, 235, 18), $"AI: {_agent.CurrentSubtask}", subtaskStyle);
            }

            // ── Danger zone warning (centre, pulsing) ─────────────────
            bool inDanger = _danger != null && _drone != null &&
                            _danger.IsDroneInDangerZone(_drone.transform.position);
            // NOTE: _drone is a DroneController (MonoBehaviour), so _drone.transform is valid
            if (inDanger)
            {
                float pulse = Mathf.Sin(Time.time * 8f) * 0.5f + 0.5f;
                GUI.color = new Color(1f, 0.2f, 0f, pulse);
                GUI.Label(new Rect(sw / 2f - 160, sh / 2f - 48, 320, 40),
                    "⚠ DANGER ZONE — FIRE NEARBY ⚠", _warnStyle);
                GUI.color = Color.white;
            }

            // Collision warning
            if (_collision != null && _collision.IsColliding)
            {
                float pulse = Mathf.Sin(Time.time * 10f) * 0.5f + 0.5f;
                GUI.color = new Color(1f, 0f, 0f, pulse);
                GUI.Label(new Rect(sw / 2f - 160, sh / 2f - 24, 320, 40),
                    "WARNING — COLLISION DETECTED", _warnStyle);
                GUI.color = Color.white;
            }

            // ── Mission result overlay ────────────────────────────────
            if (_sim != null && !_sim.MissionActive)
                DrawMissionResult(_sim.CurrentResult, sw, sh);

            // ── Bottom control hints ──────────────────────────────────
            GUI.color = new Color(0f, 0f, 0f, 0.45f);
            GUI.DrawTexture(new Rect(0, sh - 54, sw, 54), Texture2D.whiteTexture);
            GUI.color = Color.white;

            var hintStyle = new GUIStyle(_labelStyle) { fontSize = 12 };
            hintStyle.normal.textColor = new Color(0.9f, 0.9f, 0.9f, 0.80f);
            GUI.Label(new Rect(10, sh - 50, sw - 20, 22),
                "WASD: Move  |  Space: Up  |  LCtrl: Down  |  E: Rescue  |  T: Toggle AI  |  [1-4]: Manual/Heuristic/PPO/Recurrent  |  F: Camera  |  R: Restart",
                hintStyle);

            // ── PiP border + label (bottom-right corner) ─────────────
            DrawPIPBorder(sw, sh);
        }

        private void DrawPIPBorder(int sw, int sh)
        {
            // Match DronePOVCamera.PipRect: x=0.72, y=0.02, w=0.27, h=0.22
            float px = sw * 0.72f;
            float py = sh * 0.02f;
            float pw = sw * 0.27f;
            float ph = sh * 0.22f;

            // Cyan border around PiP
            GUI.color = new Color(0.2f, 0.85f, 1f, 0.85f);
            float t = 2f; // border thickness
            GUI.DrawTexture(new Rect(px,      py,      pw, t),  Texture2D.whiteTexture); // top
            GUI.DrawTexture(new Rect(px,      py+ph-t, pw, t),  Texture2D.whiteTexture); // bottom
            GUI.DrawTexture(new Rect(px,      py,      t,  ph), Texture2D.whiteTexture); // left
            GUI.DrawTexture(new Rect(px+pw-t, py,      t,  ph), Texture2D.whiteTexture); // right
            GUI.color = Color.white;

            // PiP label
            var pipLabelStyle = new GUIStyle(_labelStyle) { fontSize = 11, alignment = TextAnchor.UpperLeft };
            pipLabelStyle.normal.textColor = new Color(0.2f, 0.85f, 1f);
            GUI.Label(new Rect(px + 4, py + 3, pw, 18), "DRONE POV  [V: cycle mode]", pipLabelStyle);
        }

        private void DrawMissionResult(MissionResult result, int sw, int sh)
        {
            GUI.color = new Color(0f, 0f, 0f, 0.65f);
            GUI.DrawTexture(new Rect(0, 0, sw, sh), Texture2D.whiteTexture);
            GUI.color = Color.white;

            string headline;
            Color  headColor;

            switch (result)
            {
                case MissionResult.Success:
                    headline  = "MISSION COMPLETE — ALL RESCUED!";
                    headColor = Color.green;
                    break;
                case MissionResult.Failed_TimeOut:
                    headline  = "TIME IS UP";
                    headColor = Color.red;
                    break;
                case MissionResult.Failed_Crash:
                    headline  = "DRONE DESTROYED";
                    headColor = Color.red;
                    break;
                default:
                    headline  = "MISSION ENDED";
                    headColor = Color.yellow;
                    break;
            }

            _resultStyle.normal.textColor = headColor;
            GUI.Label(
                new Rect(sw / 2f - 250, sh / 2f - 50, 500, 60),
                headline,
                _resultStyle);

            var subStyle = new GUIStyle(_labelStyle) { alignment = TextAnchor.MiddleCenter };
            subStyle.normal.textColor = Color.white;
            GUI.Label(
                new Rect(sw / 2f - 200, sh / 2f + 20, 400, 30),
                "Press R to restart  |  Esc for menu",
                subStyle);
        }

        // R-key is now handled by SceneBootstrapper.HandleMissionRestart()
        // to avoid double-handling, nothing in Update here.
    }
}
