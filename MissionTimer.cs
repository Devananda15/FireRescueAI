// FireRescueAI — MissionTimer.cs
// ----------------------------------------------------------
// A countdown timer for the rescue mission.
//
// Usage:
//   missionTimer.StartTimer();  // begin countdown
//   missionTimer.StopTimer();   // pause
//   missionTimer.ResetTimer();  // go back to full duration
//   string s = missionTimer.GetFormattedTime(); // "04:32"
//
// When time reaches 0, it fires GameEvents.PublishTimerExpired()
// so the SimulationManager can end the mission.
// ----------------------------------------------------------

using UnityEngine;
using FireRescueAI.Utilities;

namespace FireRescueAI.Core
{
    public class MissionTimer : MonoBehaviour
    {
        // ── Inspector Settings ────────────────────────────────
        [Header("Timer Settings")]
        [Tooltip("Total mission length in seconds.")]
        public float missionDuration = 300f;

        // ── Public Read-Only Properties ───────────────────────
        /// Seconds remaining (counts down to zero).
        public float TimeRemaining => _timeRemaining;

        /// True once the timer reaches zero.
        public bool IsExpired => _timeRemaining <= 0f;

        /// True while the timer is counting down.
        public bool IsRunning => _isRunning;

        /// Normalised progress: 1.0 = full, 0.0 = expired.
        public float NormalisedProgress
            => missionDuration > 0f ? _timeRemaining / missionDuration : 0f;

        // ── Private State ─────────────────────────────────────
        private float _timeRemaining;
        private bool  _isRunning;
        private float _lastWholeSecond; // used to avoid spamming every frame

        // ── Lifecycle ─────────────────────────────────────────

        private void Awake()
        {
            _timeRemaining = missionDuration;
            _lastWholeSecond = missionDuration;
        }

        private void Update()
        {
            if (!_isRunning || _timeRemaining <= 0f) return;

            _timeRemaining -= Time.deltaTime;

            if (_timeRemaining <= 0f)
            {
                _timeRemaining = 0f;
                _isRunning     = false;
                GameEvents.PublishTimerExpired();
            }
        }

        // ── Public Controls ───────────────────────────────────

        /// Start (or resume) the countdown.
        public void StartTimer()
        {
            if (_timeRemaining <= 0f) ResetTimer();
            _isRunning = true;
        }

        /// Pause the countdown without resetting.
        public void StopTimer()  => _isRunning = false;

        /// Reset to full duration (does not auto-start).
        public void ResetTimer()
        {
            _timeRemaining   = missionDuration;
            _lastWholeSecond = missionDuration;
            _isRunning       = false;
        }

        // ── Formatting ────────────────────────────────────────

        /// Returns the time remaining as "MM:SS" (e.g. "04:32").
        public string GetFormattedTime()
        {
            int totalSeconds = Mathf.CeilToInt(_timeRemaining);
            int minutes      = totalSeconds / 60;
            int seconds      = totalSeconds % 60;
            return $"{minutes:D2}:{seconds:D2}";
        }
    }
}
