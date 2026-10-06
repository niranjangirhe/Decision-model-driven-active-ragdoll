using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace ActiveRagdoll
{
    /// <summary>
    /// Turns a <see cref="CharacterEvent"/> into a short English description with fixed templates.
    /// Deterministic: the same event always gives the same string (numbers are rounded and
    /// binned, invariant culture). Every string is logged by <see cref="EventLog"/>.
    ///
    /// Example: "Character is sitting on a bench. A heavy object (about 8 kg) is falling toward
    /// the head, about 0.4 s until impact. Open space to the left and to the right."
    /// </summary>
    public static class EventSerializer
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        static readonly Dictionary<string, string> PartNames = new Dictionary<string, string>
        {
            { "Head", "head" }, { "Chest", "chest" }, { "Abdomen", "stomach" }, { "Pelvis", "hips" },
            { "UpperArm_L", "left shoulder" }, { "UpperArm_R", "right shoulder" },
            { "Forearm_L", "left arm" }, { "Forearm_R", "right arm" },
            { "Thigh_L", "left thigh" }, { "Thigh_R", "right thigh" },
            { "Shin_L", "left leg" }, { "Shin_R", "right leg" },
            { "Foot_L", "left foot" }, { "Foot_R", "right foot" },
        };

        public static string PartName(string link) => link != null && PartNames.TryGetValue(link, out var n) ? n : "body";

        public static string Serialize(CharacterEvent ev)
        {
            var sb = new StringBuilder();
            sb.Append(StateSentence(ev.State)).Append(' ');

            switch (ev.Kind)
            {
                case EventKind.Incoming:
                    sb.Append($"{Cap(MassWord(ev.ObjectMassKg))} object (about {Kg(ev.ObjectMassKg)}) is ");
                    sb.Append(ev.FromAbove ? "falling toward" : "flying toward");
                    sb.Append($" the {PartName(ev.BodyPart)}");
                    if (!ev.FromAbove) sb.Append($" from {FromWord(ev.TravelDir)}");
                    sb.Append($", about {ev.TimeToImpact.ToString("0.0", Inv)} s until impact.");
                    break;
                case EventKind.Impact:
                    sb.Append($"{Cap(StrengthWord(ev.ImpulseNs))} impact to the {PartName(ev.BodyPart)} ");
                    sb.Append(ev.FromAbove ? "from above." : $"from {FromWord(ev.TravelDir)}.");
                    break;
                case EventKind.Trip:
                    sb.Append($"The {PartName(ev.BodyPart)} hit an obstacle mid-step.");
                    break;
                case EventKind.FootingLost:
                    sb.Append("Both feet have lost contact with the ground.");
                    break;
            }

            string surround = SurroundingsSentence(ev);
            if (surround.Length > 0) sb.Append(' ').Append(surround);
            if (ev.SupportName != null)
                sb.Append($" A {ev.SupportName} is within reach {SideWord(ev.SupportDir)}.");
            return sb.ToString();
        }

        static string StateSentence(CharacterState s)
        {
            switch (s)
            {
                case CharacterState.Sitting: return "Character is sitting on a bench.";
                case CharacterState.Walking: return "Character is walking.";
                case CharacterState.Lying: return "Character is lying on the ground.";
                default: return "Character is standing.";
            }
        }

        // Bins are fixed so the text does not jitter with tiny numeric differences.
        public static string MassWord(float kg) => kg < 2f ? "a light" : kg < 6f ? "a medium-weight" : "a heavy";

        public static string StrengthWord(float impulseNs) =>
            impulseNs < 10f ? "a light" : impulseNs < 25f ? "a moderate" : impulseNs < 45f ? "a strong" : "a very strong";

        static string Kg(float kg) => kg < 1f ? "1 kg or less" : $"{Mathf.RoundToInt(kg)} kg";

        /// <summary>Where something comes from, given the direction it travels in (heading frame).</summary>
        public static string FromWord(Vector2 travel)
        {
            if (travel == Vector2.zero) return "nearby";
            Vector2 from = -travel;
            if (Mathf.Abs(from.y) >= Mathf.Abs(from.x)) return from.y > 0 ? "the front" : "behind";
            return from.x > 0 ? "the right" : "the left";
        }

        static string SideWord(Vector2 dir)
        {
            if (Mathf.Abs(dir.y) >= Mathf.Abs(dir.x)) return dir.y > 0 ? "in front" : "behind";
            return dir.x > 0 ? "to the right" : "to the left";
        }

        /// <summary>Walls within 1.5 m, nearest first; then which sides (left/right) are open.</summary>
        static string SurroundingsSentence(CharacterEvent ev)
        {
            var parts = new List<string>();
            var walls = new List<(float d, string side)>
            {
                (ev.FreeLeft, "to the left"), (ev.FreeRight, "to the right"),
                (ev.FreeFront, "in front"), (ev.FreeBack, "behind"),
            };
            walls.Sort((a, b) => a.d.CompareTo(b.d));
            foreach (var (d, side) in walls)
                if (d < 1.5f) parts.Add($"Wall {Metres(d)} {side}.");

            bool leftOpen = ev.FreeLeft >= CharacterEvent.SurroundRange - 0.01f;
            bool rightOpen = ev.FreeRight >= CharacterEvent.SurroundRange - 0.01f;
            if (leftOpen && rightOpen) parts.Add("Open space to the left and to the right.");
            else if (leftOpen) parts.Add("Open space to the left.");
            else if (rightOpen) parts.Add("Open space to the right.");
            return string.Join(" ", parts);
        }

        /// <summary>Distance rounded to the nearest 0.5 m, at least 0.5 m.</summary>
        static string Metres(float d) => $"{Mathf.Max(0.5f, Mathf.Round(d * 2f) / 2f).ToString("0.0", Inv)} m";

        static string Cap(string s) => char.ToUpperInvariant(s[0]) + s.Substring(1);
    }
}
