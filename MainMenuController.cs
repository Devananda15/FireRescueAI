// FireRescueAI — MainMenuController.cs
// ----------------------------------------------------------
// A simple main menu that lets the player choose their visual
// mode and control mode, then loads the game scene.
//
// In Milestone 1 this uses OnGUI() (no Canvas required).
// In Milestone 4 it will be replaced with a properly styled
// UI that matches each visual mode's aesthetic.
//
// HOW TO USE:
//   1. Create a new scene called "MainMenu"
//   2. Create an empty GameObject in it
//   3. Attach this script to that object
//   4. Assign the GameData asset in the Inspector
//   5. Enter the exact scene name of your game scene in
//      the "Game Scene Name" field (default: "FireRescue_Main")
//   6. Add BOTH scenes to File → Build Settings (in order:
//      MainMenu first, then FireRescue_Main)
// ----------------------------------------------------------

using UnityEngine;
using UnityEngine.SceneManagement;
using FireRescueAI.Core;

namespace FireRescueAI.UI
{
    public class MainMenuController : MonoBehaviour
    {
        // ── Inspector ─────────────────────────────────────────
        [Header("Data")]
        [Tooltip("Drag the GameData ScriptableObject asset here.")]
        public GameData gameData;

        [Header("Scene Loading")]
        [Tooltip("Exact name of the game scene (without .unity extension).")]
        public string gameSceneName = "FireRescue_Main";

        // ── GUI styles ────────────────────────────────────────
        private GUIStyle _titleStyle;
        private GUIStyle _buttonStyle;
        private GUIStyle _labelStyle;
        private GUIStyle _selectedStyle;
        private bool     _stylesReady;

        // ── Lifecycle ─────────────────────────────────────────

        private void Start()
        {
            // If GameData was not assigned in the Inspector,
            // try loading it from a Resources folder.
            if (gameData == null)
                gameData = Resources.Load<GameData>("GameData");

            if (gameData == null)
                Debug.LogWarning("[MainMenu] GameData not assigned and not found in Resources. " +
                                 "Create it via right-click → Create → FireRescueAI → GameData " +
                                 "and assign it in the Inspector.");
        }

        private void BuildStyles()
        {
            if (_stylesReady) return;

            _titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize  = 36,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            _titleStyle.normal.textColor = Color.white;

            _buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize  = 18,
                fontStyle = FontStyle.Bold
            };

            _selectedStyle = new GUIStyle(_buttonStyle);
            _selectedStyle.normal.textColor  = Color.cyan;
            _selectedStyle.active.textColor  = Color.cyan;
            _selectedStyle.hover.textColor   = Color.cyan;

            _labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 16
            };
            _labelStyle.normal.textColor = new Color(0.9f, 0.9f, 0.9f);

            _stylesReady = true;
        }

        private void OnGUI()
        {
            BuildStyles();

            int sw = Screen.width;
            int sh = Screen.height;

            // ── Background ────────────────────────────────────
            GUI.color = new Color(0.07f, 0.07f, 0.12f, 1f);
            GUI.DrawTexture(new Rect(0, 0, sw, sh), Texture2D.whiteTexture);
            GUI.color = Color.white;

            // ── Title ─────────────────────────────────────────
            GUI.Label(new Rect(0, sh * 0.08f, sw, 50),
                "FireRescue AI", _titleStyle);

            var subStyle = new GUIStyle(_labelStyle) { alignment = TextAnchor.MiddleCenter, fontSize = 14 };
            subStyle.normal.textColor = new Color(0.6f, 0.8f, 1f);
            GUI.Label(new Rect(0, sh * 0.08f + 52, sw, 24),
                "Human Behaviour-Aware UAV Search & Rescue", subStyle);

            // ── Visual Mode selection ─────────────────────────
            float panelW = 440f;
            float panelX = (sw - panelW) / 2f;
            float y      = sh * 0.25f;

            GUI.Label(new Rect(panelX, y, panelW, 28), "SELECT VISUAL MODE", _labelStyle);
            y += 32f;

            DrawModeButton(ref y, panelX, panelW,
                VisualMode.ModeA_Realistic,
                "Mode A — Realistic 3D",
                "Cinematic drone camera, dark industrial environment");

            DrawModeButton(ref y, panelX, panelW,
                VisualMode.ModeB_Stylized,
                "Mode B — Stylized 3D",
                "Colourful simplified graphics, better performance");

            DrawModeButton(ref y, panelX, panelW,
                VisualMode.ModeC_TopDown,
                "Mode C — Top-Down 3D",
                "Overhead view, full map visibility");

            // ── Control Mode selection ────────────────────────
            y += 16f;
            GUI.Label(new Rect(panelX, y, panelW, 28), "CONTROL MODE", _labelStyle);
            y += 32f;

            DrawControlButton(ref y, panelX, panelW,
                ControlMode.Manual, "Manual (Keyboard)");
            DrawControlButton(ref y, panelX, panelW,
                ControlMode.AIControlled, "AI Controlled (Milestone 5)");

            // ── START button ──────────────────────────────────
            y += 20f;
            if (GUI.Button(new Rect(panelX, y, panelW, 50), "START MISSION", _buttonStyle))
                StartGame();

            // ── Footer ────────────────────────────────────────
            var footStyle = new GUIStyle(_labelStyle)
                { fontSize = 11, alignment = TextAnchor.MiddleCenter };
            footStyle.normal.textColor = new Color(0.5f, 0.5f, 0.5f);
            GUI.Label(new Rect(0, sh - 28, sw, 24),
                "B.Tech Research Project  |  Unity 6 + URP  |  Milestone 1",
                footStyle);
        }

        private void DrawModeButton(
            ref float y,
            float x, float w,
            VisualMode mode,
            string title,
            string subtitle)
        {
            if (gameData == null) return;

            bool selected = gameData.selectedVisualMode == mode;
            var  style    = selected ? _selectedStyle : _buttonStyle;

            string label = selected ? $"✔  {title}" : title;

            if (GUI.Button(new Rect(x, y, w, 44), label, style))
            {
                if (gameData != null) gameData.selectedVisualMode = mode;
            }

            var subStyle = new GUIStyle(_labelStyle) { fontSize = 11 };
            subStyle.normal.textColor = new Color(0.65f, 0.65f, 0.65f);
            GUI.Label(new Rect(x + 8, y + 26, w - 8, 16), subtitle, subStyle);

            y += 52f;
        }

        private void DrawControlButton(
            ref float y,
            float x, float w,
            ControlMode mode,
            string label)
        {
            if (gameData == null) return;

            bool selected = gameData.selectedControlMode == mode;
            var  style    = selected ? _selectedStyle : _buttonStyle;

            string btn = selected ? $"✔  {label}" : label;

            if (GUI.Button(new Rect(x, y, w, 38), btn, style))
                gameData.selectedControlMode = mode;

            y += 44f;
        }

        private void StartGame()
        {
            if (string.IsNullOrEmpty(gameSceneName))
            {
                Debug.LogError("[MainMenu] Game scene name is empty. " +
                               "Set it in the MainMenuController inspector.");
                return;
            }

            SceneManager.LoadScene(gameSceneName);
        }
    }
}
