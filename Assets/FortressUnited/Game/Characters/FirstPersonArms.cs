using UnityEngine;

namespace FortressUnited.Game
{
    /// <summary>
    /// A class's arms in first person (an arms-only model cut from the
    /// character by tools/meshy/arms_only.py), drawn on the view model layer.
    /// The skeleton sits with its head at the camera; each frame the right
    /// arm is bent (two-bone IK: shoulder and elbow) and the hand turned so
    /// the fist's grip point (the <Side>Grip marker from arms_only.py) sits on
    /// the weapon's grip with the handle running through the closed hand,
    /// following it through swings. The left arm hangs
    /// out of view unless given a target.
    /// </summary>
    [DefaultExecutionOrder(1000)]   // after the weapon has moved for the frame
    public sealed class FirstPersonArms : MonoBehaviour
    {
        [Tooltip("Where the head bone sits relative to the camera (metres).")]
        public Vector3 HeadOffset = new Vector3(0, -0.1f, -0.16f);
        public Transform RightTarget, LeftTarget;
        [Tooltip("Where the elbows should point (optional; otherwise down and out).")]
        public Transform RightElbow, LeftElbow;
        [Tooltip("The fists are a little smaller than the full-size character's.")]
        public float HandScale = 0.85f;
        [Tooltip("How far the hand may turn from the forearm's line to follow a handle (degrees).")]
        public float MaxHandTurn = 50;

        Transform cam, head;
        Quaternion rootRest;    // the import's axis-conversion rotation on the model root
        Arm right, left;

        sealed class Arm
        {
            public Transform Upper, Lower, Hand, Grip;
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
            foreach (var a in new[] { arms.right, arms.left })
                if (a != null) a.Hand.localScale *= arms.HandScale;
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
            var a = new Arm { Upper = Find(side + "Arm"), Lower = Find(side + "ForeArm"), Hand = Find(side + "Hand"), Grip = Find(side + "Grip") };
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

            Solve(right, RightTarget, RightElbow, 1);
            Solve(left, LeftTarget, LeftElbow, -1);
        }

        /// <summary>
        /// Puts the fist on the target: the hand is turned so the grip marker's
        /// axis runs along the target's forward (the handle), and shoulder and
        /// elbow bend (elbow down and out) so the grip marker lands on the target.
        /// </summary>
        void Solve(Arm a, Transform target, Transform elbowHint, float side)
        {
            if (a == null)
                return;
            a.Upper.localRotation = a.UpperRest;
            a.Lower.localRotation = a.LowerRest;
            a.Hand.localRotation = a.HandRest;
            if (target == null)
                return;

            // Two passes: the hand's turn moves where the wrist must go, and the IK moves the hand
            for (int pass = 0; pass < 3; pass++)
            {
                Quaternion handRot = a.Hand.rotation;
                if (a.Grip != null)
                {
                    // Turn the hand towards the handle's direction, but at most 50
                    // degrees from the forearm's own line, so the wrist never twists
                    // into a knot (past that the fist just holds the handle at an angle)
                    var want = Quaternion.FromToRotation(a.Grip.forward, target.forward) * handRot;
                    handRot = Quaternion.RotateTowards(handRot, want, MaxHandTurn);
                }
                // Where the wrist must be for the grip marker to land on the target
                Vector3 wristGoal = target.position;
                if (a.Grip != null)
                {
                    Vector3 local = Quaternion.Inverse(a.Hand.rotation) * (a.Grip.position - a.Hand.position);
                    wristGoal = target.position - handRot * local;
                }
                Reach(a, wristGoal, elbowHint, side);
                a.Hand.rotation = handRot;
            }
        }

        /// <summary>Two-bone IK: the wrist to a point, the elbow bending down and out.</summary>
        void Reach(Arm a, Vector3 goal, Transform elbowHint, float side)
        {
            Vector3 shoulder = a.Upper.position;
            Vector3 toGoal = goal - shoulder;
            float d = Mathf.Clamp(toGoal.magnitude, Mathf.Abs(a.LenA - a.LenB) + 0.001f, a.LenA + a.LenB - 0.001f);
            Vector3 n = toGoal.normalized;
            Vector3 pole = elbowHint != null
                ? elbowHint.position - (shoulder + goal) * 0.5f
                : -cam.up * 0.8f + cam.right * side * 0.6f;
            Vector3 bend = Vector3.ProjectOnPlane(pole, n).normalized;
            float cosA = (a.LenA * a.LenA + d * d - a.LenB * a.LenB) / (2 * a.LenA * d);
            float sinA = Mathf.Sqrt(Mathf.Max(0, 1 - cosA * cosA));
            Vector3 elbowWant = shoulder + n * (a.LenA * cosA) + bend * (a.LenA * sinA);
            Quaternion keepHand = a.Hand.rotation;
            a.Upper.rotation = Quaternion.FromToRotation(a.Lower.position - shoulder, elbowWant - shoulder) * a.Upper.rotation;
            a.Lower.rotation = Quaternion.FromToRotation(a.Hand.position - a.Lower.position, shoulder + n * d - a.Lower.position) * a.Lower.rotation;
            a.Hand.rotation = keepHand;
        }
    }
}
