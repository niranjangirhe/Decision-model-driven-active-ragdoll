using System.Collections.Generic;
using UnityEngine;

namespace ActiveRagdoll
{
    /// <summary>
    /// Builds a 15-link humanoid from primitives as one ArticulationBody tree rooted at the pelvis.
    ///
    /// Dimensions give a body about 1.75 m tall. Masses follow the de Leva (1996) segment
    /// fractions for a 70 kg adult (forearm and hand are merged into one link).
    /// Every link's transform sits at its joint (the pivot), and the collider is offset along the limb.
    /// The pelvis root is free-floating: nothing pins the character to the world.
    /// </summary>
    public static class RagdollBuilder
    {
        public const float TotalMass = 70f;

        public class Result
        {
            public ArticulationBody Pelvis;
            public readonly Dictionary<string, ArticulationBody> Links = new Dictionary<string, ArticulationBody>();
            public readonly List<RagdollJoint> Joints = new List<RagdollJoint>();
            public readonly List<Collider> Colliders = new List<Collider>();
        }

        // Base PD gains per group at stiffness multiplier 1: (stiffness N*m/rad, damping N*m*s/rad, torque limit N*m).
        static readonly Dictionary<BodyGroup, Vector3> Gains = new Dictionary<BodyGroup, Vector3>
        {
            { BodyGroup.Legs,  new Vector3(1500f, 60f, 450f) },
            { BodyGroup.Torso, new Vector3(1200f, 50f, 350f) },
            { BodyGroup.Arms,  new Vector3(250f,  10f, 120f) },
            { BodyGroup.Neck,  new Vector3(150f,   6f,  60f) },
        };

        public static Result Build(Transform owner, PhysicsMaterial bodyMaterial, PhysicsMaterial footMaterial, Color color)
        {
            var r = new Result();
            Material mat = MakeMaterial(color);

            // ---- Trunk and head ----
            var pelvis = MakeLink(r, "Pelvis", owner, new Vector3(0f, 1.00f, 0f), 7.82f, mat);
            AddBox(r, pelvis, Vector3.zero, new Vector3(0.30f, 0.18f, 0.18f), bodyMaterial, mat);
            r.Pelvis = pelvis;

            var abdomen = MakeLink(r, "Abdomen", pelvis.transform, new Vector3(0f, 1.09f, 0f), 11.43f, mat);
            AddBox(r, abdomen, new Vector3(0f, 0.105f, 0f), new Vector3(0.27f, 0.21f, 0.17f), bodyMaterial, mat);
            AddSpherical(r, abdomen, "SpineLower", BodyGroup.Torso, new Vector2(-30, 45), new Vector2(-30, 30), new Vector2(-25, 25));

            var chest = MakeLink(r, "Chest", abdomen.transform, new Vector3(0f, 1.30f, 0f), 11.17f, mat);
            AddBox(r, chest, new Vector3(0f, 0.11f, 0f), new Vector3(0.34f, 0.22f, 0.20f), bodyMaterial, mat);
            AddSpherical(r, chest, "SpineUpper", BodyGroup.Torso, new Vector2(-25, 35), new Vector2(-25, 25), new Vector2(-20, 20));

            var head = MakeLink(r, "Head", chest.transform, new Vector3(0f, 1.52f, 0f), 4.86f, mat);
            AddSphere(r, head, new Vector3(0f, 0.12f, 0f), 0.105f, bodyMaterial, mat);
            AddSpherical(r, head, "Neck", BodyGroup.Neck, new Vector2(-40, 50), new Vector2(-70, 70), new Vector2(-35, 35));

            // ---- Limbs, right side (+1) then left side (-1) ----
            foreach (int side in new[] { 1, -1 })
            {
                string s = side > 0 ? "_R" : "_L";

                var upperArm = MakeLink(r, "UpperArm" + s, chest.transform, new Vector3(0.22f * side, 1.47f, 0f), 1.90f, mat);
                AddCapsule(r, upperArm, new Vector3(0f, -0.145f, 0f), 0.31f, 0.045f, bodyMaterial, mat);
                AddSpherical(r, upperArm, "Shoulder" + s, BodyGroup.Arms, new Vector2(-170, 60), Mirror(new Vector2(-80, 80), side), Mirror(new Vector2(-30, 170), side));

                var forearm = MakeLink(r, "Forearm" + s, upperArm.transform, new Vector3(0.22f * side, 1.18f, 0f), 1.56f, mat);
                AddCapsule(r, forearm, new Vector3(0f, -0.17f, 0f), 0.36f, 0.04f, bodyMaterial, mat);
                AddRevolute(r, forearm, "Elbow" + s, BodyGroup.Arms, new Vector2(-145, 0));

                var thigh = MakeLink(r, "Thigh" + s, pelvis.transform, new Vector3(0.09f * side, 0.94f, 0f), 9.91f, mat);
                AddCapsule(r, thigh, new Vector3(0f, -0.215f, 0f), 0.45f, 0.07f, bodyMaterial, mat);
                AddSpherical(r, thigh, "Hip" + s, BodyGroup.Legs, new Vector2(-120, 30), Mirror(new Vector2(-40, 40), side), Mirror(new Vector2(-20, 45), side));

                var shin = MakeLink(r, "Shin" + s, thigh.transform, new Vector3(0.09f * side, 0.51f, 0f), 3.03f, mat);
                AddCapsule(r, shin, new Vector3(0f, -0.215f, 0f), 0.43f, 0.05f, bodyMaterial, mat);
                AddRevolute(r, shin, "Knee" + s, BodyGroup.Legs, new Vector2(-2, 140));

                var foot = MakeLink(r, "Foot" + s, shin.transform, new Vector3(0.09f * side, 0.08f, 0f), 0.96f, mat);
                AddBox(r, foot, new Vector3(0f, -0.045f, 0.05f), new Vector3(0.10f, 0.07f, 0.25f), footMaterial, mat);
                // Ankle: pitch and roll only, yaw locked.
                AddSpherical(r, foot, "Ankle" + s, BodyGroup.Legs, new Vector2(-30, 45), Vector2.zero, Mirror(new Vector2(-20, 20), side));
            }

            IgnoreTouchingPairs(r);
            return r;
        }

