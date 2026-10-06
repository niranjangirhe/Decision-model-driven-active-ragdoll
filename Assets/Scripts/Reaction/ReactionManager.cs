using System.Diagnostics;
using UnityEngine;

namespace ActiveRagdoll
{
    /// <summary>
    /// Connects sensing to acting, once per event:
    ///   1. A reflex right away: a short stiffness bump on all joints.
    ///   2. The event is serialised to text, logged, and sent to the selector without blocking.
    ///   3. When the answer arrives, the behaviour runs. In simulated time it starts at
    ///      event time + the selector's measured wall-clock latency, so a slow model costs the
    ///      character the same reaction time in play mode and in scripted batch runs.
    ///   4. If no answer arrives within <see cref="timeoutMs"/> of wall-clock time, the rules
    ///      selector answers instead and the timeout is logged.
    ///   5. After <see cref="recoverAfter"/> seconds, if the decision said to recover and the
    ///      character is still up, it blends back to its base pose.
    /// </summary>
    [RequireComponent(typeof(ActiveRagdollController), typeof(EventSensor))]
    public class ReactionManager : MonoBehaviour, ISimStep
    {
        [Tooltip("fixed | rules | laya | gliclass | slow:<ms> (test)")]
        public string selectorName = "rules";
        public float timeoutMs = 200f;
        public float reflexStiffness = 1.6f;
        public float reflexDuration = 0.15f;
        [Tooltip("Ignore new events for this long after a decision was applied (s).")]
        public float cooldown = 0.5f;
        public float recoverAfter = 2.0f;

        public IBehaviourSelector Selector { get; set; }
        readonly RulesSelector rules = new RulesSelector();

        // ---- State shown on the overlay and logged by the experiment harness.
        public string Phase { get; private set; } = "idle";
        public CharacterEvent LastEvent { get; private set; }
        public string LastEventText { get; private set; }
        public Decision LastDecision { get; private set; }
        public string LastNote { get; private set; }
        public int EventsHandled { get; private set; }
        /// <summary>The trial's first handled event and its decision (what the experiment compares).</summary>
        public string FirstEventText { get; private set; }
        public CharacterEvent FirstEvent { get; private set; }
        public Decision FirstDecision { get; private set; }
        public string FirstNote { get; private set; }
        public double DecisionAppliedAt { get; private set; } = -1;

        /// <summary>
        /// While waiting for a selector, scripted stepping must not run simulated time ahead of the
        /// wall-clock time already spent waiting. Null when nothing is pending.
        /// </summary>
        public double? HoldSimTimeAt
        {
            get
            {
                lock (resultLock)
                    return waiting && result == null && !timedOut
                        ? pendingEventTime + stopwatch.Elapsed.TotalSeconds : (double?)null;
            }
        }

        public int StepOrder => 10;

        ActiveRagdollController ragdoll;
        EventSensor sensor;
        BehaviourExecutor executor;
        readonly Stopwatch stopwatch = new Stopwatch();
        bool waiting, timedOut;
        double pendingEventTime;
        Decision result, lateAnswer;
        readonly object resultLock = new object();
        int requestId;
        int lastObjectId;
        bool recoverPending;

        void Awake()
        {
            ragdoll = GetComponent<ActiveRagdollController>();
            sensor = GetComponent<EventSensor>();
            executor = new BehaviourExecutor(ragdoll);
            if (Selector == null) Selector = MakeSelector(selectorName);
        }

        void OnEnable() { SimDriver.Register(this); GetComponent<EventSensor>().EventSensed += OnEvent; GetComponent<ActiveRagdollController>().BodyBuilt += ResetTrial; }
        void OnDisable() { SimDriver.Unregister(this); sensor.EventSensed -= OnEvent; ragdoll.BodyBuilt -= ResetTrial; }

        public static IBehaviourSelector MakeSelector(string name)
        {
            switch (name)
            {
                case "fixed": return new FixedSelector();
                case "rules": return new RulesSelector();
                case "laya": return new HttpModelSelector("laya");
                case "gliclass": return new HttpModelSelector("gliclass");
                case var n when n.StartsWith("fixed:"):
                    // Extra baselines: always the same behaviour, e.g. "fixed:fall_and_curl".
                    return new FixedSelector { Behaviour = n.Substring(6) };
                case var n when n.StartsWith("slow:"):
                    return new SlowTestSelector { DelayMs = int.Parse(n.Substring(5)) };
                default:
                    UnityEngine.Debug.LogWarning($"[ReactionManager] Unknown selector '{name}', using rules.");
                    return new RulesSelector();
            }
        }

