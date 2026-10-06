using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ActiveRagdoll
{
    public enum Scenario { BenchFallingObject = 1, StandingImpact = 2 }

    /// <summary>
    /// Runs one randomised trial at a time. All randomness comes from System.Random(seed), so a
    /// given seed gives the same event for every selector.
    ///   Scenario 1: sitting on a bench, an object is dropped from above. Random mass, drop height,
    ///               horizontal offset.
    ///   Scenario 2: standing, a ball is thrown at the body. Random direction, body part aimed at,
    ///               and impulse (ball mass x speed). Sometimes a wall, sometimes a railing nearby.
    /// Play mode: N = next seed, R = rerun seed. Scripted: <see cref="RunTrial"/>.
    /// </summary>
    public class ScenarioRunner : MonoBehaviour, ISimStep
    {
        public Scenario scenario = Scenario.BenchFallingObject;
        public int seed = 1;
        public string selectorName = "rules";
        public ActiveRagdollController character;
        public bool autoStartInPlayMode = true;
        public float settleTime = 1.0f;
        public float trialDuration = 5.0f;
        [Tooltip("Play mode only: start the next seed automatically when a trial ends (used for demo videos).")]
        public bool autoAdvance = false;

        [Header("Scenario 1 ranges")]
        public Vector2 massRangeKg = new Vector2(1f, 15f);
        public Vector2 dropHeightAboveHead = new Vector2(0.5f, 2.5f);
        public float maxHorizontalOffset = 0.35f;

        [Header("Scenario 2 ranges")]
        public Vector2 impulseRangeNs = new Vector2(10f, 80f);
        public Vector2 ballMassRangeKg = new Vector2(1f, 4f);
        public float launchDistance = 3f;
        [Range(0, 1)] public float wallChance = 0.4f;
        [Range(0, 1)] public float railingChance = 0.3f;

        public int StepOrder => -10;

        /// <summary>What the trial set up, for logging.</summary>
        public string TrialSetup { get; private set; }
        public double TrialStartTime { get; private set; }

        readonly List<GameObject> spawned = new List<GameObject>();
        System.Random rng;
        bool launchPending;
        double launchAt;

        void OnEnable() => SimDriver.Register(this);
        void OnDisable() => SimDriver.Unregister(this);

        void Start()
        {
            if (autoStartInPlayMode) StartTrial(seed);
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.nKey.wasPressedThisFrame) StartTrial(++seed);
            if (kb.rKey.wasPressedThisFrame) StartTrial(seed);
        }

        public void StartTrial(int trialSeed)
        {
            seed = trialSeed;
            rng = new System.Random(seed);
            ClearSpawned();

            var reaction = character.GetComponent<ReactionManager>();
            reaction.Selector = ReactionManager.MakeSelector(selectorName);
            EventLog.Context = $"S{(int)scenario} seed={seed} selector={selectorName}";

            if (scenario == Scenario.StandingImpact) PlaceSurroundings();
            character.Respawn(PoseLibrary.ByName(character.startPose));

            TrialStartTime = SimDriver.Time;
            launchAt = SimDriver.Time + settleTime;
            launchPending = true;
        }

        public void SimStep(float dt)
        {
            if (autoAdvance && Physics.simulationMode == SimulationMode.FixedUpdate
                && SimDriver.Time >= TrialStartTime + settleTime + trialDuration)
            {
                StartTrial(seed + 1);
                return;
            }
            if (!launchPending || SimDriver.Time < launchAt) return;
            launchPending = false;
            if (scenario == Scenario.BenchFallingObject) DropObject(); else ThrowBall();
            EventLog.Write("setup", TrialSetup);
        }

        // ------------------------------------------------------------------ scenario 1

        void DropObject()
        {
            float mass = Range(massRangeKg);
            float height = Range(dropHeightAboveHead);
            float r = (float)rng.NextDouble() * maxHorizontalOffset;
            float a = (float)rng.NextDouble() * Mathf.PI * 2f;

            // A box whose size grows with mass (density 800 kg/m^3, like dense wood).
            float side = Mathf.Pow(mass / 800f, 1f / 3f);
            Vector3 head = character.Body.Links["Head"].worldCenterOfMass;
            Vector3 pos = head + new Vector3(r * Mathf.Cos(a), height + side, r * Mathf.Sin(a));
            var go = MakeBody("FallingObject", PrimitiveType.Cube, pos, Vector3.one * side, mass, new Color(0.8f, 0.2f, 0.2f));
            go.transform.rotation = Quaternion.Euler(Range(new Vector2(0, 360)), Range(new Vector2(0, 360)), 0f);

            TrialSetup = $"mass={mass:F2}kg drop={height:F2}m offset={r:F2}m angle={a * Mathf.Rad2Deg:F0}deg side={side:F2}m";
        }

        // ------------------------------------------------------------------ scenario 2

        static readonly string[] Targets = { "Head", "Chest", "Chest", "UpperArm_L", "UpperArm_R", "Abdomen", "Pelvis", "Thigh_L", "Thigh_R" };

        void PlaceSurroundings()
        {
            // Draw every random number in a fixed order so the seed fully determines the trial.
            bool wall = rng.NextDouble() < wallChance;
            int wallSide = rng.Next(3);                // 0 left, 1 right, 2 behind
            float wallDist = Range(new Vector2(0.8f, 1.5f));
            bool railing = rng.NextDouble() < railingChance;
            int railSide = rng.Next(2);                 // 0 left, 1 right
            string setup = "";

            Transform t = character.transform;
            if (wall)
            {
                Vector3 dir = wallSide == 0 ? -t.right : wallSide == 1 ? t.right : -t.forward;
                var w = MakeStatic("Wall", t.position + dir * (wallDist + 0.1f) + Vector3.up * 1.0f,
                    Quaternion.LookRotation(dir), new Vector3(3f, 2f, 0.2f), new Color(0.55f, 0.55f, 0.6f));
                setup += $"wall={(wallSide == 0 ? "left" : wallSide == 1 ? "right" : "behind")}@{wallDist:F2}m ";
            }
            if (railing)
            {
                Vector3 dir = railSide == 0 ? -t.right : t.right;
                var rail = MakeStatic("Railing", t.position + dir * 0.6f + Vector3.up * 1.0f,
                    t.rotation, new Vector3(0.05f, 0.05f, 2f), new Color(0.9f, 0.8f, 0.2f));
                rail.AddComponent<GraspableSupport>().label = "railing";
                setup += $"railing={(railSide == 0 ? "left" : "right")} ";
            }
            TrialSetup = setup;
        }

        void ThrowBall()
        {
            string target = Targets[rng.Next(Targets.Length)];
            float azimuth = Range(new Vector2(0f, 360f));
            float impulse = Range(impulseRangeNs);
            float mass = Range(ballMassRangeKg);
            float speed = Mathf.Min(impulse / mass, 25f);

            Vector3 aim = character.Body.Links[target].worldCenterOfMass;
            Transform t = character.transform;
            Vector3 from = Quaternion.AngleAxis(azimuth, Vector3.up) * t.forward;
            Vector3 start = aim + from * launchDistance;
            start.y = Mathf.Max(start.y, 0.3f);
            // Ballistic launch that reaches the aim point after launchDistance / speed seconds.
            float T = launchDistance / speed;
            Vector3 v = (aim - start) / T - 0.5f * Physics.gravity * T;

            var ball = MakeBody("Ball", PrimitiveType.Sphere, start, Vector3.one * 0.22f, mass, new Color(0.2f, 0.7f, 0.3f));
            ball.GetComponent<Rigidbody>().linearVelocity = v;

            TrialSetup += $"target={target} azimuth={azimuth:F0}deg mass={mass:F2}kg speed={speed:F1}m/s nominalImpulse={mass * speed:F1}Ns";
        }

        // ------------------------------------------------------------------ helpers

        float Range(Vector2 r) => Mathf.Lerp(r.x, r.y, (float)rng.NextDouble());

        GameObject MakeBody(string name, PrimitiveType type, Vector3 pos, Vector3 scale, float mass, Color color)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.position = pos;
            go.transform.localScale = scale;
            SetColor(go, color);
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = mass;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            spawned.Add(go);
            return go;
        }

        GameObject MakeStatic(string name, Vector3 pos, Quaternion rot, Vector3 scale, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetPositionAndRotation(pos, rot);
            go.transform.localScale = scale;
            SetColor(go, color);
            spawned.Add(go);
            return go;
        }

        static void SetColor(GameObject go, Color c)
        {
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = c };
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }

        void ClearSpawned()
        {
            foreach (var go in spawned) if (go != null) DestroyImmediate(go);
            spawned.Clear();
            TrialSetup = "";
        }

        // ------------------------------------------------------------------ scripted trial

        [Header("Outcome measurement")]
        [Tooltip("COM speed (m/s) below which the body counts as still.")]
        public float stableSpeed = 0.10f;
        [Tooltip("How long (s) the COM must stay below stableSpeed to count as a stable posture.")]
        public float stableHold = 0.5f;

        /// <summary>
        /// Run one full trial by stepping physics from script and return its record.
        /// Respects <see cref="ReactionManager.HoldSimTimeAt"/>, so a slow selector cannot make
        /// simulated time run ahead of the wall-clock time it has used.
        /// Outcome measures:
        ///   fell            pelvis ever below the fall height (0.6 m standing, 0.45 m seated) or tilted over 45 deg after launch;
        ///   time_to_stable  time from the first event (or launch if no event) until the COM speed first stays
        ///                   below <see cref="stableSpeed"/> for <see cref="stableHold"/> s; a body lying still also counts;
        ///   recovered       upright (not fallen) at the end of the trial.
        /// </summary>
        public TrialRecord RunTrialRecord(int trialSeed)
        {
            StartTrial(trialSeed);
            var reaction = character.GetComponent<ReactionManager>();
            var sensor = character.GetComponent<EventSensor>();
            double end = SimDriver.Time + settleTime + trialDuration;
            bool fell = false;
            float minPelvis = float.MaxValue;
            double stillSince = -1, stableAt = -1;

            while (SimDriver.Time < end)
            {
                double? hold = reaction.HoldSimTimeAt;
                if (hold.HasValue && SimDriver.Time >= hold.Value) { System.Threading.Thread.Sleep(1); continue; }
                SimDriver.StepOnce();
                if (SimDriver.Time < launchAt) continue; // still settling

                minPelvis = Mathf.Min(minPelvis, character.PelvisHeight);
                if (character.HasFallen()) fell = true;

                // Stability is measured from the first event; before it nothing has been disturbed yet.
                double from = reaction.FirstEvent != null ? reaction.FirstEvent.SimTime : launchAt;
                if (SimDriver.Time < from) continue;
                if (stableAt >= 0) continue; // first stable posture only; the later blend back to the start pose does not count
                if (character.ComVelocity.magnitude < stableSpeed)
                {
                    if (stillSince < 0) stillSince = SimDriver.Time;
                    if (SimDriver.Time - stillSince >= stableHold) stableAt = stillSince - from;
                }
                else stillSince = -1;
            }

            var d = reaction.FirstDecision;
            var ev = reaction.FirstEvent;
            var r = new TrialRecord
            {
                Scenario = (int)scenario, Seed = seed, Selector = selectorName, Setup = TrialSetup,
                EventKind = ev != null ? ev.Kind.ToString() : "none",
                EventText = reaction.FirstEventText ?? "",
                EventsHandled = reaction.EventsHandled,
                HeadHit = sensor.HeadHit, HeadPeakImpulse = sensor.HeadPeakImpulse,
                FellEver = fell, MinPelvisHeight = minPelvis,
                TimeToStable = stableAt >= 0 ? (float)stableAt : float.NaN,
                UprightAtEnd = !character.HasFallen(),
                BackInStartPose = reaction.Phase == "recovered",
                Note = reaction.FirstNote ?? "",
            };
            if (d != null)
            {
                r.Behaviour = d.Behaviour; r.Stiffness = d.Stiffness; r.Recover = d.Recover;
                foreach (var kv in d.Probabilities) r.Probabilities[kv.Key] = kv.Value;
                r.LatencyMs = d.LatencyMs; r.ServerLatencyMs = d.ServerLatencyMs > 0 ? d.ServerLatencyMs : float.NaN;
                r.Device = d.Device ?? ""; r.TimedOut = d.TimedOut; r.Fallback = d.Fallback; r.Error = d.Error ?? "";
            }
            return r;
        }

        /// <summary>One trial, as a one-line summary (used when trying things from the CLI).</summary>
        public string RunTrial(int trialSeed) => RunTrialRecord(trialSeed).Summary();
    }
}