        /// <summary>Mirror a Y or Z limit range for the left side (rotation sense flips).</summary>
        static Vector2 Mirror(Vector2 range, int side) => side > 0 ? range : new Vector2(-range.y, -range.x);

        static ArticulationBody MakeLink(Result r, string name, Transform parent, Vector3 worldPivot, float mass, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            // Pivot positions are given for a character standing at the owner's origin.
            Transform owner = parent;
            while (owner.GetComponent<ArticulationBody>() != null) owner = owner.parent;
            go.transform.position = owner.TransformPoint(worldPivot);
            go.transform.rotation = owner.rotation;

            var ab = go.AddComponent<ArticulationBody>();
            ab.mass = mass;
            ab.linearDamping = 0.05f;
            ab.angularDamping = 0.05f;
            ab.jointFriction = 0.05f;
            r.Links[name] = ab;
            return ab;
        }

        static void AddSpherical(Result r, ArticulationBody ab, string jointName, BodyGroup group, Vector2 limX, Vector2 limY, Vector2 limZ)
        {
            ab.jointType = ArticulationJointType.SphericalJoint;
            ab.anchorPosition = Vector3.zero;
            ab.anchorRotation = Quaternion.identity;
            ab.matchAnchors = true;
            ab.twistLock = LockFor(limX);
            ab.swingYLock = LockFor(limY);
            ab.swingZLock = LockFor(limZ);
            ab.xDrive = WithLimits(ab.xDrive, limX);
            ab.yDrive = WithLimits(ab.yDrive, limY);
            ab.zDrive = WithLimits(ab.zDrive, limZ);
            AddJoint(r, ab, jointName, group, limX, limY, limZ);
        }

