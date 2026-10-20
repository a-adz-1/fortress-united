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
        public float Scale = 0.7f;
        public float BobAmount = 0.008f;

        public PlayerController Player;
        public VitalsDisplay Display;

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

        void LateUpdate()
        {
            if (Player != null)
                transform.rotation = Player.ViewModelRotation;
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
