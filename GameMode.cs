// FireRescueAI — GameMode.cs
// ----------------------------------------------------------
// Defines the core enumerations used throughout the project.
// Any script that needs to know "which visual mode?" or
// "which control mode?" imports this file.
// ----------------------------------------------------------

namespace FireRescueAI.Core
{
    // ── Which visual skin is active? ─────────────────────────
    public enum VisualMode
    {
        ModeA_Realistic = 0,   // Dark industrial, cinematic drone camera
        ModeB_Stylized  = 1,   // Colourful simplified, good performance
        ModeC_TopDown   = 2    // Orthographic overhead view
    }

    // ── How is the drone controlled? ─────────────────────────
    public enum ControlMode
    {
        Manual              = 0,   // Player controls via keyboard (DroneInput)
        HeuristicAutonomous = 1,   // Rule-based / waypoint autonomous heuristic (no Python)
        StandardPPO         = 2,   // ML-Agents Standard PPO policy
        RecurrentPPO        = 3,   // ML-Agents Recurrent PPO (LSTM) policy
        AIControlled        = 1,   // Backward compatibility alias
        RLTraining          = 2    // Backward compatibility alias
    }

    // ── State machine for each victim ────────────────────────
    public enum VictimState
    {
        Calm    = 0,   // Wandering slowly, not alarmed
        Panic   = 1,   // Running in random directions
        Flee    = 2,   // Moving toward nearest exit
        Hide    = 3,   // Found a corner, crouching
        Trapped = 4,   // Cannot move (fire/debris blocking)
        Follow  = 5,   // Drone detected them; trailing the drone
        Rescued = 6    // Successfully rescued and brought to exit
    }

    // ── How did the mission end? ─────────────────────────────
    public enum MissionResult
    {
        InProgress,       // Mission is still running
        Success,          // All victims rescued
        Failed_TimeOut,   // Timer hit zero
        Failed_Crash      // Drone destroyed (future milestone)
    }
}
