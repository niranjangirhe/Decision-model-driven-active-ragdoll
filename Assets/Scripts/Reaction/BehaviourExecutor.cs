using UnityEngine;

namespace ActiveRagdoll
{
    /// <summary>
    /// Turns a chosen behaviour and its parameters into calls on the ragdoll (pose targets,
    /// stiffness, stepping). This is the only place that knows what each behaviour name means.
    /// </summary>
    public class BehaviourExecutor
    {
        readonly ActiveRagdollController ragdoll;
        public BehaviourExecutor(ActiveRagdollController r) { ragdoll = r; }

        /// <summary>Map the decision's 0..1 stiffness to a group multiplier: 0 -> 0.05 (limp), 1 -> 1.5 (rigid).</summary>
        public static float StiffnessMultiplier(float s) => Mathf.Lerp(0.05f, 1.5f, Mathf.Clamp01(s));

        /// <summary>Run the behaviour. Returns a note for the log when the behaviour had to be adapted.</summary>
        public string Execute(Decision d, CharacterEvent ev, bool seated)
        {
            ragdoll.SetAllStiffness(StiffnessMultiplier(d.Stiffness));
            string note = "";

            switch (d.Behaviour)
            {
                case Behaviours.Brace:
                    ragdoll.SetPose(seated ? PoseLibrary.Seated(PoseLibrary.Brace) : PoseLibrary.Brace, 0.2f);
                    break;

                case Behaviours.ProtectHead:
                    ragdoll.SetPose(seated ? PoseLibrary.Seated(PoseLibrary.ProtectHead) : PoseLibrary.ProtectHead, 0.2f);
                    break;

                case Behaviours.StaggerRecover:
                    if (seated)
                    {
                        // There is no step to take from a bench; brace instead.
                        ragdoll.SetPose(PoseLibrary.Seated(PoseLibrary.Brace), 0.2f);
                        note = "stagger_recover while seated: ran seated brace";
                    }
                    else
                    {
                        Vector2 dir = ev.TravelDir != Vector2.zero ? ev.TravelDir : new Vector2(0f, 1f);
                        ragdoll.StartStagger(dir);
                    }
                    break;

                case Behaviours.DodgeSideways:
                    float side = DodgeSide(ev);
                    ragdoll.SetPose(PoseLibrary.DodgeSideways(side, seated), 0.15f);
                    note = side > 0 ? "dodge right" : "dodge left";
                    break;

                case Behaviours.FallAndCurl:
                    ragdoll.SetPose(PoseLibrary.Curl, 0.25f);
                    break;

                case Behaviours.GoLimp:
                    ragdoll.SetPose(PoseLibrary.Limp, 0.1f);
                    break;

                case Behaviours.GrabSupport:
                    if (ev.SupportName != null)
                    {
                        ragdoll.SetPose(PoseLibrary.ReachFor(ev.SupportDir, seated), 0.2f);
                        note = "reach only, no grasp constraint";
                    }
                    else
                    {
                        ragdoll.SetPose(seated ? PoseLibrary.Seated(PoseLibrary.Brace) : PoseLibrary.Brace, 0.2f);
                        note = "grab_support stub: no support within reach, ran brace";
                    }
                    break;

                default:
                    note = "unknown behaviour '" + d.Behaviour + "', ignored";
                    break;
            }
            return note;
        }

        /// <summary>Lean toward the side with more free space; if both are open, away from the object.</summary>
        static float DodgeSide(CharacterEvent ev)
        {
            if (Mathf.Abs(ev.FreeLeft - ev.FreeRight) > 0.3f) return ev.FreeRight > ev.FreeLeft ? 1f : -1f;
            if (Mathf.Abs(ev.ObjectOffset.x) > 0.02f) return ev.ObjectOffset.x > 0f ? -1f : 1f;
            if (ev.TravelDir.x != 0f) return ev.TravelDir.x > 0f ? -1f : 1f;
            return 1f;
        }
    }
}
