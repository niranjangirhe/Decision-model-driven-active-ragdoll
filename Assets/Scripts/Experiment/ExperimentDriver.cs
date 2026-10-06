using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ActiveRagdoll
{
    /// <summary>
    /// Runs every selector x seed of the experiment in the open scenario scene, from inside play mode,
    /// and appends one CSV row per trial. Added to the scene by the editor's ExperimentBatch.
    /// A runtime component (a coroutine one frame after Start) is used because editor callbacks such as
    /// EditorApplication.delayCall did not fire during play mode in -batchmode.
    /// </summary>
    public class ExperimentDriver : MonoBehaviour
    {
        public string[] selectors;
        public int firstSeed = 1, lastSeed = 25;
        public string output = "Results/trials.csv";
        public bool verbose;

        /// <summary>Called when every trial in this scene is done (the editor moves to the next scene).</summary>
        public static Action Finished;

        IEnumerator Start()
        {
            yield return null; // let every other Start run first
            try { RunAll(); }
            catch (Exception e) { Debug.LogException(e); }
            Finished?.Invoke();
        }

        void RunAll()
        {
            var runner = FindAnyObjectByType<ScenarioRunner>();
            runner.autoStartInPlayMode = false;
            if (Camera.main != null) Camera.main.enabled = false; // nothing needs to be drawn
            EventLog.EchoToConsole = false;
            Debug.Log($"[Experiment] scenario {(int)runner.scenario}: {selectors.Length} selectors x seeds {firstSeed}-{lastSeed}");

            foreach (var selector in selectors)
            {
                runner.selectorName = selector;
                var rows = new List<TrialRecord>();
                var t0 = DateTime.Now;
                for (int seed = firstSeed; seed <= lastSeed; seed++)
                {
                    var r = runner.RunTrialRecord(seed);
                    rows.Add(r);
                    if (verbose) Debug.Log("[Experiment] " + r.Summary());
                }
                TrialRecord.Append(output, rows);
                Debug.Log($"[Experiment] S{(int)runner.scenario} {selector}: {rows.Count} trials in {(DateTime.Now - t0).TotalSeconds:F0} s, " +
                          $"fell {rows.Count(r => r.FellEver)}, head hit {rows.Count(r => r.HeadHit)}, fallbacks {rows.Count(r => r.Fallback)}");
            }
        }
    }
}
