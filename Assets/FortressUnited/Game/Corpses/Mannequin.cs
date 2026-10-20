using UnityEngine;

namespace FortressUnited.Game
{
    /// <summary>
    /// A stand-in player body built from primitives (1.8 m, A-pose), with
    /// the bones a ragdoll needs and a spot for a chest screen. For testing
    /// bodies before the rigged class models arrive. Robot effects show on
    /// it (RobotFx); with colliders it can be shot as a standing target.
    /// </summary>
    public static class Mannequin
    {
        public static GameObject Build(Vector3 feet, float yaw, Color team, out BodyBones bones, out Transform chestScreen, bool colliders = false)
        {
            var root = new GameObject("Body");
            root.transform.SetPositionAndRotation(feet, Quaternion.Euler(0, yaw, 0));

            var armour = Lit(new Color(0.85f, 0.86f, 0.88f));
            var suit = Lit(new Color(0.12f, 0.13f, 0.15f));
            var teamMat = Lit(team);
            teamMat.EnableKeyword("_EMISSION");
            teamMat.SetColor("_EmissionColor", TeamColours.Glow(team, 1.2f));

            Transform Bone(string name, Transform parent, Vector3 localPos)
            {
                var t = new GameObject(name).transform;
                t.SetParent(parent, false);
                t.localPosition = localPos;
                return t;
            }

            var b = new BodyBones();
            b.Hips = Bone("Hips", root.transform, new Vector3(0, 0.95f, 0));
            b.Spine = Bone("Spine", b.Hips, new Vector3(0, 0.15f, 0));
            b.Head = Bone("Head", b.Spine, new Vector3(0, 0.45f, 0));
            foreach (int side in new[] { -1, 1 })
            {
                var upperArm = Bone(side < 0 ? "LeftUpperArm" : "RightUpperArm", b.Spine, new Vector3(0.2f * side, 0.37f, 0));
                var lowerArm = Bone(side < 0 ? "LeftLowerArm" : "RightLowerArm", upperArm, new Vector3(0.1f * side, -0.27f, 0));
                var hand = Bone(side < 0 ? "LeftHand" : "RightHand", lowerArm, new Vector3(0.05f * side, -0.25f, 0.02f));
                var upperLeg = Bone(side < 0 ? "LeftUpperLeg" : "RightUpperLeg", b.Hips, new Vector3(0.1f * side, -0.05f, 0));
                var lowerLeg = Bone(side < 0 ? "LeftLowerLeg" : "RightLowerLeg", upperLeg, new Vector3(0.01f * side, -0.42f, 0));
                var foot = Bone(side < 0 ? "LeftFoot" : "RightFoot", lowerLeg, new Vector3(0, -0.4f, 0));
                if (side < 0) { b.LeftUpperArm = upperArm; b.LeftLowerArm = lowerArm; b.LeftHand = hand; b.LeftUpperLeg = upperLeg; b.LeftLowerLeg = lowerLeg; b.LeftFoot = foot; }
                else { b.RightUpperArm = upperArm; b.RightLowerArm = lowerArm; b.RightHand = hand; b.RightUpperLeg = upperLeg; b.RightLowerLeg = lowerLeg; b.RightFoot = foot; }

                Segment(upperArm, lowerArm, 0.055f, armour);
                Segment(lowerArm, hand, 0.045f, suit);
                Shape(PrimitiveType.Sphere, hand, Vector3.zero, Quaternion.identity, Vector3.one * 0.09f, suit);
                Segment(upperLeg, lowerLeg, 0.08f, armour);
                Segment(lowerLeg, foot, 0.06f, armour);
                Shape(PrimitiveType.Cube, foot, new Vector3(0, -0.03f, 0.05f), Quaternion.identity, new Vector3(0.1f, 0.08f, 0.24f), suit);
            }

            // Torso, pelvis and head
            Shape(PrimitiveType.Cube, b.Hips, new Vector3(0, 0.02f, 0), Quaternion.identity, new Vector3(0.32f, 0.2f, 0.2f), suit);
            Shape(PrimitiveType.Cube, b.Spine, new Vector3(0, 0.2f, 0), Quaternion.identity, new Vector3(0.38f, 0.38f, 0.24f), armour);
            Shape(PrimitiveType.Sphere, b.Head, new Vector3(0, 0.1f, 0), Quaternion.identity, new Vector3(0.22f, 0.25f, 0.24f), armour);
            // Visor and shoulder stripes in team colour
            Shape(PrimitiveType.Cube, b.Head, new Vector3(0, 0.12f, 0.1f), Quaternion.identity, new Vector3(0.17f, 0.06f, 0.06f), teamMat);
            Shape(PrimitiveType.Cube, b.Spine, new Vector3(0, 0.36f, 0), Quaternion.identity, new Vector3(0.4f, 0.03f, 0.25f), teamMat);

            // Chest screen spot: on the front of the chest, facing forward
            chestScreen = Bone("ChestScreen", b.Spine, new Vector3(0, 0.2f, 0.122f));
            chestScreen.localRotation = Quaternion.Euler(0, 180, 0);   // a quad faces -Z
            chestScreen.localScale = new Vector3(0.2f, 0.08f, 1);
            Shape(PrimitiveType.Cube, b.Spine, new Vector3(0, 0.2f, 0.118f), Quaternion.identity, new Vector3(0.22f, 0.1f, 0.01f), suit);

            if (!colliders)
                foreach (var c in root.GetComponentsInChildren<Collider>())
                    Object.DestroyImmediate(c);
            var core = Bone("Core", b.Spine, new Vector3(0, 0.2f, 0.05f));
            RobotFx.Attach(root, team, core);
            bones = b;
            return root;
        }

        static void Segment(Transform from, Transform to, float radius, Material mat)
        {
            Vector3 d = to.position - from.position;
            var go = Shape(PrimitiveType.Capsule, from, Vector3.zero, Quaternion.identity, Vector3.one, mat);
            go.transform.position = from.position + d / 2;
            go.transform.rotation = Quaternion.FromToRotation(Vector3.up, d);
            go.transform.localScale = new Vector3(radius * 2, d.magnitude / 2 + radius * 0.5f, radius * 2);
        }

        static GameObject Shape(PrimitiveType type, Transform parent, Vector3 pos, Quaternion rot, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = rot;
            go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }

        static Material Lit(Color c)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            m.color = c;
            m.SetFloat("_Smoothness", 0.45f);
            return m;
        }
    }
}
