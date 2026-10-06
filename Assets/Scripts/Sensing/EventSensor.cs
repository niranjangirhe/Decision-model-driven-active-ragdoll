using System;
using System.Collections.Generic;
using UnityEngine;

namespace ActiveRagdoll
{
    /// <summary>
    /// Detects events from physics, not from scripted triggers:
    ///   * Impact: a collision impulse from a moving object on a body link.
    ///   * Incoming: a moving object whose ballistic path is predicted to touch a link soon.
    ///     The object is "seen" once it is within <see cref="sensingRadius"/> of the chest.
    ///   * Trip: a foot hits an object marked <see cref="TripHazard"/>.
    ///   * FootingLost: both feet leave the ground while the character is meant to be standing.
    /// Each event is filled in with the character's state and surroundings and raised through
    /// <see cref="EventSensed"/>. The sensor does not decide anything.
    /// </summary>
    [RequireComponent(typeof(ActiveRagdollController))]
    public class EventSensor : MonoBehaviour, ISimStep
    {
        [Header("Impacts")]
        [Tooltip("Smallest collision impulse (N*s) from a moving object that counts as an impact.")]
        public float minImpactImpulse = 2f;

        [Header("Incoming objects")]
        public float sensingRadius = 2.5f;
        public float lookahead = 1.0f;
        public float predictionStep = 0.01f;
        [Tooltip("Predicted gap (m) between object and body below which it counts as a hit.")]
        public float hitMargin = 0.03f;
        public float minIncomingSpeed = 1.0f;
        [Tooltip("Objects predicted to hit sooner than this (s) are too late to anticipate; they are reported on impact instead.")]
        public float minAnticipationTime = 0.2f;

        [Header("Footing")]
        public float footingLostDelay = 0.15f;

        public event Action<CharacterEvent> EventSensed;

        /// <summary>Set by a walking controller (scenario 3); the sensor only reports trips while walking.</summary>
        public bool IsWalking { get; set; }

        // Outcome measurements for the experiment log.
        public bool HeadHit { get; private set; }
        public float HeadPeakImpulse { get; private set; }

        public int StepOrder => 0;

        ActiveRagdollController ragdoll;
        readonly Dictionary<Rigidbody, Vector3> lastVelocity = new Dictionary<Rigidbody, Vector3>();
        readonly HashSet<Rigidbody> announced = new HashSet<Rigidbody>();
        readonly HashSet<Rigidbody> impactReported = new HashSet<Rigidbody>();
        // Collision objects are reused by Unity after the callback returns, so copy what we need.
        struct Contact { public string Link; public Collider Other; public Rigidbody Rb; public bool OtherIsArticulation; public float Impulse; public bool Enter; }
        readonly List<Contact> contacts = new List<Contact>();
        readonly Collider[] overlap = new Collider[64];
        float feetOffGroundTime;
        bool footingLostReported;
        double lastTripTime = -10;

        void Awake() => ragdoll = GetComponent<ActiveRagdollController>();
        void OnEnable() { SimDriver.Register(this); ragdoll = GetComponent<ActiveRagdollController>(); ragdoll.BodyBuilt += AttachReporters; }
        void OnDisable() { SimDriver.Unregister(this); if (ragdoll != null) ragdoll.BodyBuilt -= AttachReporters; }
        void Start() => AttachReporters();

        /// <summary>Clear per-trial memory (called when the body is rebuilt).</summary>
        public void ResetTrial()
        {
            lastVelocity.Clear(); announced.Clear(); impactReported.Clear(); contacts.Clear();
            HeadHit = false; HeadPeakImpulse = 0f;
            feetOffGroundTime = 0f; footingLostReported = false;
        }

        void AttachReporters()
        {
            ResetTrial();
            if (ragdoll.Body == null) return;
            foreach (var ab in ragdoll.Body.Links.Values)
            {
                var r = ab.GetComponent<LinkContactReporter>();
                if (r == null) r = ab.gameObject.AddComponent<LinkContactReporter>();
                r.sensor = this;
            }
        }

        /// <summary>Called from the links during the physics step; processed in the next SimStep.</summary>
        public void OnLinkCollision(string link, Collision c, bool enter) => contacts.Add(new Contact
        {
            Link = link, Other = c.collider, Rb = c.rigidbody, OtherIsArticulation = c.articulationBody != null,
            Impulse = c.impulse.magnitude, Enter = enter,
        });

