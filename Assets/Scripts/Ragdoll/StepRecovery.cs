using UnityEngine;

namespace ActiveRagdoll
{
    /// <summary>
    /// Feedback stepping used by the stagger_recover behaviour, in the spirit of SIMBICON
    /// (Yin, Loken and van de Panne, SIGGRAPH 2007), with two simplifications:
    ///   * The swing foot is aimed at the capture point (COM + v * sqrt(h/g)), not at a
    ///     hand-tuned linear feedback law.
    ///   * Torso control is done with the stance-hip PD drive, whose target is recomputed each
    ///     step from world-frame angles so that the drive pulls the pelvis upright.
    /// Leg joint targets are written directly; the pose blender still drives the upper body.
    ///
    /// Angles: phiX / phiZ are a link's world rotation about the heading-frame right / forward
    /// axes, with the same signs as the joint drives, so for a hip
    ///     joint angle ~= phi(thigh) - phi(pelvis).
    /// </summary>
    public class StepRecovery
    {
        public float swingTime = 0.30f;
        public float doubleSupportTime = 0.10f;
        public float swingKneeLift = 55f;     // deg, knee bend for foot clearance
        public float landingKnee = 8f;        // deg
        public float stanceKnee = 10f;        // deg
        public float capturePointGain = 1.1f; // >1 steps a bit past the capture point
        public float maxStepLength = 0.55f;   // m, from the hip
        public float minFootSpacing = 0.12f;  // m, swing foot stays this far from the stance foot sideways
        public int maxSteps = 4;
        [UnityEngine.Tooltip("Stance-hip torso correction: deg of hip offset per deg of pelvis tilt, and its cap.")]
        public float torsoGain = 0.5f;
        public float maxTorsoCorrection = 10f;

        public bool Active { get; private set; }
        public int StepsTaken { get; private set; }
        public bool Captured { get; private set; }
        public string Phase { get; private set; } = "idle";

        readonly ActiveRagdollController c;
        bool swingRight;
        float phaseTimer;
        bool inSwing;
        // Leg angles captured when a phase starts. Holding a snapshot (not re-reading the current
        // angle every step) is what gives the drives something to push back toward.
        Vector3 stanceHipHold;
        readonly System.Collections.Generic.Dictionary<string, Vector3> legHold = new System.Collections.Generic.Dictionary<string, Vector3>();

        public StepRecovery(ActiveRagdollController controller) { c = controller; }

        /// <summary>Start stepping. <paramref name="pushDir"/> (heading frame, x right / y forward) picks the first swing leg.</summary>
        public void Begin(Vector2 pushDir)
        {
            Active = true; Captured = false; StepsTaken = 0;
            Vector3 f0, r0; c.HeadingFrame(out f0, out r0);
            if (IsCaptured(f0, r0)) { Active = false; Captured = true; Phase = "no step needed"; return; }
            // Step first with the leg on the side we are being pushed toward; for straight
            // forward/back pushes use the less loaded leg (the one whose foot is further from the COM).
            if (Mathf.Abs(pushDir.x) > 0.3f) swingRight = pushDir.x > 0f;
            else swingRight = DistToCom("Foot_R") > DistToCom("Foot_L");
            StartSwing();
        }

        public void Stop() { Active = false; Phase = "idle"; }

        void StartSwing()
        {
            inSwing = true; phaseTimer = 0f; Phase = "swing " + (swingRight ? "R" : "L");
            stanceHipHold = c.JointAngles("Hip" + (swingRight ? "_L" : "_R"));
        }

        void StartDoubleSupport()
        {
            inSwing = false; phaseTimer = 0f; StepsTaken++; Phase = "double support";
            legHold.Clear();
            foreach (var j in new[] { "Hip_R", "Knee_R", "Ankle_R", "Hip_L", "Knee_L", "Ankle_L" })
                legHold[j] = c.JointAngles(j);
        }

