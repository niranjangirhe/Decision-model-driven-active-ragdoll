using System.Linq;
using System.Text;
using UnityEngine;

namespace ActiveRagdoll
{
    /// <summary>
    /// On-screen readout of the reaction pipeline for one character: the event as the decision model
    /// read it, the chosen behaviour with its probability, the next candidates, the latency, and the
    /// stiffness the body is running at. Read-only: scenarios run by themselves (see ScenarioRunner).
    /// </summary>
    public class RagdollDebugOverlay : MonoBehaviour
    {
        public ActiveRagdollController character;

        GUIStyle style;

        void OnGUI()
        {
            if (character == null || character.Body == null) return;
            if (style == null) style = new GUIStyle(GUI.skin.label) { fontSize = 20, richText = true, wordWrap = true };

            var rm = character.GetComponent<ReactionManager>();
            var runner = FindAnyObjectByType<ScenarioRunner>();
            var sb = new StringBuilder();
            if (runner != null) sb.Append($"<b>Scenario {(int)runner.scenario}</b>, seed {runner.seed}    selector: <b>{rm?.Selector?.Name}</b>\n");

            if (rm != null)
            {
                sb.Append($"<color=#bbbbbb>event:</color> {rm.LastEventText ?? "waiting..."}\n");
                var d = rm.LastDecision;
                if (d != null)
                {
                    float p = d.Probabilities.TryGetValue(d.Behaviour, out var v) ? v : 0f;
                    sb.Append($"<color=#bbbbbb>decision:</color> <b><color=#ffd27a>{d.Behaviour}</color></b> (p {p:F2})");
                    var others = d.Probabilities.Where(kv => kv.Key != d.Behaviour && kv.Value > 0.01f)
                                                .OrderByDescending(kv => kv.Value).Take(2).ToList();
                    if (others.Count > 0) sb.Append("   next: " + string.Join(", ", others.Select(kv => $"{kv.Key} {kv.Value:F2}")));
                    sb.Append($"\n<color=#bbbbbb>latency:</color> {d.LatencyMs:F1} ms");
                    if (d.Fallback) sb.Append("  <color=orange>(timed out, rules fallback)</color>");
                    sb.Append($"    stiffness {d.Stiffness:F2}    recover {(d.Recover ? "yes" : "no")}\n");
                }
                sb.Append($"<color=#bbbbbb>muscles:</color> legs {character.GroupStiffness(BodyGroup.Legs):F2}  torso {character.GroupStiffness(BodyGroup.Torso):F2}  " +
                          $"arms {character.GroupStiffness(BodyGroup.Arms):F2}  neck {character.GroupStiffness(BodyGroup.Neck):F2}    {rm.Phase}");
            }

            string text = sb.ToString();
            float w = Mathf.Min(1000f, Screen.width - 16f);
            float h = style.CalcHeight(new GUIContent(text), w - 16f) + 12f;
            GUI.color = new Color(0f, 0f, 0f, 0.65f);
            GUI.DrawTexture(new Rect(8, 8, w, h), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(16, 12, w - 16f, h), text, style);
        }
    }
}
