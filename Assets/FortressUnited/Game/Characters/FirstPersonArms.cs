using UnityEngine;

namespace FortressUnited.Game
{
    /// <summary>
    /// A class's arms in first person (an arms-only model cut from the
    /// character by tools/meshy/arms_only.py), drawn on the view model layer.
    /// The skeleton sits with its head at the camera; each frame the right
    /// arm is bent (two-bone IK: shoulder and elbow) so the hand lands on the
    /// held weapon's grip, following it through swings. The left arm hangs
    /// out of view unless given a target.
    /// </summary>
    [DefaultExecutionOrder(1000)]   // after the weapon has moved for the frame
    public sealed class FirstPersonArms : MonoBehaviour
    {
        [Tooltip("Where the head bone sits relative to the camera (metres).")]
        public Vector3 HeadOffset = new Vector3(0, 0.06f, -0.07f);
        public Transform RightTarget, LeftTarget;

        Transform cam, head;
        Quaternion rootRest;    // the import's axis-conversion rotation on the model root
        Arm right, left;

        sealed class Arm
        {
            public Transform Upper, Lower, Hand;
            public Quaternion UpperRest, LowerRest, HandRest;
            public float LenA, LenB;
        }

        public static FirstPersonArms Create(GameObject armsPrefab, Camera camera)
        {
            var go = Instantiate(armsPrefab, camera.transform, false);
            go.name = "First Person Arms";
            var arms = go.AddComponent<FirstPersonArms>();
            arms.cam = camera.transform;
            arms.rootRest = go.transform.localRotation;
            foreach (var a in go.GetComponentsInChildren<Animator>())
                a.enabled = false;            // posed by hand, not animated
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                if (r is SkinnedMeshRenderer smr)
                    smr.updateWhenOffscreen = true;
            }
            arms.head = arms.Find("Head");
            arms.right = arms.MakeArm("Right");
            arms.left = arms.MakeArm("Left");
            ViewModelCamera.Hold(go, camera);
            return arms;
        }

        Transform Find(string name)
        {
            foreach (var t in GetComponentsInChildren<Transform>(true))
                if (t.name == name)
                    return t;
            return null;
        }

        Arm MakeArm(string side)
        {
            var a = new Arm { Upper = Find(side + "Arm"), Lower = Find(side + "ForeArm"), Hand = Find(side + "Hand") };
            if (a.Upper == null || a.Lower == null || a.Hand == null)
                return null;
            a.UpperRest = a.Upper.localRotation;
            a.LowerRest = a.Lower.localRotation;
            a.HandRest = a.Hand.localRotation;
            a.LenA = Vector3.Distance(a.Upper.position, a.Lower.position);
            a.LenB = Vector3.Distance(a.Lower.position, a.Hand.position);
            return a;
        }

        void LateUpdate()
        {
            if (cam == null || head == null)
                return;
            // Facing the way the camera looks, head at the camera
            transform.rotation = cam.rotation * rootRest;
            transform.position += cam.TransformPoint(HeadOffset) - head.position;

            Solve(right, RightTarget, 1);
            Solve(left, LeftTarget, -1);
        }

        /// <summary>Bends shoulder and elbow so the hand reaches the target, elbow down and out.</summary>
        void Solve(Arm a, Transform target, float side)
        {
            if (a == null)
                return;
            a.Upper.localRotation = a.UpperRest;
            a.Lower.localRotation = a.LowerRest;
            a.Hand.localRotation = a.HandRest;
            if (target == null)
                return;

            Vector3 shoulder = a.Upper.position;
            Vector3 goal = target.position;
            Vector3 toGoal = goal - shoulder;
            float d = Mathf.Clamp(toGoal.magnitude, Mathf.Abs(a.LenA - a.LenB) + 0.001f, a.LenA + a.LenB - 0.001f);
            Vector3 n = toGoal.normalized;
            // Elbow drops down and out to the side
            Vector3 pole = -cam.up * 0.8f + cam.right * side * 0.6f;
            Vector3 bend = Vector3.ProjectOnPlane(pole, n).normalized;
            float cosA = (a.LenA * a.LenA + d * d - a.LenB * a.LenB) / (2 * a.LenA * d);
            float sinA = Mathf.Sqrt(Mathf.Max(0, 1 - cosA * cosA));
            Vector3 elbowWant = shoulder + n * (a.LenA * cosA) + bend * (a.LenA * sinA);

            a.Upper.rotation = Quaternion.FromToRotation(a.Lower.position - shoulder, elbowWant - shoulder) * a.Upper.rotation;
            a.Lower.rotation = Quaternion.FromToRotation(a.Hand.position - a.Lower.position, shoulder + n * d - a.Lower.position) * a.Lower.rotation;
            // Hand turned to grip along the weapon
            a.Hand.rotation = Quaternion.FromToRotation(a.Hand.rotation * Vector3.up, target.forward) * a.Hand.rotation;
        }
    }
}