        /// <summary>Overwrite the leg targets (raw joint degrees) for this physics step.</summary>
        public void Update(float dt, System.Func<string, int> jointIndex, Vector3[] targets)
        {
            if (!Active) return;
            phaseTimer += dt;

            Vector3 fwd, right; c.HeadingFrame(out fwd, out right);
            string sw = swingRight ? "_R" : "_L", st = swingRight ? "_L" : "_R";
            var pelvis = c.Body.Pelvis.transform;
            Vector2 pelvisPhi = Phi(pelvis, fwd, right);

            if (inSwing)
            {
                // ---- Stance leg: hip angle held from the start of the swing plus a small, capped
                // correction that tips the pelvis toward upright (a full SIMBICON-style torso servo
                // kicked the lightly loaded stance foot away). Knee slightly bent, foot kept flat.
                Vector2 stanceShin = Phi(c.Body.Links["Shin" + st].transform, fwd, right);
                Vector2 torso = TorsoCorrection(pelvisPhi);
                targets[jointIndex("Hip" + st)] = new Vector3(stanceHipHold.x + torso.x, 0f, stanceHipHold.z + torso.y);
                targets[jointIndex("Knee" + st)] = new Vector3(stanceKnee, 0f, 0f);
                targets[jointIndex("Ankle" + st)] = new Vector3(-stanceShin.x, 0f, -stanceShin.y);

                // ---- Swing leg: aim the foot at the capture point.
                float s = Mathf.Clamp01(phaseTimer / swingTime);
                Vector3 com = c.CenterOfMass, v = c.ComVelocity;
                float h = Mathf.Max(0.5f, com.y - GroundY());
                Vector3 cp = com + Vector3.ProjectOnPlane(v, Vector3.up) * Mathf.Sqrt(h / 9.81f) * capturePointGain;
                Vector3 hip = c.Body.Links["Thigh" + sw].transform.position;
                Vector3 stanceFoot = c.Body.Links["Foot" + st].transform.position;

                float offF = Vector3.Dot(cp - hip, fwd);
                float offR = Vector3.Dot(cp - hip, right);
                // Keep the swing foot on its own side of the stance foot (no leg crossing).
                float side = swingRight ? 1f : -1f;
                float minLateral = Vector3.Dot(stanceFoot - hip, right) + side * minFootSpacing;
                offR = side > 0 ? Mathf.Max(offR, minLateral) : Mathf.Min(offR, minLateral);
                Vector2 off = Vector2.ClampMagnitude(new Vector2(offR, offF), maxStepLength);

                float hipH = Mathf.Max(0.5f, hip.y - GroundY());
                float wantX = -Mathf.Atan2(off.y, hipH) * Mathf.Rad2Deg;  // thigh forward -> negative phiX
                float wantZ = Mathf.Atan2(off.x, hipH) * Mathf.Rad2Deg;   // foot to the right -> positive phiZ
                // Lift the knee early in the swing, straighten for landing.
                float knee = s < 0.5f ? swingKneeLift : Mathf.Lerp(swingKneeLift, landingKnee, (s - 0.5f) / 0.5f);
                // With a bent knee the foot sits behind the thigh line; swing the thigh further to compensate.
                float hipX = wantX - knee * 0.5f;

                Vector2 swingShin = Phi(c.Body.Links["Shin" + sw].transform, fwd, right);
                targets[jointIndex("Hip" + sw)] = new Vector3(
                    Mathf.Clamp(hipX - pelvisPhi.x, -90f, 30f), 0f, Mathf.Clamp(wantZ - pelvisPhi.y, -30f, 30f));
                targets[jointIndex("Knee" + sw)] = new Vector3(knee, 0f, 0f);
                targets[jointIndex("Ankle" + sw)] = new Vector3(-swingShin.x, 0f, -swingShin.y);

                bool landed = s > 0.6f && c.Body.Links["Foot" + sw].transform.position.y < GroundY() + 0.10f;
                if (s >= 1f || landed) StartDoubleSupport();
            }
            else
            {
                // ---- Double support: both legs hold the geometry they landed in, and both hips
                // share the torso correction that tips the pelvis toward upright.
                foreach (var kv in legHold) targets[jointIndex(kv.Key)] = kv.Value;
                Vector2 torso = TorsoCorrection(pelvisPhi);
                foreach (var hipName in new[] { "Hip_R", "Hip_L" })
                    targets[jointIndex(hipName)] += new Vector3(torso.x, 0f, torso.y);

                if (phaseTimer >= doubleSupportTime)
                {
                    if (IsCaptured(fwd, right)) { Captured = true; Active = false; Phase = "captured"; }
                    else if (StepsTaken >= maxSteps) { Active = false; Phase = "gave up"; }
                    else { swingRight = !swingRight; StartSwing(); }
                }
            }
        }

        /// <summary>
        /// Hip offset (deg) that rotates the pelvis back toward upright. Since hip angle ~ phi(thigh) - phi(pelvis),
        /// adding +phi(pelvis) to a hip target with the foot planted rotates the pelvis by -phi(pelvis).
        /// </summary>
        Vector2 TorsoCorrection(Vector2 pelvisPhi) => new Vector2(
            Mathf.Clamp(torsoGain * pelvisPhi.x, -maxTorsoCorrection, maxTorsoCorrection),
            Mathf.Clamp(torsoGain * pelvisPhi.y, -maxTorsoCorrection, maxTorsoCorrection));

        /// <summary>Captured when the capture point lies inside the feet's support region and the body is slow.</summary>
        bool IsCaptured(Vector3 fwd, Vector3 right)
        {
            Vector3 com = c.CenterOfMass, v = c.ComVelocity;
            float h = Mathf.Max(0.5f, com.y - GroundY());
            Vector3 cp = com + Vector3.ProjectOnPlane(v, Vector3.up) * Mathf.Sqrt(h / 9.81f);
            Vector3 a = c.Body.Links["Foot_R"].GetComponent<BoxCollider>().bounds.center;
            Vector3 b = c.Body.Links["Foot_L"].GetComponent<BoxCollider>().bounds.center;
            float dist = DistanceToSegmentXZ(cp, a, b);
            return dist < 0.10f && Vector3.ProjectOnPlane(v, Vector3.up).magnitude < 0.4f;
        }

        static float DistanceToSegmentXZ(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector2 P = new Vector2(p.x, p.z), A = new Vector2(a.x, a.z), B = new Vector2(b.x, b.z);
            Vector2 ab = B - A;
            float t = Mathf.Clamp01(Vector2.Dot(P - A, ab) / Mathf.Max(ab.sqrMagnitude, 1e-6f));
            return Vector2.Distance(P, A + t * ab);
        }

        float DistToCom(string foot)
        {
            Vector3 d = c.Body.Links[foot].transform.position - c.CenterOfMass;
            d.y = 0f;
            return d.magnitude;
        }

        float GroundY() => c.transform.position.y;

        /// <summary>World rotation of a link about the heading-frame right (x) and forward (z) axes, in degrees.</summary>
        public static Vector2 Phi(Transform t, Vector3 fwd, Vector3 right)
        {
            Vector3 up = t.up;
            float phiX = Mathf.Atan2(Vector3.Dot(up, fwd), Vector3.Dot(up, Vector3.up)) * Mathf.Rad2Deg;
            float phiZ = Mathf.Atan2(-Vector3.Dot(up, right), Vector3.Dot(up, Vector3.up)) * Mathf.Rad2Deg;
            return new Vector2(phiX, phiZ);
        }
    }
}
