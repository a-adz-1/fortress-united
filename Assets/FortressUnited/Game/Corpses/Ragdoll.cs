using System.Collections.Generic;
using UnityEngine;

namespace FortressUnited.Game
{
    /// <summary>The bones a ragdoll needs, from a Unity Humanoid rig or built by hand.</summary>
    public sealed class BodyBones
    {
        public Transform Hips, Spine, Head;
        public Transform LeftUpperArm, LeftLowerArm, LeftHand;
        public Transform RightUpperArm, RightLowerArm, RightHand;
        public Transform LeftUpperLeg, LeftLowerLeg, LeftFoot;
        public Transform RightUpperLeg, RightLowerLeg, RightFoot;

        /// <summary>Bones of a character imported with a Humanoid rig.</summary>
        public static BodyBones FromAnimator(Animator a)
        {
            Transform B(HumanBodyBones b) => a.GetBoneTransform(b);
            return new BodyBones
            {
                Hips = B(HumanBodyBones.Hips),
                Spine = B(HumanBodyBones.Chest) ?? B(HumanBodyBones.Spine),
                Head = B(HumanBodyBones.Head),
                LeftUpperArm = B(HumanBodyBones.LeftUpperArm), LeftLowerArm = B(HumanBodyBones.LeftLowerArm), LeftHand = B(HumanBodyBones.LeftHand),
                RightUpperArm = B(HumanBodyBones.RightUpperArm), RightLowerArm = B(HumanBodyBones.RightLowerArm), RightHand = B(HumanBodyBones.RightHand),
                LeftUpperLeg = B(HumanBodyBones.LeftUpperLeg), LeftLowerLeg = B(HumanBodyBones.LeftLowerLeg), LeftFoot = B(HumanBodyBones.LeftFoot),
                RightUpperLeg = B(HumanBodyBones.RightUpperLeg), RightLowerLeg = B(HumanBodyBones.RightLowerLeg), RightFoot = B(HumanBodyBones.RightFoot),
            };
        }
    }

    /// <summary>
    /// Turns a character into a ragdoll: a rigidbody, collider and joint per
    /// body part, with joint limits so knees and elbows bend the right way.
    /// The character must be standing upright when built (bind pose).
    /// </summary>
    public static class Ragdoll
    {
        public static List<Rigidbody> Build(BodyBones b, float totalMass = 80)
        {
            var bodies = new List<Rigidbody>();
            var root = b.Hips.root;
            Vector3 right = root.right, forward = root.forward, up = root.up;
            float m = totalMass / 78f;

            // Torso: hips and chest as boxes, head as a sphere
            float hipsToSpine = Vector3.Distance(b.Hips.position, b.Spine.position);
            float spineToHead = Vector3.Distance(b.Spine.position, b.Head.position);
            float shoulderWidth = Vector3.Distance(b.LeftUpperArm.position, b.RightUpperArm.position);
            float hipWidth = Vector3.Distance(b.LeftUpperLeg.position, b.RightUpperLeg.position);

            var hips = Part(b.Hips, 15 * m, bodies);
            Box(b.Hips, (b.Hips.position + b.Spine.position) / 2 - up * hipsToSpine * 0.25f,
                new Vector3(hipWidth * 1.7f, hipsToSpine * 1.5f, hipWidth * 1.1f), root);

            var spine = Part(b.Spine, 20 * m, bodies);
            Box(b.Spine, (b.Spine.position + b.Head.position) / 2 - up * spineToHead * 0.1f,
                new Vector3(shoulderWidth * 0.95f, spineToHead * 0.9f, shoulderWidth * 0.5f), root);
            Joint(b.Spine, hips, right, -25, 35, 20, 15);

            var head = Part(b.Head, 5 * m, bodies);
            var sphere = b.Head.gameObject.AddComponent<SphereCollider>();
            sphere.radius = shoulderWidth * 0.28f / Mathf.Max(0.0001f, b.Head.lossyScale.x);
            sphere.center = b.Head.InverseTransformPoint(b.Head.position + up * shoulderWidth * 0.15f);
            Joint(b.Head, spine, right, -40, 40, 30, 20);

            // Limbs: capsules along each bone, ball joints at shoulders and hips,
            // hinges at elbows and knees (positive angle about the character's
            // right moves the lower leg back; negative brings the forearm forward)
            Limb(b.LeftUpperArm, b.LeftLowerArm, 3 * m, 0.22f, spine, bodies, (b.LeftLowerArm.position - b.LeftUpperArm.position).normalized, -30, 30, 80, 60);
            Limb(b.LeftLowerArm, b.LeftHand, 2 * m, 0.18f, b.LeftUpperArm.GetComponent<Rigidbody>(), bodies, right, -130, 5, 10, 10, extend: 1.35f);
            Limb(b.RightUpperArm, b.RightLowerArm, 3 * m, 0.22f, spine, bodies, (b.RightLowerArm.position - b.RightUpperArm.position).normalized, -30, 30, 80, 60);
            Limb(b.RightLowerArm, b.RightHand, 2 * m, 0.18f, b.RightUpperArm.GetComponent<Rigidbody>(), bodies, right, -130, 5, 10, 10, extend: 1.35f);
            Limb(b.LeftUpperLeg, b.LeftLowerLeg, 9 * m, 0.24f, hips, bodies, (b.LeftLowerLeg.position - b.LeftUpperLeg.position).normalized, -20, 20, 70, 35);
            Limb(b.LeftLowerLeg, b.LeftFoot, 5 * m, 0.18f, b.LeftUpperLeg.GetComponent<Rigidbody>(), bodies, right, -5, 120, 8, 8, extend: 1.1f);
            Limb(b.RightUpperLeg, b.RightLowerLeg, 9 * m, 0.24f, hips, bodies, (b.RightLowerLeg.position - b.RightUpperLeg.position).normalized, -20, 20, 70, 35);
            Limb(b.RightLowerLeg, b.RightFoot, 5 * m, 0.18f, b.RightUpperLeg.GetComponent<Rigidbody>(), bodies, right, -5, 120, 8, 8, extend: 1.1f);

            foreach (var rb in bodies)
            {
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                rb.solverIterations = 12;
                rb.linearDamping = 0.05f;
                rb.angularDamping = 0.5f;
                rb.maxDepenetrationVelocity = 3;
            }
            // Parts of one body don't collide with each other
            var colliders = root.GetComponentsInChildren<Collider>();
            for (int i = 0; i < colliders.Length; i++)
                for (int j = i + 1; j < colliders.Length; j++)
                    Physics.IgnoreCollision(colliders[i], colliders[j]);
            return bodies;
        }

