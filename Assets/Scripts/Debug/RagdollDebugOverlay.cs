using UnityEngine;
using UnityEngine.InputSystem;

namespace ActiveRagdoll
{
    /// <summary>
    /// On-screen debug panel plus keyboard controls for trying poses by hand.
    ///   1-7  : blend the selected character to stand / sit / brace / protect_head / curl / stagger_step / limp
    ///   Tab  : select the next character
    ///   U    : toggle the upright-assist crutch
    ///   B    : toggle balance
    ///   P    : push the chest (fixed 120 N*s impulse, direction cycles front/back/left/right)
    /// Later milestones add event text, probabilities and latency to this panel.
    /// </summary>
    public class RagdollDebugOverlay : MonoBehaviour
    {
        public ActiveRagdollController[] characters;
        public int selected;
        public float pushImpulse = 120f;

        int pushCount;
        GUIStyle style;

        void Update()
        {
            if (characters == null || characters.Length == 0) return;
            var kb = Keyboard.current;
            if (kb == null) return;
            var c = characters[selected];

            Key[] keys = { Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5, Key.Digit6, Key.Digit7 };
            for (int i = 0; i < keys.Length; i++)
                if (kb[keys[i]].wasPressedThisFrame) PlayByIndex(c, i);

            if (kb.tabKey.wasPressedThisFrame) selected = (selected + 1) % characters.Length;
            if (kb.uKey.wasPressedThisFrame) c.uprightAssist = !c.uprightAssist;
            if (kb.bKey.wasPressedThisFrame) c.balanceEnabled = !c.balanceEnabled;
            if (kb.pKey.wasPressedThisFrame) Push(c);
        }

        /// <summary>Key i: poses in <see cref="PoseLibrary.All"/> order. Seated characters get seated brace/protect.</summary>
        static void PlayByIndex(ActiveRagdollController c, int i)
        {
            Pose p = PoseLibrary.All[i];
            bool seated = c.CurrentPose != null && c.CurrentPose.Support == SupportMode.Seat;
            if (p == PoseLibrary.StaggerStep) c.StartStagger(new Vector2(0f, 1f));
            else if (seated && (p == PoseLibrary.Brace || p == PoseLibrary.ProtectHead)) c.SetPose(PoseLibrary.Seated(p));
            else c.SetPose(p);
        }

        public void Push(ActiveRagdollController c)
        {
            Vector3[] dirs = { Vector3.back, Vector3.forward, Vector3.right, Vector3.left };
            Vector3 d = c.transform.TransformDirection(dirs[pushCount++ % dirs.Length]);
            c.Body.Links["Chest"].AddForce(d * pushImpulse, ForceMode.Impulse);
        }

        void OnGUI()
        {
            if (characters == null || characters.Length == 0) return;
            if (style == null) style = new GUIStyle(GUI.skin.label) { fontSize = 14, richText = true, wordWrap = true };
            var c = characters[selected];
            if (c == null || c.Body == null) return;

            var rm = c.GetComponent<ReactionManager>();
            string text =
                $"<b>{c.name}</b>  (Tab to switch)\n" +
                (rm != null ? ReactionText(rm) : "") +
                $"pose: <b>{c.CurrentPose?.Name}</b>  blend {c.BlendProgress:P0}\n" +
                $"stiffness  legs {c.GroupStiffness(BodyGroup.Legs):F2}  torso {c.GroupStiffness(BodyGroup.Torso):F2}  " +
                $"arms {c.GroupStiffness(BodyGroup.Arms):F2}  neck {c.GroupStiffness(BodyGroup.Neck):F2}\n" +
                $"pelvis h {c.PelvisHeight:F2} m   head h {c.HeadHeight:F2} m   tilt {c.PelvisTilt:F1}°\n" +
                $"COM err (r,f) {c.BalanceError.x * 100f:F1}, {c.BalanceError.y * 100f:F1} cm   ankle off {c.AnkleOffset.x:F1}/{c.AnkleOffset.z:F1}°  trunk off {c.TrunkOffset:F1}°\n" +
                $"stepper {c.Stepper.Phase}  steps {c.Stepper.StepsTaken}\n" +
                $"balance {(c.balanceEnabled ? "on" : "OFF")}   upright assist {(c.uprightAssist ? "<color=yellow>ON (crutch)</color>" : "off")}\n" +
                $"dt {Time.fixedDeltaTime * 1000f:F1} ms   solver {Physics.defaultSolverIterations}/{Physics.defaultSolverVelocityIterations}\n" +
                "keys: 1 stand 2 sit 3 brace 4 protect 5 curl 6 stagger 7 limp | U assist  B balance  P push";

            float h = style.CalcHeight(new GUIContent(text), 700f) + 12f;
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(new Rect(8, 8, 720, h), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(16, 12, 700, h), text, style);
        }

        static string ReactionText(ReactionManager rm)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append($"selector: <b>{rm.Selector?.Name}</b>   phase: {rm.Phase}\n");
            sb.Append($"event: {rm.LastEventText ?? "-"}\n");
            var d = rm.LastDecision;
            if (d != null)
            {
                sb.Append($"behaviour: <b>{d.Behaviour}</b>  stiffness {d.Stiffness:F2}  recover {d.Recover}  latency {d.LatencyMs:F1} ms");
                if (d.TimedOut) sb.Append("  <color=orange>TIMEOUT</color>");
                if (d.Fallback) sb.Append("  <color=orange>fallback: rules</color>");
                sb.Append("\nprobabilities: ");
                foreach (var b in Behaviours.All)
                    sb.Append($"{b} {(d.Probabilities.TryGetValue(b, out var p) ? p : 0f):F2}  ");
                sb.Append("\n");
                if (!string.IsNullOrEmpty(rm.LastNote)) sb.Append($"note: {rm.LastNote}\n");
            }
            return sb.ToString();
        }
    }
}
