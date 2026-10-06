using System;
using ActiveRagdoll;
using UnityEditor;
using UnityEditor.Recorder;
using UnityEditor.Recorder.Input;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Records demo videos with Unity Recorder: opens a scenario scene, plays consecutive seeds in real
/// time with the decision overlay, and writes an MP4 to Recordings/.
/// Menu: Ragdoll > Record Demo > ...  or  DemoRecorder.Record(...) from the Unity CLI.
/// Keep the Unity window focused while it records: play mode only advances in real time when the
/// editor has focus. The laya and gliclass selectors need the decision server running.
/// </summary>
[InitializeOnLoad]
public static class DemoRecorder
{
    [Serializable]
    class Job { public int scenario; public string selector; public int firstSeed; public int trials; public float secondsPerTrial; public string output; }

    const string Key = "DemoRecorder.Job";
    static RecorderController controller;

    static DemoRecorder() => EditorApplication.playModeStateChanged += OnPlayModeChanged;

    [MenuItem("Ragdoll/Record Demo/Scenario 1 (bench)")]
    static void RecordS1() => Record(1, "rules", 1, 4, "demo_S1_rules");

    [MenuItem("Ragdoll/Record Demo/Scenario 2 (standing)")]
    static void RecordS2() => Record(2, "rules", 2, 4, "demo_S2_rules");

    /// <summary>Record <paramref name="trials"/> consecutive seeds of a scenario to Recordings/&lt;output&gt;.mp4.</summary>
    public static void Record(int scenario, string selector, int firstSeed, int trials, string output, float secondsPerTrial = 4.5f)
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        EditorSceneManager.OpenScene(scenario == 1 ? RagdollSceneBuilder.Scenario1Path : RagdollSceneBuilder.Scenario2Path);
        var job = new Job { scenario = scenario, selector = selector, firstSeed = firstSeed, trials = trials, secondsPerTrial = secondsPerTrial, output = output };
        SessionState.SetString(Key, JsonUtility.ToJson(job));
        EditorApplication.EnterPlaymode();
    }

    static void OnPlayModeChanged(PlayModeStateChange change)
    {
        string json = SessionState.GetString(Key, "");
        if (change != PlayModeStateChange.EnteredPlayMode || json == "") return;
        SessionState.EraseString(Key);
        var job = JsonUtility.FromJson<Job>(json);

        var runner = UnityEngine.Object.FindAnyObjectByType<ScenarioRunner>();
        runner.selectorName = job.selector;
        runner.trialDuration = job.secondsPerTrial - runner.settleTime;
        runner.autoAdvance = true;
        runner.StartTrial(job.firstSeed);

        var settings = ScriptableObject.CreateInstance<RecorderControllerSettings>();
        var movie = ScriptableObject.CreateInstance<MovieRecorderSettings>();
        movie.name = "Ragdoll demo";
        movie.Enabled = true;
        movie.OutputFormat = MovieRecorderSettings.VideoRecorderOutputFormat.MP4;
        movie.ImageInputSettings = new GameViewInputSettings { OutputWidth = 1280, OutputHeight = 720 };
        movie.AudioInputSettings.PreserveAudio = false;
        movie.OutputFile = "Recordings/" + job.output;
        settings.AddRecorderSettings(movie);
        settings.SetRecordModeToTimeInterval(0f, job.trials * job.secondsPerTrial);
        settings.FrameRate = 30f;

        controller = new RecorderController(settings);
        controller.PrepareRecording();
        controller.StartRecording();
        Debug.Log($"[DemoRecorder] recording S{job.scenario} {job.selector} seeds {job.firstSeed}-{job.firstSeed + job.trials - 1} to {movie.OutputFile}.mp4");
    }
}
