using UnityEngine;

namespace FortressUnited.Game
{
    /// <summary>
    /// The medikit held in first person: places the model in front of the
    /// camera with its screen facing the player, puts the live vitals
    /// display on its Screen_Front and Screen_Back surfaces, and sways it
    /// with movement.
    /// </summary>
    public sealed class MedkitViewModel : MonoBehaviour
    {
        [Tooltip("Where the screen centre sits, in camera space (metres).")]
        public Vector3 ScreenOffset = new Vector3(0.13f, -0.15f, 0.45f);
        [Tooltip("Tilt of the device towards the player (degrees).")]
        public float Tilt = 18;
        public float Scale = 0.45f;
        public float BobAmount = 0.008f;

        public PlayerController Player;
        public VitalsDisplay Display;
        /// <summary>TFC's medkit hold and "use" jab (measured from v_tfc_medkit), frame by frame.</summary>
        public ViewModelMotion Motion = ViewModelMotions.Medkit;
        /// <summary>The right handle, for the arms to hold; and where the elbow should be.</summary>
        public Transform Grip { get; private set; }
        public Transform ElbowHint { get; private set; }
        /// <summary>A jab connected (heal a teammate, infect an enemy).</summary>
        public System.Action<RaycastHit> OnStrike;
        public float Reach = 1.3f;
        [Tooltip("Lift on top of TFC's hold so the kit's screen shows (TFC's kit is smaller and screenless).")]
        public Vector3 HoldShift = new Vector3(-0.02f, 0.075f, 0.05f);

        Transform carry;
        Vector3 restHand, restDir;
        float idleTime, swingT = -1;
        bool swingHit;

        Transform model;
        Vector3 basePosition;
        Quaternion baseRotation;
        float bobPhase;
        Vector3 swayVelocity, sway;

        public static MedkitViewModel Create(GameObject prefab, Camera cam, PlayerController player, VitalsDisplay display)
        {
            var holder = new GameObject("Medkit (view model)");
            holder.transform.SetParent(cam.transform, false);
            var vm = holder.AddComponent<MedkitViewModel>();
            vm.Player = player;
            vm.Display = display;
            vm.model = Instantiate(prefab, holder.transform).transform;
            vm.Setup();
            ViewModelCamera.Hold(holder, cam);
            return vm;
        }

