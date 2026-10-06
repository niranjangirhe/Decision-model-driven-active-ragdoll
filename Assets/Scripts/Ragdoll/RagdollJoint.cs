using UnityEngine;

namespace ActiveRagdoll
{
    /// <summary>Body groups whose stiffness a behaviour can scale independently.</summary>
    public enum BodyGroup { Legs, Torso, Arms, Neck }

    /// <summary>
    /// One driven joint of the ragdoll: the ArticulationBody of the child link plus
    /// the PD gains and limits we use for it.
    ///
    /// Angle convention: every link is built with identity world rotation and the
    /// joint anchor is left at identity, so in the rest pose the joint axes line up
    /// with the world: X = right (pitch), Y = up (yaw), Z = forward (roll).
    /// Angles are in degrees, in the raw joint frame (not mirrored for the left side).
    /// </summary>
    public class RagdollJoint
    {
        public string Name;
        public BodyGroup Group;
        public ArticulationBody Body;

        // Per-axis limits in degrees. An axis with min == max is locked.
        public Vector2 LimitX, LimitY, LimitZ;

        // Base PD gains at stiffness multiplier 1.
        public float Stiffness;    // N*m per rad
        public float Damping;      // N*m*s per rad
        public float ForceLimit;   // N*m

        public bool IsRevolute => Body.jointType == ArticulationJointType.RevoluteJoint;

        public Vector3 Clamp(Vector3 anglesDeg)
        {
            return new Vector3(
                Mathf.Clamp(anglesDeg.x, LimitX.x, LimitX.y),
                Mathf.Clamp(anglesDeg.y, LimitY.x, LimitY.y),
                Mathf.Clamp(anglesDeg.z, LimitZ.x, LimitZ.y));
        }

        /// <summary>Writes target angles and gains into the joint drives (the PD controller).</summary>
        public void ApplyDrive(Vector3 targetDeg, float stiffnessMultiplier)
        {
            Vector3 t = Clamp(targetDeg);
            float k = Stiffness * stiffnessMultiplier;
            // Damping scales with sqrt(k) so the damping ratio stays constant
            // when a behaviour makes the body stiffer or softer.
            float d = Damping * Mathf.Sqrt(Mathf.Max(stiffnessMultiplier, 0f));

            Body.xDrive = MakeDrive(Body.xDrive, t.x, k, d);
            if (!IsRevolute)
            {
                Body.yDrive = MakeDrive(Body.yDrive, t.y, k, d);
                Body.zDrive = MakeDrive(Body.zDrive, t.z, k, d);
            }
        }

        ArticulationDrive MakeDrive(ArticulationDrive drive, float target, float k, float d)
        {
            drive.driveType = ArticulationDriveType.Force;
            drive.target = target;
            drive.targetVelocity = 0f;
            drive.stiffness = k;
            drive.damping = d;
            drive.forceLimit = ForceLimit;
            return drive;
        }

        /// <summary>Current joint angles in degrees, read from the reduced coordinates.</summary>
        public Vector3 CurrentAnglesDeg()
        {
            var p = Body.jointPosition;
            var a = new float[3];
            int i = 0;
            if (Body.twistLock != ArticulationDofLock.LockedMotion) a[0] = p[i++];
            if (!IsRevolute)
            {
                if (Body.swingYLock != ArticulationDofLock.LockedMotion) a[1] = p[i++];
                if (Body.swingZLock != ArticulationDofLock.LockedMotion) a[2] = p[i++];
            }
            return new Vector3(a[0], a[1], a[2]) * Mathf.Rad2Deg;
        }

        /// <summary>Teleports the joint to the given angles with zero velocity (used to spawn in a pose).</summary>
        public void SnapTo(Vector3 anglesDeg)
        {
            Vector3 r = Clamp(anglesDeg) * Mathf.Deg2Rad;
            var values = new System.Collections.Generic.List<float>();
            if (Body.twistLock != ArticulationDofLock.LockedMotion) values.Add(r.x);
            if (!IsRevolute)
            {
                if (Body.swingYLock != ArticulationDofLock.LockedMotion) values.Add(r.y);
                if (Body.swingZLock != ArticulationDofLock.LockedMotion) values.Add(r.z);
            }
            Body.jointPosition = ToReducedSpace(values);
            Body.jointVelocity = ToReducedSpace(new System.Collections.Generic.List<float>(new float[values.Count]));
        }

        static ArticulationReducedSpace ToReducedSpace(System.Collections.Generic.List<float> v)
        {
            switch (v.Count)
            {
                case 1: return new ArticulationReducedSpace(v[0]);
                case 2: return new ArticulationReducedSpace(v[0], v[1]);
                default: return new ArticulationReducedSpace(v[0], v[1], v[2]);
            }
        }
    }
}