        static Rigidbody Part(Transform bone, float mass, List<Rigidbody> bodies)
        {
            var rb = bone.gameObject.AddComponent<Rigidbody>();
            rb.mass = mass;
            bodies.Add(rb);
            return rb;
        }

        static void Limb(Transform bone, Transform end, float mass, float radiusFraction, Rigidbody parent,
            List<Rigidbody> bodies, Vector3 twistAxis, float lowTwist, float highTwist, float swing1, float swing2, float extend = 1)
        {
            Part(bone, mass, bodies);
            Vector3 a = bone.position, b = end.position;
            float length = Vector3.Distance(a, b) * extend;
            Vector3 dir = (b - a).normalized;
            // The collider sits on a child lined up with the bone, so it works whatever the rig's axes
            var holder = new GameObject("Collider").transform;
            holder.SetParent(bone, false);
            holder.position = a + dir * length / 2;
            holder.rotation = Quaternion.FromToRotation(Vector3.up, dir);
            Vector3 s = holder.lossyScale;
            var cap = holder.gameObject.AddComponent<CapsuleCollider>();
            cap.direction = 1;
            float radius = Vector3.Distance(a, b) * radiusFraction;
            cap.radius = radius / Mathf.Max(0.0001f, s.x);
            cap.height = (length + radius) / Mathf.Max(0.0001f, s.y);
            Joint(bone, parent, twistAxis, lowTwist, highTwist, swing1, swing2);
        }

        static void Box(Transform bone, Vector3 centreWorld, Vector3 sizeWorld, Transform root)
        {
            var holder = new GameObject("Collider").transform;
            holder.SetParent(bone, false);
            holder.position = centreWorld;
            holder.rotation = root.rotation;
            var box = holder.gameObject.AddComponent<BoxCollider>();
            Vector3 s = holder.lossyScale;
            box.size = new Vector3(sizeWorld.x / s.x, sizeWorld.y / s.y, sizeWorld.z / s.z);
        }

        static void Joint(Transform bone, Rigidbody parent, Vector3 twistWorld, float lowTwist, float highTwist, float swing1, float swing2)
        {
            var j = bone.gameObject.AddComponent<CharacterJoint>();
            j.connectedBody = parent;
            j.enablePreprocessing = false;
            j.enableProjection = true;
            Vector3 axis = bone.InverseTransformDirection(twistWorld).normalized;
            Vector3 swing = Vector3.Cross(axis, Vector3.up);
            if (swing.sqrMagnitude < 0.01f) swing = Vector3.Cross(axis, Vector3.right);
            j.axis = axis;
            j.swingAxis = swing.normalized;
            j.lowTwistLimit = new SoftJointLimit { limit = lowTwist };
            j.highTwistLimit = new SoftJointLimit { limit = highTwist };
            j.swing1Limit = new SoftJointLimit { limit = swing1 };
            j.swing2Limit = new SoftJointLimit { limit = swing2 };
        }
    }
}
