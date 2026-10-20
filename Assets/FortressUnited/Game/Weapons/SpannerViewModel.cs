using UnityEngine;
using UnityEngine.InputSystem;

namespace FortressUnited.Game
{
    /// <summary>
    /// A melee weapon held in first person: the engineer's plasma spanner,
    /// or the shock prod (the scout, soldier, demoman, heavy, sniper, pyro
    /// and civilian's melee; the civilian, a robot dog, has it as the
    /// tip of a jointed arm on its back; the spy and medic have their own). Points the business end
    /// forward (using the model's Grip and Arc_* markers) and swings on left
    /// click. The spanner keeps a live plasma arc between its jaws that
    /// flares on a swing; the prod only zaps between its prongs on a hit.
    /// Where it's held and how it swings follow TFC's own first-person
    /// models (ViewModelMotions, measured from them): the prod moves like
    /// the crowbar, the spanner like the spanner, the killswitch like the knife.
    /// </summary>
    public sealed class SpannerViewModel : MonoBehaviour
    {
        [Tooltip("Where the grip sits, in camera space (metres).")]
        public Vector3 GripOffset = new Vector3(0.17f, -0.12f, 0.36f);
        [Tooltip("Pitch, yaw, roll of the spanner relative to the view.")]
        public Vector3 HoldAngles = new Vector3(-35, -85, 0);
        public float Scale = 1;
        public float SwingTime = 0.45f;
        public float BobAmount = 0.01f;

        public PlayerController Player;
        /// <summary>The hold and swing, frame by frame; null falls back to GripOffset / HoldAngles.</summary>
        public ViewModelMotion Motion;
        [Tooltip("Turn of the weapon about its own length (degrees), e.g. which way the spanner's jaws face.")]
        public float Roll;
        [Tooltip("Shifts on top of the motion, for arms bulkier than TFC's (camera space, metres).")]
        public Vector3 HandShift, ElbowShift;
        /// <summary>Where the elbow should be (for the arms' IK), moved with the motion.</summary>
        public Transform ElbowHint { get; private set; }
        float idleTime;
        public PlasmaArc Arc { get; private set; }
        /// <summary>Where the hand holds it (the swing pivots about this point).</summary>
        public Transform Grip => pivot;

        Transform pivot, model;
        float swingT = -1;
        float bobPhase;
        bool swingHit;
        [Tooltip("Reach of a swing (m).")]
        public float Reach = 1.3f;
        public float Damage = 25;

        /// <summary>The shock prod: no idle arc, a zap between the prongs on each hit, team skin and glow.</summary>
        public static SpannerViewModel CreateProd(GameObject prefab, Camera cam, PlayerController player, Color team, Texture2D teamSkin)
        {
            var vm = Create(prefab, cam, player, team, "Shock Prod (view model)");
            vm.Arc.Bolts = 0;          // only surges show
            vm.Motion = ViewModelMotions.Prod;
            vm.Roll = 0;
            vm.pivot.localScale = Vector3.one * 0.8f;   // longer than the spanner, so held a little smaller
            vm.zapOnly = true;
            foreach (var r in vm.model.GetComponentsInChildren<Renderer>())
                foreach (var m in r.materials)
                {
                    if (teamSkin != null) m.SetTexture("_BaseMap", teamSkin);
                    m.EnableKeyword("_EMISSION");
                    m.SetColor("_EmissionColor", TeamColours.Glow(team, 1.6f));
                }
            return vm;
        }

        /// <summary>
        /// The spy's killswitch spike: held like a dagger, pointing forward. No
        /// sparks: when a strike lands its glass blade flares bright and fades,
        /// the hack running through it (e.g. jacking into a robot's back to shut
        /// it down).
        /// </summary>
        public static SpannerViewModel CreateKillswitch(GameObject prefab, Camera cam, PlayerController player, Color team, Texture2D teamSkin)
        {
            var vm = CreateProd(prefab, cam, player, team, teamSkin);
            vm.name = "Killswitch (view model)";
            vm.pivot.localScale = Vector3.one * 1.25f;
            vm.GripOffset = new Vector3(0.16f, -0.1f, 0.32f);
            vm.HoldAngles = new Vector3(-40, -22, -30);
            vm.pivot.localPosition = vm.GripOffset;
            vm.pivot.localRotation = Quaternion.Euler(vm.HoldAngles);
            vm.Reach = 1.0f;
            vm.Motion = ViewModelMotions.Killswitch;
            // No arc at all; the blade's own glow flares instead
            if (Application.isPlaying) Destroy(vm.Arc); else DestroyImmediate(vm.Arc);
            vm.Arc = null;
            vm.flashColour = team;
            return vm;
        }

