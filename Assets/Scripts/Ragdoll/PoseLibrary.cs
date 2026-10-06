using System.Collections.Generic;
using UnityEngine;

namespace ActiveRagdoll
{
    /// <summary>What the balance controller should try to keep the centre of mass over.</summary>
    public enum SupportMode { None, Feet, Seat }

    /// <summary>One entry of a pose sequence: blend to <see cref="Pose"/> over <see cref="BlendTime"/> seconds.</summary>
    public struct PoseStep
    {
        public Pose Pose;
        public float BlendTime;
        public PoseStep(Pose pose, float blendTime) { Pose = pose; BlendTime = blendTime; }
    }

    /// <summary>A target pose: joint angles (degrees, raw joint frame) plus how it should be balanced.</summary>
    public class Pose
    {
        public string Name;
        public SupportMode Support = SupportMode.Feet;
        /// <summary>Overall stiffness scale of this pose (limp uses a tiny value).</summary>
        public float Stiffness = 1f;
        public readonly Dictionary<string, Vector3> Angles = new Dictionary<string, Vector3>();

        public Vector3 Get(string joint) => Angles.TryGetValue(joint, out var a) ? a : Vector3.zero;

        public Pose Set(string joint, float x, float y = 0f, float z = 0f)
        {
            Angles[joint] = new Vector3(x, y, z);
            return this;
        }

        /// <summary>
        /// Set a left/right joint pair. Values are given for the right side; the left side
        /// gets the mirror image (pitch kept, yaw and roll negated).
        /// </summary>
        public Pose Sym(string joint, float x, float y = 0f, float z = 0f)
        {
            Angles[joint + "_R"] = new Vector3(x, y, z);
            Angles[joint + "_L"] = new Vector3(x, -y, -z);
            return this;
        }
    }

    /// <summary>
    /// The target poses, written as joint-angle sets. No animation assets.
    ///
    /// Sign guide (verified in the editor, see README):
    ///   Hip x &lt; 0 swings the thigh forward.      Knee x &gt; 0 bends the knee.
    ///   Ankle x &gt; 0 points the toes down.        Spine/Neck x &gt; 0 bends forward.
    ///   Shoulder x &lt; 0 raises the arm forward.   Shoulder z &gt; 0 (right side) raises the arm sideways.
    ///   Elbow x &lt; 0 bends the elbow.
    /// </summary>
    public static class PoseLibrary
    {
        // Feet a little wider than hip width (hip roll 5 deg, ankle rolled back to keep the feet flat).
        public static readonly Pose Stand = new Pose { Name = "stand" }
            .Sym("Hip", -4f, 0f, 5f).Sym("Knee", 8f).Sym("Ankle", -4f, 0f, -5f)
            .Set("SpineLower", 0f).Set("SpineUpper", 0f).Set("Neck", 0f)
            .Sym("Shoulder", 0f, 0f, 8f).Sym("Elbow", -10f);

        public static readonly Pose Sit = new Pose { Name = "sit", Support = SupportMode.Seat }
            .Sym("Hip", -90f).Sym("Knee", 90f).Sym("Ankle", 0f)
            .Set("SpineLower", 5f).Set("SpineUpper", 0f).Set("Neck", 5f)
            .Sym("Shoulder", -25f, 0f, 5f).Sym("Elbow", -55f);

        public static readonly Pose Brace = new Pose { Name = "brace" }
            .Sym("Hip", -25f, 0f, 5f).Sym("Knee", 40f).Sym("Ankle", -15f, 0f, -5f)
            .Set("SpineLower", 12f).Set("SpineUpper", 5f).Set("Neck", 5f)
            .Sym("Shoulder", -75f, 0f, 12f).Sym("Elbow", -25f);

        public static readonly Pose ProtectHead = new Pose { Name = "protect_head" }
            .Sym("Hip", -30f).Sym("Knee", 45f).Sym("Ankle", -15f)
            .Set("SpineLower", 15f).Set("SpineUpper", 10f).Set("Neck", 30f)
            .Sym("Shoulder", -150f, 0f, 35f).Sym("Elbow", -120f);

        public static readonly Pose Curl = new Pose { Name = "curl", Support = SupportMode.None }
            .Sym("Hip", -100f).Sym("Knee", 120f).Sym("Ankle", 20f)
            .Set("SpineLower", 35f).Set("SpineUpper", 25f).Set("Neck", 40f)
            .Sym("Shoulder", -70f, 0f, -10f).Sym("Elbow", -125f);

        /// <summary>All drives near zero; the target is the stand pose but it barely matters.</summary>
        public static readonly Pose Limp = new Pose { Name = "limp", Support = SupportMode.None, Stiffness = 0.02f }
            .Sym("Knee", 10f).Sym("Elbow", -15f);

