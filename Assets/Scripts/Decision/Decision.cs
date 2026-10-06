using System;
using System.Collections.Generic;

namespace ActiveRagdoll
{
    /// <summary>The fixed v1 behaviour list. The decision layer may only pick from these.</summary>
    public static class Behaviours
    {
        public const string Brace = "brace";
        public const string ProtectHead = "protect_head";
        public const string StaggerRecover = "stagger_recover";
        public const string DodgeSideways = "dodge_sideways";
        public const string FallAndCurl = "fall_and_curl";
        public const string GoLimp = "go_limp";
        public const string GrabSupport = "grab_support";

        public static readonly string[] All =
            { Brace, ProtectHead, StaggerRecover, DodgeSideways, FallAndCurl, GoLimp, GrabSupport };
    }

    /// <summary>What a selector returns for one event.</summary>
    public class Decision
    {
        public string Behaviour;
        /// <summary>Probability per behaviour (one-hot for rule-based selectors).</summary>
        public Dictionary<string, float> Probabilities = new Dictionary<string, float>();
        /// <summary>Muscle stiffness, 0 = go limp, 1 = rigid resist. Mapped to the group multipliers.</summary>
        public float Stiffness = 1f;
        /// <summary>Try to get back to the base pose afterwards?</summary>
        public bool Recover = true;

        /// <summary>Wall-clock time the selector took to answer.</summary>
        public float LatencyMs;
        public string Selector;
        public bool TimedOut;
        /// <summary>True when the rules selector answered because the primary selector did not.</summary>
        public bool Fallback;
        public string Error;

        public static Decision OneHot(string behaviour, float stiffness, bool recover, string selector)
        {
            var d = new Decision { Behaviour = behaviour, Stiffness = stiffness, Recover = recover, Selector = selector };
            foreach (var b in Behaviours.All) d.Probabilities[b] = b == behaviour ? 1f : 0f;
            return d;
        }
    }

    /// <summary>
    /// A behaviour selector. Requests never block: the selector calls <paramref name="onDone"/> when it
    /// has an answer, which may be in the same call (rules) or frames later (a server).
    /// </summary>
    public interface IBehaviourSelector
    {
        string Name { get; }
        void Request(CharacterEvent ev, string eventText, string[] allowed, Action<Decision> onDone);
    }
}
