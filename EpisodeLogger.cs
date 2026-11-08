// FireRescueAI – EpisodeLogger.cs
// ============================================================
// Writes per-episode metrics to a CSV file for research analysis.
//
// COLUMNS WRITTEN:
//   Episode, PolicyName, Seed, TotalReward, VictimsRescued,
//   VictimsTotal, AllRescued, UAVSurvived, MissionTimeSecs,
//   StepsTaken, Collisions, FireEncounters, PathLength,
//   RewardPickup, RewardDeliver, RewardTime, RewardDistance,
//   RewardFire, RewardCollision, RewardRisk, RewardExplore
//
// OUTPUT FILE:
//   results/episode_log_<PolicyName>_<timestamp>.csv
// ============================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace FireRescueAI.Training
{
    public class EpisodeLogger : MonoBehaviour
    {
        // ── Public API ────────────────────────────────────────────────────
        public static EpisodeLogger Instance { get; private set; }

        [Header("Settings")]
        public string policyName   = "StandardPPO";
        public string outputFolder = "results";

        // ── Per-episode accumulators (reset each episode) ─────────────────
        private EpisodeRecord _current = new();
        private int           _episodeCount;
        private string        _csvPath;
        private bool          _headerWritten;

        // ── Lifetime stats ────────────────────────────────────────────────
        private readonly List<EpisodeRecord> _history = new();

        // ─────────────────────────────────────────────────────────────────
        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        void Start()
        {
            if (!Directory.Exists(outputFolder))
                Directory.CreateDirectory(outputFolder);

            string ts  = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            _csvPath   = Path.Combine(outputFolder,
                             $"episode_log_{policyName}_{ts}.csv");
            Debug.Log($"[EpisodeLogger] Logging to: {_csvPath}");
        }

        // ─────────────────────────────────────────────────────────────────
        // Called at the START of each episode
        // ─────────────────────────────────────────────────────────────────
        public void BeginEpisode(int seed = 0)
        {
            _episodeCount++;
            _current = new EpisodeRecord
            {
                Episode    = _episodeCount,
                PolicyName = policyName,
                Seed       = seed,
                StartTime  = Time.time
            };
        }

        // ─────────────────────────────────────────────────────────────────
        // Reward component loggers — call from DroneAgent
        // ─────────────────────────────────────────────────────────────────
        public void LogPickup()           => _current.RewardPickup    += 3f;
        public void LogDeliver()          => _current.RewardDeliver   += 10f;
        public void LogTimePenalty()      => _current.RewardTime      -= 0.01f;
        public void LogDistanceReward()   => _current.RewardDistance  += 0.01f;
        public void LogFirePenalty()      => _current.RewardFire      -= 10f;
        public void LogCollision()        { _current.RewardCollision  -= 0.5f;  _current.Collisions++; }
        public void LogRiskPenalty(float r) => _current.RewardRisk    += r;
        public void LogRisk(float r)        => LogRiskPenalty(r);
        public void LogUselessGrab()        => _current.RewardCollision -= 0.05f;
        public void LogBadGrab()            => LogUselessGrab();
        public void LogExploration()      => _current.RewardExplore   += 0.005f;
        public void LogFireEncounter()    => _current.FireEncounters++;
        public void LogStep(float dist)   => _current.PathLength      += dist;
        public void LogStep()             => _current.StepsTaken++;

        // ─────────────────────────────────────────────────────────────────
        // Called at the END of each episode
        // ─────────────────────────────────────────────────────────────────
        public void EndEpisode(float cumulativeReward, int victimsRescued, int victimsTotal,
                               bool uavSurvived, float missionTime, int stepCount)
        {
            _current.StepsTaken = stepCount;
            EndEpisode(victimsRescued, victimsTotal, uavSurvived);
            _current.MissionTime = missionTime;
            _current.TotalReward = cumulativeReward;
        }

        public void EndEpisode(int victimsRescued, int victimsTotal,
                               bool uavSurvived, float missionTime, int stepCount)
        {
            _current.StepsTaken = stepCount;
            EndEpisode(victimsRescued, victimsTotal, uavSurvived);
            _current.MissionTime = missionTime;
        }

        public void EndEpisode(int victimsRescued, int victimsTotal,
                               bool uavSurvived)
        {
            _current.VictimsRescued = victimsRescued;
            _current.VictimsTotal   = victimsTotal;
            _current.AllRescued     = (victimsRescued >= victimsTotal) && victimsTotal > 0;
            _current.UAVSurvived    = uavSurvived;
            _current.MissionTime    = Time.time - _current.StartTime;

            _current.TotalReward =
                _current.RewardPickup  + _current.RewardDeliver +
                _current.RewardTime    + _current.RewardDistance +
                _current.RewardFire    + _current.RewardCollision +
                _current.RewardRisk    + _current.RewardExplore;

            _history.Add(_current);
            WriteRow(_current);

            Debug.Log($"[EpisodeLogger] Ep {_current.Episode} | " +
                      $"Reward: {_current.TotalReward:F2} | " +
                      $"Rescued: {victimsRescued}/{victimsTotal} | " +
                      $"UAV: {(uavSurvived ? "OK" : "LOST")}");
        }

        // ─────────────────────────────────────────────────────────────────
        // Returns a summary dict of rolling averages over last N episodes
        // ─────────────────────────────────────────────────────────────────
        public EpisodeSummary GetRollingSummary(int lastN = 100)
        {
            int start = Mathf.Max(0, _history.Count - lastN);
            var window = _history.GetRange(start, _history.Count - start);
            if (window.Count == 0) return new EpisodeSummary();

            var s = new EpisodeSummary();
            foreach (var r in window)
            {
                s.MeanReward       += r.TotalReward;
                s.MeanVictims      += r.VictimsRescued;
                s.SuccessRate      += r.AllRescued ? 1f : 0f;
                s.UAVSurvivalRate  += r.UAVSurvived ? 1f : 0f;
                s.MeanTime         += r.MissionTime;
                s.MeanPath         += r.PathLength;
                s.MeanCollisions   += r.Collisions;
            }
            int n = window.Count;
            s.MeanReward      /= n; s.MeanVictims  /= n;
            s.SuccessRate     /= n; s.UAVSurvivalRate /= n;
            s.MeanTime        /= n; s.MeanPath     /= n;
            s.MeanCollisions  /= n;
            s.EpisodeCount     = _episodeCount;
            return s;
        }

        // ─────────────────────────────────────────────────────────────────
        // CSV writing
        // ─────────────────────────────────────────────────────────────────
        private static readonly string CsvHeader =
            "Episode,PolicyName,Seed,TotalReward,VictimsRescued,VictimsTotal," +
            "AllRescued,UAVSurvived,MissionTimeSecs,StepsTaken,Collisions," +
            "FireEncounters,PathLength,RewardPickup,RewardDeliver,RewardTime," +
            "RewardDistance,RewardFire,RewardCollision,RewardRisk,RewardExplore";

        private void WriteRow(EpisodeRecord r)
        {
            var sb = new StringBuilder();
            if (!_headerWritten)
            {
                sb.AppendLine(CsvHeader);
                _headerWritten = true;
            }
            sb.Append($"{r.Episode},{r.PolicyName},{r.Seed},");
            sb.Append($"{r.TotalReward:F4},{r.VictimsRescued},{r.VictimsTotal},");
            sb.Append($"{r.AllRescued},{r.UAVSurvived},{r.MissionTime:F2},");
            sb.Append($"{r.StepsTaken},{r.Collisions},{r.FireEncounters},");
            sb.Append($"{r.PathLength:F2},{r.RewardPickup:F4},{r.RewardDeliver:F4},");
            sb.Append($"{r.RewardTime:F4},{r.RewardDistance:F4},{r.RewardFire:F4},");
            sb.Append($"{r.RewardCollision:F4},{r.RewardRisk:F4},{r.RewardExplore:F4}");

            try { File.AppendAllText(_csvPath, sb.ToString() + "\n"); }
            catch (Exception ex) { Debug.LogWarning($"[EpisodeLogger] Write error: {ex.Message}"); }
        }
    }

    // ── Data structures ───────────────────────────────────────────────────

    [Serializable]
    public class EpisodeRecord
    {
        public int    Episode;
        public string PolicyName;
        public int    Seed;
        public float  TotalReward;
        public int    VictimsRescued;
        public int    VictimsTotal;
        public bool   AllRescued;
        public bool   UAVSurvived;
        public float  MissionTime;
        public int    StepsTaken;
        public int    Collisions;
        public int    FireEncounters;
        public float  PathLength;

        // Per-component reward breakdown
        public float RewardPickup;
        public float RewardDeliver;
        public float RewardTime;
        public float RewardDistance;
        public float RewardFire;
        public float RewardCollision;
        public float RewardRisk;
        public float RewardExplore;

        [NonSerialized] public float StartTime;
    }

    public class EpisodeSummary
    {
        public int   EpisodeCount;
        public float MeanReward;
        public float MeanVictims;
        public float SuccessRate;
        public float UAVSurvivalRate;
        public float MeanTime;
        public float MeanPath;
        public float MeanCollisions;
    }
}
