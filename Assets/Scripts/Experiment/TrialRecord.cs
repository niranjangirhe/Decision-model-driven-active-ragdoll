using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace ActiveRagdoll
{
    /// <summary>One row of the experiment CSV: what happened in one trial under one selector.</summary>
    public class TrialRecord
    {
        public int Scenario;
        public int Seed;
        public string Selector;
        public string Setup;

        // First event of the trial and the decision for it.
        public string EventKind = "";
        public string EventText = "";
        public string Behaviour = "";
        public Dictionary<string, float> Probabilities = new Dictionary<string, float>();
        public float Stiffness = float.NaN;
        public bool? Recover;
        public float LatencyMs = float.NaN;
        public float ServerLatencyMs = float.NaN;
        public string Device = "";
        public bool TimedOut, Fallback;
        public string Error = "";
        public string Note = "";
        public int EventsHandled;

        // Outcome.
        public bool HeadHit;
        public float HeadPeakImpulse;
        public bool FellEver;
        public float MinPelvisHeight;
        public float TimeToStable = float.NaN;
        public bool UprightAtEnd;
        public bool BackInStartPose;

        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static string Header() => string.Join(",", new[]
        {
            "scenario", "seed", "selector", "setup", "event_kind", "event_text", "behaviour",
        }.Concat(Behaviours.All.Select(b => "p_" + b)).Concat(new[]
        {
            "stiffness", "recover_decided", "latency_ms", "server_latency_ms", "device",
            "timed_out", "fallback", "error", "note", "events_handled",
            "head_hit", "head_peak_impulse", "fell", "min_pelvis_height",
            "time_to_stable", "recovered", "back_in_start_pose",
        }));

        public string ToCsv()
        {
            var f = new List<string>
            {
                Scenario.ToString(Inv), Seed.ToString(Inv), Q(Selector), Q(Setup), Q(EventKind), Q(EventText), Q(Behaviour),
            };
            foreach (var b in Behaviours.All) f.Add(Probabilities.TryGetValue(b, out var p) ? N(p) : "");
            f.AddRange(new[]
            {
                N(Stiffness), Recover.HasValue ? B(Recover.Value) : "", N(LatencyMs), N(ServerLatencyMs), Q(Device),
                B(TimedOut), B(Fallback), Q(Error), Q(Note), EventsHandled.ToString(Inv),
                B(HeadHit), N(HeadPeakImpulse), B(FellEver), N(MinPelvisHeight),
                N(TimeToStable), B(UprightAtEnd), B(BackInStartPose),
            });
            return string.Join(",", f);
        }

        public string Summary() =>
            $"S{Scenario} seed={Seed} {Selector}: {EventText} -> {Behaviour}" +
            $"{(Fallback ? " (fallback)" : "")} {LatencyMs:F1}ms | headHit={HeadHit} fell={FellEver} " +
            $"stable@{TimeToStable:F2}s upright={UprightAtEnd}";

        public static void Append(string path, IEnumerable<TrialRecord> rows)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            bool header = !File.Exists(path);
            var sb = new StringBuilder();
            if (header) sb.AppendLine(Header());
            foreach (var r in rows) sb.AppendLine(r.ToCsv());
            File.AppendAllText(path, sb.ToString(), new UTF8Encoding(false));
        }

        static string N(float v) => float.IsNaN(v) ? "" : v.ToString("0.####", Inv);
        static string B(bool v) => v ? "1" : "0";
        static string Q(string s) => "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";
    }
}