        static void AddRevolute(Result r, ArticulationBody ab, string jointName, BodyGroup group, Vector2 limX)
        {
            ab.jointType = ArticulationJointType.RevoluteJoint;
            ab.anchorPosition = Vector3.zero;
            ab.anchorRotation = Quaternion.identity;
            ab.matchAnchors = true;
            ab.twistLock = ArticulationDofLock.LimitedMotion;
            ab.xDrive = WithLimits(ab.xDrive, limX);
            AddJoint(r, ab, jointName, group, limX, Vector2.zero, Vector2.zero);
        }

        static void AddJoint(Result r, ArticulationBody ab, string name, BodyGroup group, Vector2 limX, Vector2 limY, Vector2 limZ)
        {
            Vector3 g = Gains[group];
            r.Joints.Add(new RagdollJoint
            {
                Name = name, Group = group, Body = ab,
                LimitX = limX, LimitY = limY, LimitZ = limZ,
                Stiffness = g.x, Damping = g.y, ForceLimit = g.z,
            });
        }

        static ArticulationDofLock LockFor(Vector2 range) =>
            Mathf.Approximately(range.x, range.y) ? ArticulationDofLock.LockedMotion : ArticulationDofLock.LimitedMotion;

        static ArticulationDrive WithLimits(ArticulationDrive d, Vector2 range)
        {
            d.lowerLimit = range.x;
            d.upperLimit = range.y;
            return d;
        }

        // ---- Colliders with matching visuals ----

        static void AddBox(Result r, ArticulationBody ab, Vector3 center, Vector3 size, PhysicsMaterial pm, Material mat)
        {
            var c = ab.gameObject.AddComponent<BoxCollider>();
            c.center = center; c.size = size; c.sharedMaterial = pm;
            r.Colliders.Add(c);
            AddVisual(ab.transform, PrimitiveType.Cube, center, size, mat);
        }

        static void AddSphere(Result r, ArticulationBody ab, Vector3 center, float radius, PhysicsMaterial pm, Material mat)
        {
            var c = ab.gameObject.AddComponent<SphereCollider>();
            c.center = center; c.radius = radius; c.sharedMaterial = pm;
            r.Colliders.Add(c);
            AddVisual(ab.transform, PrimitiveType.Sphere, center, Vector3.one * radius * 2f, mat);
        }

        static void AddCapsule(Result r, ArticulationBody ab, Vector3 center, float height, float radius, PhysicsMaterial pm, Material mat)
        {
            var c = ab.gameObject.AddComponent<CapsuleCollider>();
            c.center = center; c.height = height; c.radius = radius; c.direction = 1; c.sharedMaterial = pm;
            r.Colliders.Add(c);
            // Unity's capsule primitive is 2 m tall with radius 0.5.
            AddVisual(ab.transform, PrimitiveType.Capsule, center, new Vector3(radius * 2f, height * 0.5f, radius * 2f), mat);
        }

        static void AddVisual(Transform parent, PrimitiveType type, Vector3 center, Vector3 scale, Material mat)
        {
            var v = GameObject.CreatePrimitive(type);
            Object.DestroyImmediate(v.GetComponent<Collider>());
            v.name = "Visual";
            v.transform.SetParent(parent, false);
            v.transform.localPosition = center;
            v.transform.localScale = scale;
            v.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }

        static Material MakeMaterial(Color color)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var m = new Material(shader) { color = color };
            return m;
        }

        /// <summary>
        /// Disable collisions between links that touch in the rest pose (parent/child and neighbours
        /// such as pelvis/thigh). All other self-collisions stay on, so arms can still hit the torso.
        /// </summary>
        static void IgnoreTouchingPairs(Result r)
        {
            Physics.SyncTransforms();
            for (int i = 0; i < r.Colliders.Count; i++)
            for (int j = i + 1; j < r.Colliders.Count; j++)
            {
                Collider a = r.Colliders[i], b = r.Colliders[j];
                bool adjacent = a.transform.parent == b.transform || b.transform.parent == a.transform;
                Bounds ba = a.bounds; ba.Expand(0.02f);
                if (adjacent || ba.Intersects(b.bounds))
                    Physics.IgnoreCollision(a, b, true);
            }
        }
    }
}