        public void SimStep(float dt)
        {
            if (ragdoll.Body == null) return;
            ProcessContacts(dt);
            ScanForIncoming();
        }

        // ------------------------------------------------------------------ contacts

        void ProcessContacts(float dt)
        {
            bool footOnGround = false;
            // Strongest impact per object this step.
            var best = new Dictionary<Rigidbody, (string link, float impulse)>();

            foreach (var k in contacts)
            {
                if (k.Other == null) continue;
                string link = k.Link; bool enter = k.Enter; var rb = k.Rb;
                bool isEnvironment = rb == null && !k.OtherIsArticulation;

                if (link.StartsWith("Foot") && isEnvironment) footOnGround = true;

                if (enter && link.StartsWith("Foot") && k.Other.GetComponentInParent<TripHazard>() != null
                    && IsWalking && SimDriver.Time - lastTripTime > 1.0)
                {
                    lastTripTime = SimDriver.Time;
                    var ev = NewEvent(EventKind.Trip, link);
                    Raise(ev);
                }

                if (rb == null || rb.isKinematic) continue;
                float j = k.Impulse;
                if (link == "Head")
                {
                    HeadHit = true;
                    HeadPeakImpulse = Mathf.Max(HeadPeakImpulse, j);
                }
                if (j < minImpactImpulse) continue;
                if (!best.TryGetValue(rb, out var cur) || j > cur.impulse) best[rb] = (link, j);
            }
            contacts.Clear();

            foreach (var kv in best)
            {
                if (impactReported.Contains(kv.Key)) continue; // one impact event per object
                impactReported.Add(kv.Key);
                var ev = NewEvent(EventKind.Impact, kv.Value.link);
                ev.ImpulseNs = kv.Value.impulse;
                ev.ObjectMassKg = kv.Key.mass;
                ev.ObjectId = kv.Key.GetHashCode();
                Vector3 v = lastVelocity.TryGetValue(kv.Key, out var lv) ? lv : kv.Key.linearVelocity;
                FillDirection(ev, v);
                Raise(ev);
            }

            // Footing: only meaningful when the pose expects the feet to carry the body.
            bool expectsFeet = ragdoll.CurrentPose != null && ragdoll.CurrentPose.Support == SupportMode.Feet;
            if (expectsFeet && !footOnGround) feetOffGroundTime += dt; else feetOffGroundTime = 0f;
            if (footOnGround) footingLostReported = false;
            if (feetOffGroundTime > footingLostDelay && !footingLostReported)
            {
                footingLostReported = true;
                Raise(NewEvent(EventKind.FootingLost, "Pelvis"));
            }
        }

        // ------------------------------------------------------------------ anticipation

        void ScanForIncoming()
        {
            Vector3 chest = ragdoll.Body.Links["Chest"].worldCenterOfMass;
            int n = Physics.OverlapSphereNonAlloc(chest, sensingRadius + 2f, overlap);
            for (int i = 0; i < n; i++)
            {
                var rb = overlap[i].attachedRigidbody;
                if (rb == null || rb.isKinematic) continue;
                Vector3 p = rb.worldCenterOfMass, v = rb.linearVelocity;
                lastVelocity[rb] = v;
                if (announced.Contains(rb) || impactReported.Contains(rb)) continue;
                if (Vector3.Distance(p, chest) > sensingRadius || v.magnitude < minIncomingSpeed) continue;

                if (PredictHit(rb, overlap[i], out string link, out float tti, out Vector3 vAtHit))
                {
                    announced.Add(rb);
                    if (tti < minAnticipationTime) continue; // too late to anticipate: wait for the impact
                    var ev = NewEvent(EventKind.Incoming, link);
                    ev.TimeToImpact = tti;
                    ev.ObjectMassKg = rb.mass;
                    ev.ObjectSpeed = vAtHit.magnitude;
                    ev.ObjectId = rb.GetHashCode();
                    Vector3 fwd, right; ragdoll.HeadingFrame(out fwd, out right);
                    Vector3 off = p - ragdoll.Body.Pelvis.transform.position;
                    ev.ObjectOffset = new Vector2(Vector3.Dot(off, right), Vector3.Dot(off, fwd));
                    FillDirection(ev, vAtHit);
                    Raise(ev);
                }
            }
        }