        public void ResetTrial()
        {
            waiting = false; timedOut = false; result = null; recoverPending = false;
            requestId++; // late answers from a previous trial are dropped
            Phase = "idle"; LastEvent = null; LastEventText = null; LastDecision = null; LastNote = null;
            EventsHandled = 0; DecisionAppliedAt = -1; lastObjectId = 0;
            FirstEventText = null; FirstEvent = null; FirstDecision = null; FirstNote = null;
            lock (resultLock) result = null;
        }

        bool Seated => ragdoll.startPose == PoseLibrary.Sit.Name;

        void OnEvent(CharacterEvent ev)
        {
            if (waiting) return;
            if (ev.ObjectId != 0 && ev.ObjectId == lastObjectId) return; // impact of an object we already reacted to
            if (DecisionAppliedAt >= 0 && SimDriver.Time - DecisionAppliedAt < cooldown) return;

            // 1. Reflex, before anything is known.
            ragdoll.ApplyReflex(reflexStiffness, reflexDuration);

            // 2. Describe, log, ask.
            LastEvent = ev;
            LastEventText = EventSerializer.Serialize(ev);
            EventLog.Write("event", LastEventText);
            lastObjectId = ev.ObjectId;
            EventsHandled++;
            if (FirstEvent == null) { FirstEvent = ev; FirstEventText = LastEventText; }

            waiting = true; timedOut = false; recoverPending = false;
            lock (resultLock) result = null;
            pendingEventTime = SimDriver.Time;
            Phase = "waiting for " + Selector.Name;
            int id = ++requestId;
            stopwatch.Restart();
            // The callback may run on another thread (HTTP client, test selector): only store the result here.
            Selector.Request(ev, LastEventText, Behaviours.All, d =>
            {
                d.LatencyMs = (float)stopwatch.Elapsed.TotalMilliseconds;
                lock (resultLock)
                {
                    if (id != requestId) return; // answer for an old request
                    if (timedOut) { lateAnswer = d; return; }
                    if (d.Behaviour == null)
                    {
                        // The selector failed (server down, bad reply): rules answer now, flagged.
                        var fb = rules.Decide(LastEvent);
                        fb.Fallback = true; fb.Error = d.Error; fb.LatencyMs = d.LatencyMs;
                        d = fb;
                    }
                    result = d;
                }
            });
        }

        public void SimStep(float dt)
        {
            Decision late;
            lock (resultLock) { late = lateAnswer; lateAnswer = null; }
            if (late != null)
                EventLog.Write("late-answer", $"{late.Selector} answered {late.Behaviour} after {late.LatencyMs:F0} ms (ignored)");

            if (waiting)
            {
                double simWaitMs = (SimDriver.Time - pendingEventTime) * 1000.0;
                bool justTimedOut = false;
                Decision ready;
                lock (resultLock)
                {
                    if (result == null && !timedOut && stopwatch.Elapsed.TotalMilliseconds >= timeoutMs)
                    {
                        timedOut = true; justTimedOut = true;
                        result = rules.Decide(LastEvent);
                        result.TimedOut = true; result.Fallback = true; result.LatencyMs = timeoutMs;
                    }
                    ready = result;
                }
                if (justTimedOut)
                    EventLog.Write("timeout", $"{Selector.Name} gave no answer within {timeoutMs:F0} ms, using rules");
                // Usable in this step if it arrived before the step ends.
                if (ready != null && simWaitMs + dt * 1000.0 >= ready.LatencyMs) Apply(ready);
            }

            if (recoverPending && SimDriver.Time - DecisionAppliedAt >= recoverAfter)
            {
                recoverPending = false;
                var state = sensor.CurrentState();
                if (state == CharacterState.Lying)
                {
                    Phase = "down (no get-up behaviour)";
                }
                else
                {
                    ragdoll.SetAllStiffness(1f);
                    ragdoll.SetPose(PoseLibrary.ByName(ragdoll.startPose) ?? PoseLibrary.Stand, 0.6f);
                    Phase = "recovered";
                }
            }
        }

        void Apply(Decision d)
        {
            waiting = false;
            LastDecision = d;
            if (FirstDecision == null) FirstDecision = d;
            LastNote = executor.Execute(d, LastEvent, Seated);
            if (FirstNote == null) FirstNote = LastNote;
            DecisionAppliedAt = SimDriver.Time;
            recoverPending = d.Recover;
            Phase = "acting: " + d.Behaviour;
            if (d.Error != null) EventLog.Write("selector-error", d.Error);
            EventLog.Write("decision",
                $"{d.Selector}{(d.Fallback ? " (fallback)" : "")} -> {d.Behaviour} stiffness={d.Stiffness:F2} recover={d.Recover} " +
                $"latency={d.LatencyMs:F1}ms{(LastNote != "" ? " note: " + LastNote : "")}");
        }
    }
}