        void Setup()
        {
            model.localScale = Vector3.one * Scale;
            Transform front = FindDeep(model, "Screen_Front");
            Transform back = FindDeep(model, "Screen_Back");

            foreach (var t in new[] { front, back })
            {
                if (t == null) continue;
                var r = t.GetComponent<MeshRenderer>();
                if (r != null && Display != null)
                    r.sharedMaterial = Display.ScreenMaterial;
            }

            // Turn the model so the front screen faces the camera, upright
            Quaternion orient = Quaternion.identity;
            Vector3 centre = Vector3.zero;
            if (front != null)
            {
                var mesh = front.GetComponent<MeshFilter>().sharedMesh;
                // Corners by UV, since the importer may reorder vertices
                Vector3 bl = model.InverseTransformPoint(front.TransformPoint(Corner(mesh, 0, 0)));
                Vector3 br = model.InverseTransformPoint(front.TransformPoint(Corner(mesh, 1, 0)));
                Vector3 tl = model.InverseTransformPoint(front.TransformPoint(Corner(mesh, 0, 1)));
                Vector3 right = (br - bl).normalized;
                Vector3 up = (tl - bl).normalized;
                Vector3 into = Vector3.Cross(right, up); // away from the viewer
                orient = Quaternion.Inverse(Quaternion.LookRotation(into, up));
                centre = bl + (br - bl) * 0.5f + (tl - bl) * 0.5f;
            }

            baseRotation = Quaternion.Euler(-Tilt, 0, 0) * orient;
            model.localRotation = baseRotation;
            // Put the screen centre at ScreenOffset
            Vector3 centreInHolder = baseRotation * Vector3.Scale(centre, model.localScale);
            basePosition = ScreenOffset - centreInHolder;
            model.localPosition = basePosition;

            // Carry pivot at TFC's resting hand, with the kit's right handle on it
            carry = new GameObject("Carry").transform;
            carry.SetParent(transform, false);
            Motion.Pose(0, -1, out restHand, out restDir, out _);
            // Bounds in the holder's own axes (x right, y up, z forward from the camera)
            var b = new Bounds(); bool first = true;
            foreach (var mf in model.GetComponentsInChildren<MeshFilter>())
            {
                var lb = mf.sharedMesh.bounds;
                for (int c = 0; c < 8; c++)
                {
                    var corner = lb.center + Vector3.Scale(lb.extents, new Vector3((c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1, (c & 4) == 0 ? -1 : 1));
                    var p = transform.InverseTransformPoint(mf.transform.TransformPoint(corner));
                    if (first) { b = new Bounds(p, Vector3.zero); first = false; } else b.Encapsulate(p);
                }
            }
            Vector3 handle = new Vector3(b.max.x - b.size.x * 0.06f, b.center.y, b.center.z);
            basePosition += restHand - handle;
            model.localPosition = basePosition;
            carry.localPosition = restHand;
            model.SetParent(carry, true);
            basePosition = model.localPosition;
            Grip = new GameObject("Grip").transform;
            Grip.SetParent(carry, false);
            Grip.localRotation = Quaternion.LookRotation(Vector3.up, Vector3.back);   // the handle runs up and down
            ElbowHint = new GameObject("Elbow Hint").transform;
            ElbowHint.SetParent(transform, false);

            // The view model shouldn't cast shadows on the world
            foreach (var r in model.GetComponentsInChildren<Renderer>())
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        static Vector3 Corner(Mesh mesh, float u, float v)
        {
            var verts = mesh.vertices;
            var uvs = mesh.uv;
            int best = 0;
            float bestD = float.MaxValue;
            for (int i = 0; i < verts.Length && i < uvs.Length; i++)
            {
                float d = (uvs[i] - new Vector2(u, v)).sqrMagnitude;
                if (d < bestD) { bestD = d; best = i; }
            }
            return verts[best];
        }

        static Transform FindDeep(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name)
                    return t;
            return null;
        }

        /// <summary>The use jab (left click does this; scripts can too).</summary>
        public void Swing()
        {
            if (swingT >= 0)
                return;
            swingT = 0;
            swingHit = false;
        }

        void Update()
        {
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame && Cursor.lockState == CursorLockMode.Locked)
                Swing();
        }

        /// <summary>Moves the kit along TFC's hold / jab path; the hit lands where it reaches the patient.</summary>
        void FollowMotion()
        {
            idleTime += Time.deltaTime;
            float swingTime = -1;
            if (swingT >= 0)
            {
                swingT += Time.deltaTime;
                swingTime = swingT;
                if (!swingHit && swingT >= Motion.HitSeconds)
                {
                    swingHit = true;
                    var cam = transform.parent;
                    if (cam != null && Physics.SphereCast(cam.position, 0.15f, cam.forward, out var hit, Reach))
                        OnStrike?.Invoke(hit);
                }
                if (swingT >= Motion.SwingSeconds)
                    swingT = -1;
            }
            Motion.Pose(idleTime, swingTime, out var hand, out var dir, out var elbow);
            carry.localPosition = hand + HoldShift;
            carry.localRotation = Quaternion.FromToRotation(restDir, dir);
            ElbowHint.localPosition = elbow + HoldShift;
        }

        void LateUpdate()
        {
            if (Player != null)
                transform.rotation = Player.ViewModelRotation;
            if (carry != null && Motion != null)
                FollowMotion();
            if (model == null || Player == null || Player.State == null)
                return;

            var s = Player.State;
            float speed = new FortressUnited.Core.Movement.Vec3(s.Velocity.X, s.Velocity.Y, 0).Length();
            bool onGround = s.OnGround != -1;

            // Walk bob
            if (onGround)
                bobPhase += Time.deltaTime * Mathf.Lerp(0, 9, speed / 400f);
            float bob = Mathf.Sin(bobPhase) * BobAmount * Mathf.Clamp01(speed / 300f);
            float bobSide = Mathf.Cos(bobPhase * 0.5f) * BobAmount * 0.6f * Mathf.Clamp01(speed / 300f);

            // Lag behind vertical motion a little (jumps, landings)
            Vector3 target = new Vector3(0, Mathf.Clamp(-s.Velocity.Z * 0.00004f, -0.02f, 0.02f), 0);
            sway = Vector3.SmoothDamp(sway, target, ref swayVelocity, 0.08f);

            model.localPosition = basePosition + new Vector3(bobSide, bob, 0) + sway;
        }
    }
}
