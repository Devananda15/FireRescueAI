// FireRescueAI – EvaluationManager.cs
// ============================================================
// Runs reproducible evaluation episodes for both Standard PPO
// and Recurrent PPO, then writes a comparison CSV and a
// human-readable summary to the console.
//
// HOW TO USE:
//   1. After training, load a trained model in Behavior Parameters
//      (set Behavior Type = Inference, drag .onnx file)
//   2. Add EvaluationManager to any GameObject in the scene
//   3. Set policyName to "StandardPPO" or "RecurrentPPO"
//   4. Press Play → evaluation runs automatically
//   5. Results written to results/eval_<name>_<timestamp>.csv
//
// For full comparison: run evaluation for both models, then
// run results/plot_comparison.py to generate plots.
// ============================================================

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using FireRescueAI.Fire;
using FireRescueAI.Victims;
using FireRescueAI.Core;

namespace FireRescueAI.Training
{
    public class EvaluationManager : MonoBehaviour
    {
        // ── Inspector ─────────────────────────────────────────────────────
        [Header("Evaluation Settings")]
        [Tooltip("Name of the policy being evaluated.")]
        public string policyName       = "StandardPPO";

        [Tooltip("Number of evaluation episodes to run.")]
        public int    evalEpisodes     = 20;

        [Tooltip("Maximum steps per evaluation episode.")]
        public int    maxStepsPerEp    = 3000;

        [Tooltip("Base random seed (incremented per episode).")]
        public int    baseSeed         = 42;

        [Tooltip("If true, evaluation starts automatically on Play.")]
        public bool   autoStart        = false;

        [Header("Output")]
        public string outputFolder     = "results";

        // ── Private ───────────────────────────────────────────────────────
        private VictimManager     _victimManager;
        private FireSystem        _fireSystem;
        private SimulationManager _simManager;
        private EpisodeLogger     _logger;
        private AI.DroneAgent     _agent;

        private bool              _isRunning;
        private int               _currentEpisode;
        private string            _evalCsvPath;

        private readonly List<EpisodeRecord> _evalResults = new();

        // ─────────────────────────────────────────────────────────────────
        void Start()
        {
            _victimManager = FindFirstObjectByType<VictimManager>();
            _fireSystem    = FindFirstObjectByType<FireSystem>();
            _simManager    = FindFirstObjectByType<SimulationManager>();
            _logger        = FindFirstObjectByType<EpisodeLogger>();
            _agent         = FindFirstObjectByType<AI.DroneAgent>();

            if (!Directory.Exists(outputFolder))
                Directory.CreateDirectory(outputFolder);

            string ts  = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            _evalCsvPath = Path.Combine(outputFolder,
                               $"eval_{policyName}_{ts}.csv");

            if (autoStart) StartCoroutine(RunEvaluation());
        }

        // ─────────────────────────────────────────────────────────────────
        // Public entry point (can also be called from a UI button)
        // ─────────────────────────────────────────────────────────────────
        public void StartEvaluation() => StartCoroutine(RunEvaluation());

