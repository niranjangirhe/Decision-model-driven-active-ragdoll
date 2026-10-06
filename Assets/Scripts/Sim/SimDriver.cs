using System.Collections.Generic;
using UnityEngine;

namespace ActiveRagdoll
{
    /// <summary>Something that runs once per physics step, before the physics solve.</summary>
    public interface ISimStep
    {
        /// <summary>Lower runs first. Sensors 0, reaction layer 10, ragdoll controller 20.</summary>
        int StepOrder { get; }
        void SimStep(float dt);
    }

    /// <summary>
    /// One clock for the whole simulation. Every per-step component registers here and is
    /// stepped in a fixed order, then physics advances. Two modes:
    ///   * Play mode (Physics.simulationMode = FixedUpdate): the driver's FixedUpdate steps everything.
    ///   * Scripted / batch: <see cref="Advance"/> steps everything and calls Physics.Simulate itself,
    ///     faster than real time and without the editor window needing focus.
    /// <see cref="Time"/> is simulated time in seconds since the scene started (it also advances in
    /// scripted mode, where Unity's Time.fixedTime does not).
    /// </summary>
    [DefaultExecutionOrder(-200)]
    public class SimDriver : MonoBehaviour
    {
        public static double Time { get; private set; }

        static readonly List<ISimStep> steppers = new List<ISimStep>();
        static bool sorted;

        static SimDriver instance;

        public static void Register(ISimStep s)
        {
            if (!steppers.Contains(s)) { steppers.Add(s); sorted = false; }
            // Play mode needs a driver object for FixedUpdate; create one if the scene has none.
            if (instance == null && Application.isPlaying)
                instance = FindAnyObjectByType<SimDriver>() ?? new GameObject("SimDriver").AddComponent<SimDriver>();
        }

        public static void Unregister(ISimStep s) => steppers.Remove(s);

        void Awake() { instance = this; Time = 0; }

        void FixedUpdate()
        {
            if (Physics.simulationMode == SimulationMode.FixedUpdate)
                StepComponents(UnityEngine.Time.fixedDeltaTime);
        }

        static void StepComponents(float dt)
        {
            if (!sorted)
            {
                steppers.Sort((a, b) => a.StepOrder.CompareTo(b.StepOrder));
                sorted = true;
            }
            // Copy: a step may register or unregister components (e.g. a respawn).
            foreach (var s in steppers.ToArray())
            {
                if (s is Object o && o == null) continue;
                s.SimStep(dt);
            }
            Time += dt;
        }

        /// <summary>Scripted stepping: advance the whole simulation by <paramref name="seconds"/>.</summary>
        public static void Advance(float seconds)
        {
            float dt = UnityEngine.Time.fixedDeltaTime;
            int steps = Mathf.RoundToInt(seconds / dt);
            var previous = Physics.simulationMode;
            Physics.simulationMode = SimulationMode.Script;
            for (int i = 0; i < steps; i++)
            {
                StepComponents(dt);
                Physics.Simulate(dt);
            }
            Physics.simulationMode = previous;
        }

        /// <summary>Scripted stepping of a single physics step.</summary>
        public static void StepOnce() => Advance(UnityEngine.Time.fixedDeltaTime);
    }
}
