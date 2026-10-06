using System.IO;
using UnityEngine;

namespace ActiveRagdoll
{
    /// <summary>
    /// Append-only log of every event string the serialiser produced, plus the decision that
    /// followed. Written to Results/event_log.txt next to the Assets folder, and to the console.
    /// </summary>
    public static class EventLog
    {
        public static string Path => System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", "Results", "event_log.txt"));

        /// <summary>Free-form context written into each line (e.g. "S1 seed=12 selector=rules").</summary>
        public static string Context = "";

        public static bool EchoToConsole = true;

        public static void Write(string kind, string text)
        {
            string line = $"{SimDriver.Time,8:F3}\t{Context}\t{kind}\t{text}";
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path));
            File.AppendAllText(Path, line + "\n");
            if (EchoToConsole) Debug.Log("[EventLog] " + line);
        }
    }
}