        bool zapOnly;
        Color? flashColour;     // set for blades that flare on a hit instead of arcing
        float flash, flashWas;

        public static SpannerViewModel Create(GameObject prefab, Camera cam, PlayerController player, Color arcColor, string name = "Spanner (view model)")
        {
            var holder = new GameObject(name);
            holder.transform.SetParent(cam.transform, false);
            var vm = holder.AddComponent<SpannerViewModel>();
            vm.Player = player;
            // The pivot sits at the grip, so swings turn about the hand
            vm.pivot = new GameObject("Pivot").transform;
            vm.pivot.SetParent(holder.transform, false);
            vm.model = Instantiate(prefab, vm.pivot).transform;
            vm.ElbowHint = new GameObject("Elbow Hint").transform;
            vm.ElbowHint.SetParent(holder.transform, false);
            vm.Motion = ViewModelMotions.Spanner;
            vm.Roll = 90;      // jaws side-on to the player, as TFC shows its wrench
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
                flash = 1;
                return;
            }
            var target = hit.collider.GetComponentInParent<RobotFx>();
            if (target != null)
                target.Hit(hit.point, Damage);
            if (hit.rigidbody != null && !hit.rigidbody.isKinematic)
                hit.rigidbody.AddForceAtPosition(cam.forward * 3, hit.point, ForceMode.VelocityChange);
            Arc?.Surge(1.5f);
            flash = 1;
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
            // Blade flare: bright on a hit, back to its normal glow in about a third of a second
            if (flashColour.HasValue && (flash > 0 || flashWas > 0))
            {
                flash = Mathf.MoveTowards(flash, 0, Time.deltaTime * 3);
                var c = TeamColours.Glow(flashColour.Value, 1.6f + 10f * flash * flash);
                foreach (var r in model.GetComponentsInChildren<Renderer>())
                    foreach (var m in r.materials)
                        m.SetColor("_EmissionColor", c);
                flashWas = flash;
            }
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
            if (!zapOnly)
                Arc?.Surge(1);
        }

        void LateUpdate()
        {
            if (Player != null)
                transform.rotation = Player.ViewModelRotation;

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
            var sway = new Vector3(bobSide, bob, 0);

            if (Motion == null)
            {
                OldSwing(sway);
                return;
            }

            idleTime += Time.deltaTime;
            float swingTime = -1;
            if (swingT >= 0)
            {
                swingT += Time.deltaTime;
                swingTime = swingT;
                if (!swingHit && swingT >= Motion.HitSeconds)
                {
                    swingHit = true;
                    Strike();
                }
                if (swingT >= Motion.SwingSeconds)
                    swingT = -1;
            }
            Motion.Pose(idleTime, swingTime, out var hand, out var dir, out var elbow);
            pivot.localPosition = hand + HandShift + sway;
            // Point along the weapon direction; keep "up" towards the top of the screen
            pivot.localRotation = Quaternion.LookRotation(dir, Vector3.up) * Quaternion.Euler(0, 0, Roll);
            ElbowHint.localPosition = elbow + ElbowShift + sway;
        }

        /// <summary>The hand-made hold and chop, for weapons without a motion.</summary>
        void OldSwing(Vector3 sway)
        {
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
                if (!swingHit && t >= 0.32f)
                {
                    swingHit = true;
                    Strike();
                }
                if (swingT >= 1) swingT = -1;
            }
            pivot.localPosition = GripOffset + sway;
            pivot.localRotation = Quaternion.Euler(HoldAngles) * swing;
        }
    }
}
