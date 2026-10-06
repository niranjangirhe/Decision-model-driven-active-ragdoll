using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ActiveRagdoll;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Runs the experiment: every scenario scene, every selector, every seed, one CSV row per trial.
/// Physics is stepped from script inside play mode (faster than real time) with the camera off.
///
/// Working ways to run it, against the open editor:
///   * menu Ragdoll > Run Experiment (default plan: S1+S2, fixed/rules/laya/gliclass, seeds 1-25);
///   * Unity CLI:  unity cmd eval --code "ExperimentBatch.RunWith(\"1,2\", \"fixed,rules,laya,gliclass\", 1, 30, \"Results/trials.csv\"); return 0;"
///     (play mode only advances while the editor ticks; if the window has no focus, keep it ticking,
///     see README).
///
/// NOT WORKING: Unity.exe -batchmode ... -executeMethod ExperimentBatch.RunFromCommandLine.
/// The flow runs and writes a CSV, but in -batchmode PhysX has not registered a newly built
/// ArticulationBody when the body is placed in its start pose, so every trial starts broken and
/// the rows are invalid (checked against the editor run: all trials "fell"). Kept for reference.
///
/// Each scene needs play mode (the ragdoll is built by its components), so the plan is kept in
/// SessionState. In play mode an ExperimentDriver component runs the trials and calls back when done.
/// </summary>
[InitializeOnLoad]
public static class ExperimentBatch
{
    [Serializable]
    class Plan
    {
        public int[] scenarios = { 1, 2 };
        public string[] selectors = { "fixed", "rules", "laya", "gliclass" };
        public int firstSeed = 1, lastSeed = 25;
        public string output = "Results/trials.csv";
        public int sceneIndex;
        public bool batch;
    }

    const string Key = "ExperimentBatch.Plan";

    static ExperimentBatch()
    {
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    [MenuItem("Ragdoll/Run Experiment")]
    public static void RunFromMenu() => Start(new Plan());

    public static void RunFromCommandLine()
    {
        var p = new Plan { batch = true };
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            switch (args[i])
            {
                case "-scenarios": p.scenarios = args[i + 1].Split(',').Select(int.Parse).ToArray(); break;
                case "-selectors": p.selectors = args[i + 1].Split(','); break;
                case "-seeds":
                    var r = args[i + 1].Split('-');
                    p.firstSeed = int.Parse(r[0]); p.lastSeed = int.Parse(r[r.Length - 1]);
                    break;
                case "-out": p.output = args[i + 1]; break;
            }
        }
        Start(p);
    }

    /// <summary>Scripted entry used from the CLI against an open editor (same as the menu, with options).</summary>
    public static void RunWith(string scenarios, string selectors, int firstSeed, int lastSeed, string output)
    {
        Start(new Plan
        {
            scenarios = scenarios.Split(',').Select(int.Parse).ToArray(),
            selectors = selectors.Split(','), firstSeed = firstSeed, lastSeed = lastSeed, output = output,
        });
    }

    static void Start(Plan p)
    {
        p.selectors = CheckServer(p.selectors);
        if (File.Exists(p.output)) File.Delete(p.output);
        Debug.Log($"[Experiment] scenarios {string.Join(",", p.scenarios)}, selectors {string.Join(",", p.selectors)}, " +
                  $"seeds {p.firstSeed}-{p.lastSeed}, output {p.output}");
        p.sceneIndex = 0;
        Save(p);
        OpenAndPlay(p);
    }

    /// <summary>Drop the model selectors if the decision server is not reachable, and say so.</summary>
    static string[] CheckServer(string[] selectors)
    {
        if (!selectors.Any(s => s == "laya" || s == "gliclass")) return selectors;
        try
        {
            using (var c = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(3) })
            {
                string health = c.GetStringAsync(HttpModelSelector.ServerUrl + "/health").Result;
                Debug.Log("[Experiment] decision server: " + health);
                return selectors;
            }
        }
        catch (Exception e)
        {
            Debug.LogError("[Experiment] decision server not reachable, skipping laya and gliclass: " + (e.InnerException ?? e).Message);
            return selectors.Where(s => s != "laya" && s != "gliclass").ToArray();
        }
    }

    static string ScenePath(int scenario) =>
        scenario == 1 ? RagdollSceneBuilder.Scenario1Path : RagdollSceneBuilder.Scenario2Path;

    static void OpenAndPlay(Plan p)
    {
        EditorSceneManager.OpenScene(ScenePath(p.scenarios[p.sceneIndex]));
        EditorApplication.EnterPlaymode();
    }

    static void OnPlayModeChanged(PlayModeStateChange change)
    {
        var p = Load();
        if (p == null) return;

        if (change == PlayModeStateChange.EnteredPlayMode)
        {
            var driver = new GameObject("ExperimentDriver").AddComponent<ExperimentDriver>();
            driver.selectors = p.selectors;
            driver.firstSeed = p.firstSeed; driver.lastSeed = p.lastSeed;
            driver.output = p.output; driver.verbose = p.batch;
            ExperimentDriver.Finished = () =>
            {
                var plan = Load();
                plan.sceneIndex++;
                Save(plan);
                EditorApplication.ExitPlaymode();
            };
        }
        else if (change == PlayModeStateChange.EnteredEditMode)
        {
            if (p.sceneIndex < p.scenarios.Length)
            {
                OpenAndPlay(p);
                return;
            }
            SessionState.EraseString(Key);
            Debug.Log("[Experiment] done: " + Path.GetFullPath(p.output));
            if (p.batch) EditorApplication.Exit(0);
        }
    }

    static void Save(Plan p) => SessionState.SetString(Key, JsonUtility.ToJson(p));

    static Plan Load()
    {
        string json = SessionState.GetString(Key, "");
        return string.IsNullOrEmpty(json) ? null : JsonUtility.FromJson<Plan>(json);
    }
}
