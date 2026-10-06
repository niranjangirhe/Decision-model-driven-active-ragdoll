using UnityEngine;

namespace ActiveRagdoll
{
    /// <summary>
    /// Added to every ragdoll link by <see cref="EventSensor"/>; forwards collisions to the sensor.
    /// ArticulationBody links receive OnCollisionEnter like rigidbodies do.
    /// </summary>
    public class LinkContactReporter : MonoBehaviour
    {
        public EventSensor sensor;

        void OnCollisionEnter(Collision c) { if (sensor != null) sensor.OnLinkCollision(name, c, true); }
        void OnCollisionStay(Collision c) { if (sensor != null) sensor.OnLinkCollision(name, c, false); }
    }
}
