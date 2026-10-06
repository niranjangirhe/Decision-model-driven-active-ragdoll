using System;
using UnityEngine;

namespace ActiveRagdoll
{
    /// <summary>Selector A: always the same behaviour.</summary>
    public class FixedSelector : IBehaviourSelector
    {
        public string Behaviour = Behaviours.Brace;
        public float Stiffness = 1f;
        public string Name => Behaviour == Behaviours.Brace ? "fixed" : "fixed:" + Behaviour;

        public void Request(CharacterEvent ev, string text, string[] allowed, Action<Decision> onDone)
        {
            onDone(Decision.OneHot(Behaviour, Stiffness, true, Name));
        }
    }

    /// <summary>
    /// Selector B: hand-written if/else on the event's numeric features (it never reads the text).
    /// Also the fallback when a model selector times out. Thresholds come from the ragdoll push
    /// tests: a standing body without a step survives about 15-25 N*s; curling while seated survives
    /// harder side pushes than sitting upright.
    /// </summary>
    public class RulesSelector : IBehaviourSelector
    {
        public string Name => "rules";

        public void Request(CharacterEvent ev, string text, string[] allowed, Action<Decision> onDone)
        {
            onDone(Decide(ev));
        }

        public Decision Decide(CharacterEvent ev)
        {
            bool seated = ev.State == CharacterState.Sitting;
            bool lying = ev.State == CharacterState.Lying;
            bool openSide = ev.FreeLeft > 1.0f || ev.FreeRight > 1.0f;
            bool heavy = ev.ObjectMassKg >= 6f;

            if (lying) return D(Behaviours.FallAndCurl, 0.6f, false);

            switch (ev.Kind)
            {
                case EventKind.Incoming:
                    if (ev.HitsHead)
                    {
                        // Enough time to move out of the way, and somewhere to go: dodge. Otherwise cover up.
                        if (heavy && ev.TimeToImpact >= 0.3f && openSide) return D(Behaviours.DodgeSideways, 1f, true);
                        return D(Behaviours.ProtectHead, 1f, true);
                    }
                    if (heavy && ev.TimeToImpact >= 0.3f && openSide && !seated) return D(Behaviours.DodgeSideways, 1f, true);
                    return D(Behaviours.Brace, 0.9f, true);

                case EventKind.Impact:
                    if (ev.HitsHead) return D(Behaviours.ProtectHead, 1f, true);
                    if (seated)
                        return ev.ImpulseNs < 25f ? D(Behaviours.Brace, 0.8f, true) : D(Behaviours.FallAndCurl, 0.7f, false);
                    if (ev.ImpulseNs < 15f) return D(Behaviours.Brace, 0.7f, true);
                    if (ev.SupportName != null && ev.ImpulseNs < 60f) return D(Behaviours.GrabSupport, 1f, true);
                    if (ev.ImpulseNs < 45f) return D(Behaviours.StaggerRecover, 1f, true);
                    return D(Behaviours.FallAndCurl, 0.6f, false);

                case EventKind.Trip:
                    return D(Behaviours.StaggerRecover, 1f, true);

                default: // FootingLost
                    return D(Behaviours.FallAndCurl, 0.6f, false);
            }
        }

        Decision D(string b, float stiffness, bool recover) => Decision.OneHot(b, stiffness, recover, Name);
    }
}

namespace ActiveRagdoll
{
    /// <summary>
    /// Test-only selector: answers like the rules selector, but from a thread-pool thread after
    /// <see cref="DelayMs"/> of wall-clock time. Used to check the latency and timeout handling
    /// ("slow:150" answers in time, "slow:400" times out).
    /// </summary>
    public class SlowTestSelector : IBehaviourSelector
    {
        public int DelayMs = 150;
        public string Name => "slow:" + DelayMs;
        readonly RulesSelector rules = new RulesSelector();

        public void Request(CharacterEvent ev, string text, string[] allowed, Action<Decision> onDone)
        {
            var d = rules.Decide(ev);
            d.Selector = Name;
            System.Threading.Tasks.Task.Delay(DelayMs).ContinueWith(_ => onDone(d));
        }
    }
}