        private IEnumerator RunEvaluation()
        {
            if (_isRunning) yield break;
            _isRunning = true;
            _evalResults.Clear();

            Debug.Log($"[EvalManager] Starting {evalEpisodes}-episode evaluation of {policyName}");

            for (int ep = 0; ep < evalEpisodes; ep++)
            {
                _currentEpisode = ep + 1;
                int seed = baseSeed + ep;
                UnityEngine.Random.InitState(seed);

                // Reset the world
                if (_fireSystem    != null) _fireSystem.ResetAll();
                if (_victimManager != null && FindFirstObjectByType<Building.BuildingLayout>() is var bld && bld != null)
                    _victimManager.ResetAll(bld.Rooms, bld.ExitPositions);
                if (_simManager    != null) _simManager.ResetEpisode();

                if (_logger != null) _logger.BeginEpisode(seed);

                // Wait for episode to finish (MaxStep or EndEpisode)
                int stepCount = 0;
                bool episodeDone = false;
                float startTime  = Time.time;

                while (!episodeDone && stepCount < maxStepsPerEp)
                {
                    stepCount++;
                    // Yield one frame
                    yield return null;

                    // Check termination signals
                    if (_victimManager != null && _victimManager.RemainingCount == 0)
                        episodeDone = true;
                    if (_simManager != null && !_simManager.MissionActive)
                        episodeDone = true;
                }

                // Record results
                int totalVictims = (_victimManager != null && _victimManager.TotalVictims > 0)
                    ? _victimManager.TotalVictims : 5;
                int rescued  = (_victimManager != null) ? (totalVictims - _victimManager.RemainingCount) : 0;
                bool allSafe = (_victimManager != null) && _victimManager.RemainingCount == 0;
                bool uavOk   = (_fireSystem    != null) &&
                               _fireSystem.GetFireIntensityAt(
                                   (_agent != null) ? _agent.transform.position : Vector3.zero) < 0.3f;

                if (_logger != null) _logger.EndEpisode(rescued, totalVictims, uavOk);

                // Local record for summary
                var record = new EpisodeRecord
                {
                    Episode        = _currentEpisode,
                    PolicyName     = policyName,
                    Seed           = seed,
                    VictimsRescued = rescued,
                    VictimsTotal   = totalVictims,
                    AllRescued     = allSafe,
                    UAVSurvived    = uavOk,
                    MissionTime    = Time.time - startTime,
                    StepsTaken     = stepCount
                };
                _evalResults.Add(record);

                Debug.Log($"[EvalManager] Ep {_currentEpisode}/{evalEpisodes} " +
                          $"rescued={rescued}/{totalVictims} allOk={allSafe} uav={uavOk} " +
                          $"steps={stepCount}");

                // Short pause between episodes
                yield return new WaitForSeconds(0.1f);
            }

            WriteEvalCsv();
            PrintSummary();
            _isRunning = false;
        }

        // ─────────────────────────────────────────────────────────────────
        // Output
        // ─────────────────────────────────────────────────────────────────
        private void WriteEvalCsv()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Episode,PolicyName,Seed,VictimsRescued,VictimsTotal," +
                          "AllRescued,UAVSurvived,MissionTimeSecs,StepsTaken");
            foreach (var r in _evalResults)
            {
                sb.AppendLine($"{r.Episode},{r.PolicyName},{r.Seed}," +
                              $"{r.VictimsRescued},{r.VictimsTotal}," +
                              $"{r.AllRescued},{r.UAVSurvived}," +
                              $"{r.MissionTime:F2},{r.StepsTaken}");
            }
            File.WriteAllText(_evalCsvPath, sb.ToString());
            Debug.Log($"[EvalManager] Evaluation results → {_evalCsvPath}");
        }

        private void PrintSummary()
        {
            int n = _evalResults.Count;
            if (n == 0) return;

            float sumRescued  = 0, sumAll = 0, sumUAV = 0, sumTime = 0;
            foreach (var r in _evalResults)
            {
                sumRescued += r.VictimsRescued;
                sumAll     += r.AllRescued  ? 1f : 0f;
                sumUAV     += r.UAVSurvived ? 1f : 0f;
                sumTime    += r.MissionTime;
            }

            Debug.Log(
                $"\n╔══════════════════════════════════════╗\n" +
                $"║  EVALUATION SUMMARY: {policyName,-14} ║\n" +
                $"╠══════════════════════════════════════╣\n" +
                $"║  Episodes:          {n,-18}║\n" +
                $"║  Avg victims/ep:    {(sumRescued/n):F2,-16}  ║\n" +
                $"║  Success rate:      {(sumAll/n*100f):F1}%{"",-14}║\n" +
                $"║  UAV survival:      {(sumUAV/n*100f):F1}%{"",-14}║\n" +
                $"║  Avg mission time:  {(sumTime/n):F1}s{"",-14}║\n" +
                $"╚══════════════════════════════════════╝");
        }

        // GUI for evaluation progress
        private void OnGUI()
        {
            if (!_isRunning) return;
            GUI.color = new Color(0, 0, 0, 0.7f);
            GUI.Box(new Rect(10, Screen.height - 70, 300, 55), "");
            GUI.color = Color.white;
            var style = new GUIStyle(GUI.skin.label) { fontSize = 13 };
            style.normal.textColor = Color.cyan;
            GUI.Label(new Rect(18, Screen.height - 65, 285, 20),
                $"EVALUATION: {policyName}", style);
            style.normal.textColor = Color.white;
            GUI.Label(new Rect(18, Screen.height - 44, 285, 20),
                $"Episode {_currentEpisode} / {evalEpisodes}", style);
        }
    }
}