        /// <summary>
        /// Step the object's ballistic path (gravity only, no drag) and find the first body link it
        /// comes within <see cref="hitMargin"/> of. The body is assumed to stay where it is.
        /// </summary>
        bool PredictHit(Rigidbody rb, Collider objCollider, out string link, out float tti, out Vector3 vAtHit)
        {
            link = null; tti = 0f; vAtHit = Vector3.zero;
            float radius = objCollider.bounds.extents.magnitude / Mathf.Sqrt(3f); // roughly the object's radius
            Vector3 p0 = rb.worldCenterOfMass, v0 = rb.linearVelocity, g = Physics.gravity;
            for (float t = 0f; t <= lookahead; t += predictionStep)
            {
                Vector3 p = p0 + v0 * t + 0.5f * g * t * t;
                foreach (var col in ragdoll.Body.Colliders)
                {
                    float gap = Vector3.Distance(col.ClosestPoint(p), p) - radius;
                    if (gap < hitMargin)
                    {
                        link = col.name; tti = t; vAtHit = v0 + g * t;
                        return true;
                    }
                }
            }
            return false;
        }

        // ------------------------------------------------------------------ event filling

        CharacterEvent NewEvent(EventKind kind, string link)
        {
            var ev = new CharacterEvent { Kind = kind, BodyPart = link, SimTime = SimDriver.Time, State = CurrentState() };
            FillSurroundings(ev);
            return ev;
        }

        public CharacterState CurrentState()
        {
            bool seated = ragdoll.CurrentPose != null && ragdoll.CurrentPose.Support == SupportMode.Seat;
            if (seated && ragdoll.PelvisHeight > 0.4f) return CharacterState.Sitting;
            if (ragdoll.PelvisHeight < 0.45f || ragdoll.PelvisTilt > 60f) return CharacterState.Lying;
            return IsWalking ? CharacterState.Walking : CharacterState.Standing;
        }

        void FillDirection(CharacterEvent ev, Vector3 v)
        {
            Vector3 fwd, right; ragdoll.HeadingFrame(out fwd, out right);
            Vector3 h = Vector3.ProjectOnPlane(v, Vector3.up);
            ev.FromAbove = -v.y > h.magnitude;
            ev.TravelDir = h.magnitude > 0.5f ? new Vector2(Vector3.Dot(h, right), Vector3.Dot(h, fwd)).normalized : Vector2.zero;
        }

        /// <summary>Free distance in the four heading directions (chest-height rays) and the nearest support in reach.</summary>
        void FillSurroundings(CharacterEvent ev)
        {
            Vector3 fwd, right; ragdoll.HeadingFrame(out fwd, out right);
            Vector3 origin = ragdoll.Body.Links["Chest"].worldCenterOfMass;
            ev.FreeLeft = FreeDistance(origin, -right);
            ev.FreeRight = FreeDistance(origin, right);
            ev.FreeFront = FreeDistance(origin, fwd);
            ev.FreeBack = FreeDistance(origin, -fwd);

            float bestDist = 1.0f; // arm's reach from the chest
            int n = Physics.OverlapSphereNonAlloc(origin, bestDist, overlap);
            for (int i = 0; i < n; i++)
            {
                var s = overlap[i].GetComponentInParent<GraspableSupport>();
                if (s == null) continue;
                Vector3 d = overlap[i].ClosestPoint(origin) - origin;
                if (d.magnitude < bestDist)
                {
                    bestDist = d.magnitude;
                    ev.SupportName = s.label;
                    ev.SupportDir = new Vector2(Vector3.Dot(d, right), Vector3.Dot(d, fwd)).normalized;
                }
            }
        }

        float FreeDistance(Vector3 origin, Vector3 dir)
        {
            float best = CharacterEvent.SurroundRange;
            foreach (var hit in Physics.RaycastAll(origin, dir, CharacterEvent.SurroundRange))
            {
                if (hit.collider.GetComponentInParent<ArticulationBody>() != null) continue; // own body
                if (hit.rigidbody != null && !hit.rigidbody.isKinematic) continue;          // moving objects are not walls
                best = Mathf.Min(best, hit.distance);
            }
            return best;
        }

        void Raise(CharacterEvent ev) => EventSensed?.Invoke(ev);
    }
}
