using ActiveRagdoll;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Editor menu that builds the test scenes from code, so the scenes can be rebuilt
/// at any time and nothing depends on hand-placed objects.
/// </summary>
public static class RagdollSceneBuilder
{
    [MenuItem("Ragdoll/Apply Physics Project Settings")]
    public static void ApplyPhysicsProjectSettings()
    {
        // Time
        // Time. Unity 6 stores the fixed timestep as a rational number: count / (numerator / denominator).
        var time = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TimeManager.asset")[0]);
        var step = time.FindProperty("Fixed Timestep");
        long rate = step.FindPropertyRelative("m_Rate.m_Numerator").longValue;
        step.FindPropertyRelative("m_Count").longValue = (long)System.Math.Round(SimulationSettings.FixedTimestep * rate);
        time.FindProperty("Maximum Allowed Timestep").floatValue = 0.1f;
        time.ApplyModifiedProperties();

        // Physics: TGS solver, more iterations, enhanced determinism (same seed -> same trial).
        var dyn = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/DynamicsManager.asset")[0]);
        dyn.FindProperty("m_DefaultSolverIterations").intValue = SimulationSettings.SolverIterations;
        dyn.FindProperty("m_DefaultSolverVelocityIterations").intValue = SimulationSettings.SolverVelocityIterations;
        dyn.FindProperty("m_SolverType").intValue = 1; // 0 = PGS, 1 = TGS
        dyn.FindProperty("m_EnableEnhancedDeterminism").boolValue = true;
        dyn.ApplyModifiedProperties();

        AssetDatabase.SaveAssets();
        Debug.Log("[RagdollSceneBuilder] Physics project settings applied.");
    }

    public const string Scenario1Path = "Assets/Scenes/S1_BenchFallingObject.unity";
    public const string Scenario2Path = "Assets/Scenes/S2_StandingImpact.unity";

    [MenuItem("Ragdoll/Build Scenario Scenes")]
    public static void BuildScenarioScenes()
    {
        BuildScenarioScene(Scenario.BenchFallingObject, Scenario1Path);
        BuildScenarioScene(Scenario.StandingImpact, Scenario2Path);
    }

    static void BuildScenarioScene(Scenario scenario, string path)
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        new GameObject("SimulationSettings").AddComponent<SimulationSettings>();
        new GameObject("SimDriver").AddComponent<SimDriver>();
        MakeGround();

        Transform seat = null;
        bool seated = scenario == Scenario.BenchFallingObject;
        if (seated)
        {
            var bench = MakeBox("Bench", new Vector3(0f, 0.225f, 0f), new Vector3(1.2f, 0.45f, 0.40f), new Color(0.45f, 0.35f, 0.25f));
            seat = new GameObject("SeatAnchor").transform;
            seat.SetParent(bench.transform, false);
            seat.position = new Vector3(0f, 0.45f, 0f);
        }

        var character = MakeCharacter("Character", Vector3.zero, seated ? "sit" : "stand", seat, new Color(0.85f, 0.55f, 0.35f));
        character.gameObject.AddComponent<EventSensor>();
        character.gameObject.AddComponent<ReactionManager>();

        var runner = new GameObject("ScenarioRunner").AddComponent<ScenarioRunner>();
        runner.scenario = scenario;
        runner.character = character;

        var overlay = new GameObject("DecisionOverlay").AddComponent<RagdollDebugOverlay>();
        overlay.character = character;

        var cam = Camera.main;
        cam.transform.position = new Vector3(2.2f, 1.6f, 3.2f);
        cam.transform.LookAt(new Vector3(0f, 0.9f, 0f));

        EditorSceneManager.SaveScene(scene, path);
        AddToBuild(path);
        Debug.Log("[RagdollSceneBuilder] Built " + path);
    }

    static ActiveRagdollController MakeCharacter(string name, Vector3 pos, string pose, Transform seat, Color color)
    {
        var go = new GameObject(name);
        go.transform.position = pos;
        var c = go.AddComponent<ActiveRagdollController>();
        c.startPose = pose;
        c.seatAnchor = seat;
        c.color = color;
        return c;
    }

    static void MakeGround()
    {
        var g = MakeBox("Ground", new Vector3(0f, -0.1f, 0f), new Vector3(30f, 0.2f, 30f), new Color(0.6f, 0.6f, 0.6f));
        g.GetComponent<Collider>().sharedMaterial = new PhysicsMaterial("Ground") { dynamicFriction = 0.8f, staticFriction = 0.9f };
    }

    static GameObject MakeBox(string name, Vector3 pos, Vector3 size, Color color)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.position = pos;
        go.transform.localScale = size;
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        var mat = new Material(shader) { color = color };
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        return go;
    }

    static void AddToBuild(string path)
    {
        var list = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        if (!list.Exists(s => s.path == path)) list.Add(new EditorBuildSettingsScene(path, true));
        EditorBuildSettings.scenes = list.ToArray();
    }
}
