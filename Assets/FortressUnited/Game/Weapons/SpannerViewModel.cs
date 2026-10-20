using UnityEngine;
using UnityEngine.InputSystem;

namespace FortressUnited.Game
{
    /// <summary>
    /// The plasma spanner held in first person: points the jaws forward
    /// (using the model's Grip and Arc_* markers), runs the live plasma arc,
    /// and swings on left click with the arc flaring.
    /// </summary>
    public sealed class SpannerViewModel : MonoBehaviour
    {
        [Tooltip("Where the grip sits, in camera space (metres).")]
        public Vector3 GripOffset = new Vector3(0.17f, -0.2f, 0.12f);
        [Tooltip("Pitch, yaw, roll of the spanner relative to the view.")]
        public Vector3 HoldAngles = new Vector3(-8, -10, -8);
        public float Scale = 1;
        public float SwingTime = 0.45f;
        public float BobAmount = 0.01f;

        public PlayerController Player;
        public PlasmaArc Arc { get; private set; }

        Transform pivot, model;
        float swingT = -1;
        float bobPhase;
        bool swingHit;
        [Tooltip("Reach of a swing (m).")]
        public float Reach = 1.3f;
        public float Damage = 25;

        public static SpannerViewModel Create(GameObject prefab, Camera cam, PlayerController player, Color arcColor)
        {
            var holder = new GameObject("Spanner (view model)");
            holder.transform.SetParent(cam.transform, false);
            var vm = holder.AddComponent<SpannerViewModel>();
            vm.Player = player;
            // The pivot sits at the grip, so swings turn about the hand
            vm.pivot = new GameObject("Pivot").transform;
            vm.pivot.SetParent(holder.transform, false);
            vm.model = Instantiate(prefab, vm.pivot).transform;
            vm.Setup();
            vm.Arc = PlasmaArc.Attach(vm.model, arcColor);
            ViewModelCamera.Hold(holder, cam);
            return vm;
        }

        void Setup()
        {
            model.localScale = Vector3.one * Scale;
            Transform grip = Find("Grip"), top = Find("Arc_Top"), bottom = Find("Arc_Bottom");
            Vector3 g = grip ? Local(grip) : Vector3.zero;
            Quaternion orient = Quaternion.identity;
            if (grip && top && bottom)
            {
                Vector3 jaws = (Local(top) + Local(bottom)) * 0.5f;
                Vector3 forward = (jaws - g).normalized;
                Vector3 up = Vector3.ProjectOnPlane(Local(top) - Local(bottom), forward).normalized;
                orient = Quaternion.Inverse(Quaternion.LookRotation(forward, up));
            }
            model.localRotation = orient;
            model.localPosition = -(orient * Vector3.Scale(g, model.localScale));
            pivot.localPosition = GripOffset;
            pivot.localRotation = Quaternion.Euler(HoldAngles);

            foreach (var r in model.GetComponentsInChildren<Renderer>())
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        void Strike()
        {
            var cam = transform.parent;
            if (cam == null)
                return;
            if (!Physics.SphereCast(cam.position, 0.12f, cam.forward, out var hit, Reach))
                return;
            var sentry = hit.collider.GetComponentInParent<SentryGun>();
            if (sentry != null)
            {
                // Your own sentry: the game upgrades or repairs it
                sentry.Fx?.Hit(hit.point, 6);
                SentryGun.OnSpanner?.Invoke(sentry);
                Arc?.Surge(1.5f);
                return;
            }
            var target = hit.collider.GetComponentInParent<RobotFx>();
            if (target != null)
                target.Hit(hit.point, Damage);
            if (hit.rigidbody != null && !hit.rigidbody.isKinematic)
                hit.rigidbody.AddForceAtPosition(cam.forward * 3, hit.point, ForceMode.VelocityChange);
            Arc?.Surge(1.5f);
        }

        Vector3 Local(Transform t) => model.InverseTransformPoint(t.position);

        Transform Find(string name)
        {
            foreach (var t in model.GetComponentsInChildren<Transform>(true))
                if (t.name == name)
                    return t;
            return null;
        }

        void Update()
        {
            var mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame && Cursor.lockState == CursorLockMode.Locked)
                Swing();
        }

        /// <summary>Starts a swing (left click does this; scripts can too).</summary>
        public void Swing()
        {
            if (swingT >= 0)
                return;
            swingT = 0;
            swingHit = false;
            Arc?.Surge(1);
        }

        void LateUpdate()
        {
            if (Player != null)
                transform.rotation = Player.ViewModelRotation;
            // Swing: wind up, chop down and across, recover
            Quaternion swing = Quaternion.identity;
            if (swingT >= 0)
            {
                swingT += Time.deltaTime / SwingTime;
                float t = swingT;
                float pitch, yaw;
                if (t < 0.2f) { float k = t / 0.2f; pitch = -25 * k; yaw = -10 * k; }
                else if (t < 0.45f) { float k = (t - 0.2f) / 0.25f; k = k * k; pitch = Mathf.Lerp(-25, 55, k); yaw = Mathf.Lerp(-10, 25, k); }
                else { float k = Mathf.SmoothStep(0, 1, (t - 0.45f) / 0.55f); pitch = Mathf.Lerp(55, 0, k); yaw = Mathf.Lerp(25, 0, k); }
                swing = Quaternion.Euler(pitch, yaw, 0);
                // The blow lands mid-chop
                if (!swingHit && t >= 0.32f)
                {
                    swingHit = true;
                    Strike();
                }
                if (swingT >= 1) swingT = -1;
            }

            float bob = 0, bobSide = 0;
            if (Player != null && Player.State != null)
            {
                var v = Player.State.Velocity;
                float speed = Mathf.Sqrt(v.X * v.X + v.Y * v.Y);
                if (Player.State.OnGround != -1)
                    bobPhase += Time.deltaTime * Mathf.Lerp(0, 9, speed / 400f);
                float k = Mathf.Clamp01(speed / 300f);
                bob = Mathf.Sin(bobPhase) * BobAmount * k;
                bobSide = Mathf.Cos(bobPhase * 0.5f) * BobAmount * 0.6f * k;
            }

            pivot.localPosition = GripOffset + new Vector3(bobSide, bob, 0);
            pivot.localRotation = Quaternion.Euler(HoldAngles) * swing;
        }
    }
}
