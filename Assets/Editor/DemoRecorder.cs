using ActiveRagdoll;
using UnityEditor;
using UnityEditor.Recorder;
using UnityEditor.Recorder.Input;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Records a short demo video with Unity Recorder: opens a scenario scene, plays several seeds
/// back to back in real time (with the debug overlay), and writes an MP4 to Recordings/.
/// Menu: Ragdoll > Record Demo > ...  Keep the Unity window focused while it records: play mode
/// only advances in real time when the editor has focus.
/// The selector is whatever the scene's ScenarioRunner is set to (default rules); the laya and
/// gliclass selectors need the decision server running.
/// </summary>
[InitializeOnLoad]
public static class DemoRecorder
{
    const string Key = "DemoRecorder.Pending";
    const int Seeds = 4;               // trials per video
    const float SecondsPerTrial = 4.5f; // settle 1 s + about 3.5 s of reaction

    static RecorderController controller;

    static DemoRecorder() => EditorApplication.playModeStateChanged += OnPlayModeChanged;

    [MenuItem("Ragdoll/Record Demo/Scenario 1 (bench)")]
    static void RecordS1() => Begin(RagdollSceneBuilder.Scenario1Path, "S1");

    [MenuItem("Ragdoll/Record Demo/Scenario 2 (standing)")]
    static void RecordS2() => Begin(RagdollSceneBuilder.Scenario2Path, "S2");

    static void Begin(string scene, string tag)
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        EditorSceneManager.OpenScene(scene);
        SessionState.SetString(Key, tag);
        EditorApplication.EnterPlaymode();
    }

    static void OnPlayModeChanged(PlayModeStateChange change)
    {
        string tag = SessionState.GetString(Key, "");
        if (change != PlayModeStateChange.EnteredPlayMode || tag == "") return;
        SessionState.EraseString(Key);

        var runner = Object.FindAnyObjectByType<ScenarioRunner>();
        runner.trialDuration = SecondsPerTrial - runner.settleTime;
        runner.autoAdvance = true; // next seed when a trial ends

        var settings = ScriptableObject.CreateInstance<RecorderControllerSettings>();
        var movie = ScriptableObject.CreateInstance<MovieRecorderSettings>();
        movie.name = "Ragdoll demo";
        movie.Enabled = true;
        movie.OutputFormat = MovieRecorderSettings.VideoRecorderOutputFormat.MP4;
        movie.ImageInputSettings = new GameViewInputSettings { OutputWidth = 1280, OutputHeight = 720 };
        movie.AudioInputSettings.PreserveAudio = false;
        movie.OutputFile = $"Recordings/demo_{tag}_{runner.selectorName}_seed{runner.seed}";
        settings.AddRecorderSettings(movie);
        settings.SetRecordModeToTimeInterval(0f, Seeds * SecondsPerTrial);
        settings.FrameRate = 30f;

        controller = new RecorderController(settings);
        controller.PrepareRecording();
        controller.StartRecording();
        Debug.Log($"[DemoRecorder] recording {Seeds} trials to {movie.OutputFile}.mp4; stop play mode when it finishes.");
    }
}