        /// <summary>Scales the stagger step length (1 = 30 deg hip swing forward).</summary>
        public static float StaggerSize = 0.7f;

        public static readonly Pose StaggerStep = MakeStaggerStep(new Vector2(0f, 1f));

        /// <summary>
        /// The stagger_step pose: arms out for balance and a lunge in <paramref name="direction"/>.
        /// Through <see cref="ActiveRagdollController.StartStagger"/> the legs are driven by the
        /// step controller instead, and only the upper body of this pose is used.
        /// The stance knee is solved so that both feet reach the same height (both stay on the
        /// ground), and the ankles are set to keep each foot flat.
        /// </summary>
        public static Pose MakeStaggerStep(Vector2 direction)
        {
            Vector2 d = Dir(direction);
            bool rightLeg = StepWithRight(d);
            string step = rightLeg ? "_R" : "_L", stance = rightLeg ? "_L" : "_R";
            float side = rightLeg ? 1f : -1f;
            float f = d.y * StaggerSize, l = Mathf.Abs(d.x) * StaggerSize;

            // Stepping leg: forward (hip < 0) or back (hip > 0), and out to the side (roll).
            float stepHip = f >= 0f ? -30f * f : -25f * f;
            float stepKnee = f >= 0f ? 30f * f : 0f;
            float stepRoll = 25f * l * side;
            // Stance leg: the pelvis has moved toward the step, so this leg leans the other way.
            float stanceHip = f >= 0f ? 15f * f : 15f * f;
            float stanceRoll = 8f * l * -side;
            float stanceKnee = SolveKnee(LegHeight(stepHip, stepKnee, stepRoll), stanceHip, stanceRoll);

            return new Pose { Name = "stagger_step" }
                .Set("SpineLower", 8f * f).Set("SpineUpper", 0f).Set("Neck", 0f)
                .Sym("Shoulder", -10f, 0f, 40f).Sym("Elbow", -20f)
                .Set("Hip" + step, stepHip, 0f, stepRoll).Set("Knee" + step, stepKnee)
                .Set("Ankle" + step, -(stepHip + stepKnee), 0f, -stepRoll)
                .Set("Hip" + stance, stanceHip, 0f, stanceRoll).Set("Knee" + stance, stanceKnee)
                .Set("Ankle" + stance, -(stanceHip + stanceKnee), 0f, -stanceRoll);
        }

        static Vector2 Dir(Vector2 v) => v.sqrMagnitude > 1e-6f ? v.normalized : new Vector2(0f, 1f);
        static bool StepWithRight(Vector2 d) => Mathf.Abs(d.x) > 0.3f ? d.x > 0f : true;

        /// <summary>Vertical hip-to-ankle height (m) of a leg with thigh and shin of 0.43 m.</summary>
        static float LegHeight(float hipDeg, float kneeDeg, float rollDeg)
        {
            float h = 0.43f * Mathf.Cos(hipDeg * Mathf.Deg2Rad) + 0.43f * Mathf.Cos((hipDeg + kneeDeg) * Mathf.Deg2Rad);
            return h * Mathf.Cos(rollDeg * Mathf.Deg2Rad);
        }

        static float SolveKnee(float targetHeight, float hipDeg, float rollDeg)
        {
            float best = 0f, bestErr = float.MaxValue;
            for (float k = 0f; k <= 90f; k += 0.5f)
            {
                float err = Mathf.Abs(LegHeight(hipDeg, k, rollDeg) - targetHeight);
                if (err < bestErr) { bestErr = err; best = k; }
            }
            return best;
        }

        /// <summary>
        /// Seated version of a pose: legs and support from <see cref="Sit"/>, upper body from <paramref name="upper"/>.
        /// Behaviours use this when the character is sitting, so a seated brace does not try to stand up.
        /// </summary>
        public static Pose Seated(Pose upper)
        {
            var p = new Pose { Name = upper.Name, Support = SupportMode.Seat, Stiffness = upper.Stiffness };
            foreach (var kv in upper.Angles) p.Angles[kv.Key] = kv.Value;
            foreach (var leg in new[] { "Hip", "Knee", "Ankle" })
            {
                p.Angles[leg + "_R"] = Sit.Get(leg + "_R");
                p.Angles[leg + "_L"] = Sit.Get(leg + "_L");
            }
            return p;
        }

        public static readonly Pose[] All = { Stand, Sit, Brace, ProtectHead, Curl, StaggerStep, Limp };

        public static Pose ByName(string name)
        {
            foreach (var p in All) if (p.Name == name) return p;
            return null;
        }
    }
}
