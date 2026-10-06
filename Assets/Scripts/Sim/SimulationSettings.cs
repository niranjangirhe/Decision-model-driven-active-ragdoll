using UnityEngine;

namespace ActiveRagdoll
{
    /// <summary>
    /// Physics settings used by every scene, applied at startup so the values are visible
    /// in one place (they are also written into ProjectSettings by the editor menu
    /// "Ragdoll/Apply Physics Project Settings"). Final values are recorded in the README.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class SimulationSettings : MonoBehaviour
    {
        public const float FixedTimestep = 0.01f;
        public const int SolverIterations = 16;
        public const int SolverVelocityIterations = 4;

        void Awake() => Apply();

        public static void Apply()
        {
            Time.fixedDeltaTime = FixedTimestep;
            Time.maximumDeltaTime = 0.1f;
            Physics.defaultSolverIterations = SolverIterations;
            Physics.defaultSolverVelocityIterations = SolverVelocityIterations;
        }
    }
}
