using System.Collections.Generic;
using UnityEngine;

namespace ActiveRagdoll
{
    /// <summary>
    /// The active ragdoll. Every link is simulated at all times; the only thing this
    /// component does is choose joint drive targets and gains (PD control) each physics step:
    ///   1. blend the current target pose toward the requested pose,
    ///   2. add balance corrections (ankle strategy when standing, trunk lean when sitting),
    ///   3. write targets and per-group stiffness into the ArticulationBody drives.
    /// Optional: an upright-assist torque on the pelvis (a crutch, off by default).
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class ActiveRagdollController : MonoBehaviour, ISimStep
    {
        [Header("Spawn")]
        public string startPose = "stand";
        [Tooltip("For a sitting start: the top-centre of the seat. The pelvis is placed on it.")]
        public Transform seatAnchor;
        public Color color = new Color(0.85f, 0.55f, 0.35f);

        [Header("Pose blending")]
        [Tooltip("Default time (s) to blend between poses.")]
        public float defaultBlendTime = 0.35f;

        [Header("Stiffness multipliers per body group (1 = nominal)")]
        public float legs = 1f;
        public float torso = 1f;
        public float arms = 1f;
        public float neck = 1f;
        [Tooltip("How fast (multiplier units per second) group stiffness moves toward a new value.")]
        public float stiffnessRampRate = 8f;

        [Header("Balance: standing (ankle strategy)")]
        public bool balanceEnabled = true;
        [Tooltip("Ankle offset (deg) per metre of centre-of-mass error.")]
        public float ankleKp = 120f;
        [Tooltip("Ankle offset (deg) per m/s of centre-of-mass velocity.")]
        public float ankleKd = 25f;
        [Tooltip("Share of the ankle correction also applied at the hips, in the opposite sense (hip strategy).")]
        public float hipShare = 0.4f;
        public float maxAnkleOffset = 20f;
        [Tooltip("Where the COM should sit, forward of the foot-centre midpoint (m).")]
        public float comForwardBias = 0.0f;

        [Header("Balance: sitting (trunk lean)")]
        public float trunkKp = 150f;
        public float trunkKd = 20f;
        public float maxTrunkOffset = 20f;
        [Tooltip("Where the upper-body COM should sit, forward of the pelvis (m).")]
        public float seatComBias = 0.03f;

        [Header("Upright assist (CRUTCH, off by default)")]
        [Tooltip("Adds an external torque to the pelvis that pushes it upright. Not physically honest: documented as a crutch.")]
        public bool uprightAssist = false;
        public float assistStiffness = 300f;
        public float assistDamping = 30f;

        // ---- Public state for sensors, overlay and logging ----
        public RagdollBuilder.Result Body { get; private set; }
        public Pose CurrentPose { get; private set; }
        public float BlendProgress => blendDuration <= 0f ? 1f : Mathf.Clamp01(blendTimer / blendDuration);
        public Vector3 CenterOfMass { get; private set; }
        public Vector3 ComVelocity { get; private set; }
        /// <summary>COM error in the heading frame: x = right, y = forward (metres).</summary>
        public Vector2 BalanceError { get; private set; }
        public Vector3 AnkleOffset { get; private set; }
        public float TrunkOffset { get; private set; }
        public float PelvisHeight => Body.Pelvis.transform.position.y;
        public float HeadHeight => Body.Links["Head"].worldCenterOfMass.y;
        /// <summary>
        /// Fallen: pelvis well below its height in the start pose (0.6 m standing, 0.45 m seated)
        /// or tilted more than 45 deg. Used by the tests and the experiment log.
        /// </summary>
        public bool HasFallen()
        {
            bool seated = startPose == PoseLibrary.Sit.Name;
            return PelvisHeight < (seated ? 0.45f : 0.6f) || PelvisTilt > 45f;
        }

        /// <summary>Angle (deg) between the pelvis up axis and world up.</summary>
        public float PelvisTilt => Vector3.Angle(Body.Pelvis.transform.up, Vector3.up);

        public float GroupStiffness(BodyGroup g) => currentStiffness[(int)g];

        /// <summary>Feedback stepping used by stagger_recover (see <see cref="StepRecovery"/>).</summary>
        public StepRecovery Stepper { get; private set; }

        /// <summary>Mean absolute gap (deg) between commanded and actual joint angles, over all driven axes.</summary>
        public float MeanTrackingError()
        {
            float sum = 0f; int count = 0;
            for (int i = 0; i < Body.Joints.Count; i++)
            {
                Vector3 e = Body.Joints[i].CurrentAnglesDeg() - commandedTargets[i];
                sum += Mathf.Abs(e.x); count++;
                if (!Body.Joints[i].IsRevolute) { sum += Mathf.Abs(e.y) + Mathf.Abs(e.z); count += 2; }
            }
            return count > 0 ? sum / count : 0f;
        }

        readonly float[] currentStiffness = { 1f, 1f, 1f, 1f };
        Vector3[] fromTargets, toTargets, currentTargets, commandedTargets;
        float blendTimer, blendDuration;
        float reflexScale = 1f, reflexDuration, reflexTimer;
        readonly Queue<PoseStep> pendingSteps = new Queue<PoseStep>();
        readonly Dictionary<string, int> jointIndex = new Dictionary<string, int>();
        Vector3[] frameTargets;
        float totalMass;

        void Awake() => BuildBody();

        void Start() => SnapToPose(PoseLibrary.ByName(startPose) ?? PoseLibrary.Stand);

        void BuildBody()
        {
            var bodyMat = new PhysicsMaterial("Body") { dynamicFriction = 0.6f, staticFriction = 0.7f };
            var footMat = new PhysicsMaterial("Foot") { dynamicFriction = 0.9f, staticFriction = 1.0f, frictionCombine = PhysicsMaterialCombine.Maximum };
            Body = RagdollBuilder.Build(transform, bodyMat, footMat, color);

            int n = Body.Joints.Count;
            fromTargets = new Vector3[n];
            toTargets = new Vector3[n];
            currentTargets = new Vector3[n];
            commandedTargets = new Vector3[n];
            frameTargets = new Vector3[n];
            jointIndex.Clear();
            for (int i = 0; i < n; i++) jointIndex[Body.Joints[i].Name] = i;
            if (Stepper == null) Stepper = new StepRecovery(this); else Stepper.Stop();
            totalMass = 0f;
            foreach (var ab in Body.Links.Values) totalMass += ab.mass;

            for (int i = 0; i < 4; i++) currentStiffness[i] = TargetStiffness((BodyGroup)i);
            BodyBuilt?.Invoke();
        }

        /// <summary>Raised after the body is (re)built, so sensors can attach to the new links.</summary>
        public event System.Action BodyBuilt;

        // ------------------------------------------------------------------ API

        /// <summary>Blend toward a new target pose over <paramref name="blendTime"/> seconds (never snaps).</summary>
        public void SetPose(Pose pose, float blendTime = -1f)
        {
            pendingSteps.Clear();
            Stepper?.Stop();
            BlendTo(pose, blendTime);
        }

        /// <summary>
        /// Stagger and recover: arms go out (stagger_step upper body) and the step controller takes
        /// over the legs until the body is captured, then the legs hold where they landed.
        /// </summary>
        public void StartStagger(Vector2 pushDirHeading)
        {
            // Upper body from the stagger_step pose; legs start from where they are (the stepper drives them).
            var pose = CurrentAnglesAsPose("stagger_step");
            var stagger = PoseLibrary.MakeStaggerStep(pushDirHeading);
            foreach (var j in UpperBodyJoints) pose.Angles[j] = stagger.Get(j);
            SetPose(pose, 0.15f);
            Stepper.Begin(pushDirHeading);
        }

        /// <summary>Heading frame from the pelvis: forward and right, both horizontal.</summary>
        public void HeadingFrame(out Vector3 fwd, out Vector3 right)
        {
            fwd = Vector3.ProjectOnPlane(Body.Pelvis.transform.forward, Vector3.up).normalized;
            right = Vector3.Cross(Vector3.up, fwd);
        }

        /// <summary>Current angles (deg) of one joint.</summary>
        public Vector3 JointAngles(string joint) => Body.Joints[jointIndex[joint]].CurrentAnglesDeg();

        /// <summary>A pose made of the joints' current angles (used to hold the legs where a step landed).</summary>
        public Pose CurrentAnglesAsPose(string name)
        {
            var p = new Pose { Name = name };
            foreach (var j in Body.Joints) p.Angles[j.Name] = j.CurrentAnglesDeg();
            return p;
        }

        /// <summary>Play poses one after another; each starts when the previous blend has finished.</summary>
        public void PlaySequence(IEnumerable<PoseStep> steps)
        {
            pendingSteps.Clear();
            foreach (var s in steps) pendingSteps.Enqueue(s);
            var first = pendingSteps.Dequeue();
            BlendTo(first.Pose, first.BlendTime);
        }

        void BlendTo(Pose pose, float blendTime)
        {
            if (pose == null) return;
            for (int i = 0; i < Body.Joints.Count; i++)
            {
                fromTargets[i] = currentTargets[i];
                toTargets[i] = Body.Joints[i].Clamp(pose.Get(Body.Joints[i].Name));
            }
            CurrentPose = pose;
            blendDuration = blendTime >= 0f ? blendTime : defaultBlendTime;
            blendTimer = 0f;
        }

        public void SetPose(string poseName, float blendTime = -1f) => SetPose(PoseLibrary.ByName(poseName), blendTime);

        /// <summary>Set the stiffness multiplier of one group. It ramps there at <see cref="stiffnessRampRate"/>.</summary>
        public void SetGroupStiffness(BodyGroup g, float value)
        {
            value = Mathf.Max(0f, value);
            switch (g)
            {
                case BodyGroup.Legs: legs = value; break;
                case BodyGroup.Torso: torso = value; break;
                case BodyGroup.Arms: arms = value; break;
                case BodyGroup.Neck: neck = value; break;
            }
        }

        public void SetAllStiffness(float value)
        {
            for (int i = 0; i < 4; i++) SetGroupStiffness((BodyGroup)i, value);
        }

        /// <summary>
        /// Start a fresh trial: destroy the body, build a new one and place it in <paramref name="pose"/>.
        /// Rebuilding (rather than snapping the old body) matters: PhysX applies new joint positions
        /// only at the next step, so contacts from the old configuration would kick the body.
        /// </summary>
        public void Respawn(Pose pose)
        {
            if (Body != null) DestroyImmediate(Body.Pelvis.gameObject);
            pendingSteps.Clear();
            BuildBody();
            SnapToPose(pose);
        }

        /// <summary>
        /// Place a freshly built body into a pose with zero velocity. Only valid right after the
        /// body is built (see <see cref="Respawn"/>); during a trial poses are always blended.
        /// </summary>
        void SnapToPose(Pose pose)
        {
            for (int i = 0; i < Body.Joints.Count; i++)
            {
                Vector3 a = Body.Joints[i].Clamp(pose.Get(Body.Joints[i].Name));
                Body.Joints[i].SnapTo(a);
                fromTargets[i] = toTargets[i] = currentTargets[i] = a;
            }

            Vector3 pelvisPos;
            if (pose.Support == SupportMode.Seat && seatAnchor != null)
                // Thighs rest on the seat; pelvis sits toward the back of it.
                pelvisPos = seatAnchor.position + Vector3.up * 0.135f - transform.forward * 0.12f;
            else
                pelvisPos = transform.TransformPoint(new Vector3(0f, 1.00f + StandingDrop(pose), 0f));

            Body.Pelvis.TeleportRoot(pelvisPos, transform.rotation);
            Body.Pelvis.linearVelocity = Vector3.zero;
            Body.Pelvis.angularVelocity = Vector3.zero;
            CurrentPose = pose;
            blendDuration = 0f;
        }

        /// <summary>How much lower the pelvis is when the legs are bent in this pose (keeps feet on the ground at spawn).</summary>
        static float StandingDrop(Pose pose)
        {
            float hip = pose.Get("Hip_R").x * Mathf.Deg2Rad;
            float knee = pose.Get("Knee_R").x * Mathf.Deg2Rad;
            // Thigh 0.43 m and shin 0.43 m; knee angle is relative to the thigh.
            float legHeight = 0.43f * Mathf.Cos(hip) + 0.43f * Mathf.Cos(hip + knee);
            return legHeight - 0.86f;
        }

        // ------------------------------------------------------------------ Physics step

        public int StepOrder => 20;
        void OnEnable() => SimDriver.Register(this);
        void OnDisable() => SimDriver.Unregister(this);

        /// <summary>
        /// Reflex: multiply every group's stiffness by <paramref name="scale"/> for <paramref name="duration"/>
        /// seconds, fading out linearly. Used the moment an event is sensed, before any decision exists.
        /// </summary>
        public void ApplyReflex(float scale, float duration)
        {
            reflexScale = scale; reflexDuration = duration; reflexTimer = 0f;
        }

        public float ReflexScale => reflexTimer < reflexDuration
            ? Mathf.Lerp(reflexScale, 1f, reflexTimer / reflexDuration) : 1f;

        /// <summary>One control step, called by <see cref="SimDriver"/> before each physics step.</summary>
        public void SimStep(float dt)
        {
            reflexTimer += dt;

            // 1. Pose blending (smoothstep), then the next pose of a sequence if any.
            if (BlendProgress >= 1f && pendingSteps.Count > 0)
            {
                var next = pendingSteps.Dequeue();
                BlendTo(next.Pose, next.BlendTime);
            }
            blendTimer += dt;
            float w = Mathf.SmoothStep(0f, 1f, BlendProgress);
            for (int i = 0; i < currentTargets.Length; i++)
                currentTargets[i] = Vector3.Lerp(fromTargets[i], toTargets[i], w);

            // Group stiffness ramps toward the requested values.
            for (int g = 0; g < 4; g++)
                currentStiffness[g] = Mathf.MoveTowards(currentStiffness[g], TargetStiffness((BodyGroup)g), stiffnessRampRate * dt);

            // 2. Balance, or feedback stepping while a stagger is in progress.
            UpdateCenterOfMass();
            System.Array.Copy(currentTargets, frameTargets, frameTargets.Length);
            if (Stepper.Active)
            {
                AnkleOffset = Vector3.zero; TrunkOffset = 0f;
                Stepper.Update(dt, n => jointIndex[n], frameTargets);
                if (!Stepper.Active) HoldLegsWhereTheyAre();
            }
            else ComputeBalance();

            // 3. Drives.
            float poseScale = (CurrentPose != null ? CurrentPose.Stiffness : 1f) * ReflexScale;
            for (int i = 0; i < Body.Joints.Count; i++)
            {
                var joint = Body.Joints[i];
                Vector3 target = frameTargets[i];
                if (joint.Name.StartsWith("Ankle")) target += AnkleOffset;
                if (joint.Name.StartsWith("Hip")) target.x -= AnkleOffset.x * hipShare;
                if (joint.Name == "SpineLower") target.x += TrunkOffset;
                commandedTargets[i] = joint.Clamp(target);
                joint.ApplyDrive(target, currentStiffness[(int)joint.Group] * poseScale);
            }

            // 4. Optional crutch.
            if (uprightAssist && CurrentPose != null && CurrentPose.Support != SupportMode.None)
            {
                var pelvis = Body.Pelvis;
                Vector3 axis = Vector3.Cross(pelvis.transform.up, Vector3.up); // |axis| = sin(tilt)
                Vector3 w3 = pelvis.angularVelocity; w3.y = 0f;
                pelvis.AddTorque(axis * assistStiffness - w3 * assistDamping);
            }
        }

        /// <summary>After a stagger: keep the legs as they landed, bring the arms back, balance on both feet.</summary>
        void HoldLegsWhereTheyAre()
        {
            var hold = CurrentAnglesAsPose("stagger_step");
            foreach (var j in UpperBodyJoints) hold.Angles[j] = PoseLibrary.Stand.Get(j);
            BlendTo(hold, 0.4f);
        }

        float TargetStiffness(BodyGroup g)
        {
            switch (g)
            {
                case BodyGroup.Legs: return legs;
                case BodyGroup.Torso: return torso;
                case BodyGroup.Arms: return arms;
                default: return neck;
            }
        }

        void UpdateCenterOfMass()
        {
            Vector3 com = Vector3.zero, vel = Vector3.zero;
            foreach (var ab in Body.Links.Values)
            {
                com += ab.mass * ab.worldCenterOfMass;
                vel += ab.mass * ab.linearVelocity;
            }
            CenterOfMass = com / totalMass;
            ComVelocity = vel / totalMass;
        }

        void ComputeBalance()
        {
            AnkleOffset = Vector3.zero;
            TrunkOffset = 0f;
            if (!balanceEnabled || CurrentPose == null) return;

            // Heading frame from the pelvis.
            Vector3 fwd = Vector3.ProjectOnPlane(Body.Pelvis.transform.forward, Vector3.up).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, fwd);

            if (CurrentPose.Support == SupportMode.Feet)
            {
                Vector3 support = 0.5f * (FootCenter("Foot_R") + FootCenter("Foot_L")) + fwd * comForwardBias;
                Vector3 e = CenterOfMass - support;
                BalanceError = new Vector2(Vector3.Dot(e, right), Vector3.Dot(e, fwd));
                Vector2 v = new Vector2(Vector3.Dot(ComVelocity, right), Vector3.Dot(ComVelocity, fwd));

                // COM ahead of the feet -> point the toes down (+x) to tip the body back.
                float pitch = Mathf.Clamp(ankleKp * BalanceError.y + ankleKd * v.y, -maxAnkleOffset, maxAnkleOffset);
                // COM right of the feet -> roll the ankles so the shins lean left (-z).
                float roll = Mathf.Clamp(-(ankleKp * BalanceError.x + ankleKd * v.x), -maxAnkleOffset, maxAnkleOffset);
                AnkleOffset = new Vector3(pitch, 0f, roll);
            }
            else if (CurrentPose.Support == SupportMode.Seat)
            {
                // Keep the upper-body COM over the pelvis by leaning the lower spine.
                Vector3 c = Vector3.zero, cv = Vector3.zero; float m = 0f;
                foreach (var name in UpperBody)
                {
                    var ab = Body.Links[name];
                    c += ab.mass * ab.worldCenterOfMass; cv += ab.mass * ab.linearVelocity; m += ab.mass;
                }
                c /= m; cv /= m;
                float ef = Vector3.Dot(c - Body.Pelvis.worldCenterOfMass, fwd) - seatComBias;
                float vf = Vector3.Dot(cv, fwd);
                BalanceError = new Vector2(0f, ef);
                // Upper body too far forward -> lean back (-x).
                TrunkOffset = Mathf.Clamp(-(trunkKp * ef + trunkKd * vf), -maxTrunkOffset, maxTrunkOffset);
            }
        }

        static readonly string[] UpperBodyJoints = { "SpineLower", "SpineUpper", "Neck", "Shoulder_R", "Shoulder_L", "Elbow_R", "Elbow_L" };
        static readonly string[] UpperBody = { "Abdomen", "Chest", "Head", "UpperArm_R", "UpperArm_L", "Forearm_R", "Forearm_L" };

        Vector3 FootCenter(string foot) => Body.Links[foot].GetComponent<BoxCollider>().bounds.center;
    }
}
