using System.Text;
using UnityEngine;

namespace ActiveRagdoll
{
    /// <summary>
    /// Milestone 1 check, run from play mode (or from the CLI with eval).
    /// Physics is stepped by script, so it runs faster than real time and does not need the
    /// editor window to have focus. Every test starts from a fresh snap into the start pose.
    /// </summary>
    public static class PoseStabilityTest
    {
        /// <summary>Advance every ragdoll in the scene by <paramref name="seconds"/> of simulated time.</summary>
        public static void Simulate(float seconds) => SimDriver.Advance(seconds);

        public class Sample
        {
            public float PelvisY, HeadY, Tilt, ComSpeed, TrackingError;
            public override string ToString() =>
                $"pelvisY {PelvisY:F2}  headY {HeadY:F2}  tilt {Tilt,5:F1}  comSpeed {ComSpeed:F3}  trackErr {TrackingError,4:F1}deg";
        }

        public static Sample Measure(ActiveRagdollController c)
        {
            return new Sample
            {
                PelvisY = c.PelvisHeight, HeadY = c.HeadHeight, Tilt = c.PelvisTilt,
                ComSpeed = c.ComVelocity.magnitude, TrackingError = c.MeanTrackingError(),
            };
        }

        /// <summary>From a fresh start pose, blend into each pose and hold it.</summary>
        public static string RunPoses(string characterName, string poses, float settle = 1f, float hold = 3f)
        {
            var c = Find(characterName);
            var start = PoseLibrary.ByName(c.startPose);
            var sb = new StringBuilder();
            foreach (var name in poses.Split(','))
            {
                c.Respawn(start);
                c.SetAllStiffness(1f);
                Simulate(settle);
                Play(c, name.Trim());
                Simulate(hold);
                sb.AppendLine($"{c.name,-9} {start.Name}->{name.Trim(),-13} {Measure(c)}");
            }
            c.Respawn(start);
            return sb.ToString();
        }

        /// <summary>"brace", "seated:brace", or "stagger:x:y" (step direction in the heading frame).</summary>
        static void Play(ActiveRagdollController c, string name)
        {
            var parts = name.Split(':');
            if (parts[0] == "seated") c.SetPose(PoseLibrary.Seated(PoseLibrary.ByName(parts[1])));
            else if (parts[0] == "stagger")
                c.StartStagger(new Vector2(
                    float.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture),
                    float.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture)));
            else c.SetPose(name);
        }

        /// <summary>Push the chest with a given impulse in four directions and report whether the character stayed up.</summary>
        public static string RunPushes(string characterName, string impulses, float hold = 3f)
        {
            var c = Find(characterName);
            var start = PoseLibrary.ByName(c.startPose);
            Vector3[] dirs = { Vector3.forward, Vector3.back, Vector3.right, Vector3.left };
            string[] dirNames = { "front->back", "back->front", "left->right", "right->left" };
            var sb = new StringBuilder();
            foreach (var s in impulses.Split(','))
            {
                float j = float.Parse(s, System.Globalization.CultureInfo.InvariantCulture);
                sb.Append($"{c.name} impulse {j,5:F0} N*s: ");
                for (int d = 0; d < 4; d++)
                {
                    c.Respawn(start);
                    Simulate(1f);
                    // Pushed object moves along the impulse, so "front->back" is an impulse toward -forward.
                    c.Body.Links["Chest"].AddForce(-c.transform.TransformDirection(dirs[d]) * j, ForceMode.Impulse);
                    Simulate(hold);
                    bool fell = Fell(c, start);
                    sb.Append($"{dirNames[d]} {(fell ? "FELL" : "ok")} ({c.PelvisTilt:F0}deg)  ");
                }
                sb.AppendLine();
            }
            c.Respawn(start);
            return sb.ToString();
        }

        /// <summary>
        /// Push the chest, wait <paramref name="reactionDelay"/>, then start a reaction
        /// ("none", a pose name, or "stagger" toward the push). Reports survival per direction.
        /// </summary>
        public static string RunPushReactions(string characterName, string impulses, string reactions, float reactionDelay = 0.1f, float hold = 3f)
        {
            var c = Find(characterName);
            var start = PoseLibrary.ByName(c.startPose);
            Vector2[] dirs = { new Vector2(0, -1), new Vector2(0, 1), new Vector2(1, 0), new Vector2(-1, 0) };
            var sb = new StringBuilder();
            foreach (var reaction in reactions.Split(','))
            foreach (var s in impulses.Split(','))
            {
                float j = float.Parse(s, System.Globalization.CultureInfo.InvariantCulture);
                sb.Append($"{reaction,-13} {j,4:F0} N*s: ");
                int ok = 0;
                foreach (var d in dirs)
                {
                    c.Respawn(start);
                    Simulate(1f);
                    // d is the push direction in the heading frame (x right, y forward).
                    Vector3 world = c.transform.TransformDirection(new Vector3(d.x, 0f, d.y));
                    c.Body.Links["Chest"].AddForce(world * j, ForceMode.Impulse);
                    Simulate(reactionDelay);
                    if (reaction == "stagger") c.StartStagger(d);
                    else if (reaction != "none") c.SetPose(reaction);
                    Simulate(hold);
                    bool fell = Fell(c, start);
                    if (!fell) ok++;
                    sb.Append(fell ? "X" : "o");
                    if (reaction == "stagger") sb.Append($"[{c.Stepper.StepsTaken}{(c.Stepper.Captured ? "c" : "")}]");
                    sb.Append(" ");
                }
                sb.AppendLine($" ({ok}/4 upright)   [dirs: back, fwd, right, left]");
            }
            c.Respawn(start);
            return sb.ToString();
        }

        /// <summary>Fallen: pelvis well below its height in the start pose, or pelvis tilted more than 45 deg.</summary>
        public static bool Fell(ActiveRagdollController c, Pose start) =>
            c.PelvisHeight < (start.Support == SupportMode.Seat ? 0.45f : 0.6f) || c.PelvisTilt > 45f;

        static ActiveRagdollController Find(string name)
        {
            foreach (var c in Object.FindObjectsByType<ActiveRagdollController>())
                if (c.name == name) return c;
            throw new System.ArgumentException("No character named " + name);
        }
    }
}
