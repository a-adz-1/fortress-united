using FortressUnited.Core.Maps;
using FortressUnited.Core.Movement;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FortressUnited.Game
{
    /// <summary>
    /// A local player for the greybox: reads keyboard and mouse, runs TFC's
    /// movement (PlayerMovement) against the map's real collision at a fixed
    /// tick rate, and places the camera at the eyes, smoothed between ticks.
    ///
    /// Controls: WASD move, mouse look, Space jump, Ctrl crouch, Shift walk,
    /// V noclip, Esc releases the mouse, F1 toggles the speed readout.
    /// </summary>
    public sealed class PlayerController : MonoBehaviour
    {
        [Header("Movement")]
        [Tooltip("Class max speed (TFC: scout 400, medic 320, soldier 240, HWGuy 230, ...).")]
        public float MaxSpeed = 400;
        [Tooltip("Commands per second. TFC servers commonly ran 100.")]
        public int TickRate = 100;
        public float ForwardSpeed = 400, SideSpeed = 400, UpSpeed = 320;

        [Header("Mouse")]
        [Tooltip("Like the in-game sensitivity setting (degrees per mouse count = 0.022 x sensitivity).")]
        public float Sensitivity = 3;
        public bool InvertY;

        [Header("View")]
        [Tooltip("Horizontal field of view for the world, 70-170 (held weapons have their own, see ViewModelCamera).")]
        [Range(70, 170)] public float FieldOfView = 90;
        public bool ShowSpeed = true;

        public BspWorld World;
        public MoveState State { get; private set; }
        /// <summary>
        /// Extra view angles on top of the punch angle: the concussion sway.
        /// Like TFC's idle sway it only moves what you see; aim and movement
        /// use the real angles.
        /// </summary>
        public Vec3 ViewOffset;

        /// <summary>
        /// Rotation for held view models: TFC takes the idle sway back off
        /// the weapon (all of the yaw and roll, half the pitch), so it stays
        /// near the real aim while the view sways.
        /// </summary>
        public Quaternion ViewModelRotation { get; private set; } = Quaternion.identity;

        readonly PlayerMovement movement = new PlayerMovement();
        readonly MoveVars vars = new MoveVars();
        Camera cam;
        Vec3 viewAngles;
        Vec3 previousEye, currentEye;
        double accumulator;
        float topSpeed;
        GUIStyle hudStyle;

        public void Spawn(Vec3 origin, float yaw)
        {
            State = new MoveState
            {
                Origin = origin,
                ViewOfs = new Vec3(0, 0, 28),
                MaxSpeed = MaxSpeed,
                ClientMaxSpeed = MaxSpeed,
            };
            State.PhysInfo["tfc"] = "1";
            viewAngles = new Vec3(0, yaw, 0);
            currentEye = previousEye = Eye();
            topSpeed = 0;
        }

        void Start()
        {
            cam = GetComponentInChildren<Camera>();
            if (cam == null)
            {
                var go = new GameObject("Eyes");
                go.transform.SetParent(transform, false);
                cam = go.AddComponent<Camera>();
                cam.nearClipPlane = 0.05f;
                cam.farClipPlane = 1000;
                go.AddComponent<AudioListener>();
            }
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        Vec3 Eye() => State.Origin + State.ViewOfs;

        void Update()
        {
            if (World == null || State == null)
                return;

            var kb = Keyboard.current;
            var mouse = Mouse.current;

            if (kb != null && kb.escapeKey.wasPressedThisFrame)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            if (mouse != null && mouse.leftButton.wasPressedThisFrame && Cursor.lockState != CursorLockMode.Locked)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
            if (kb != null && kb.f1Key.wasPressedThisFrame)
                ShowSpeed = !ShowSpeed;
            if (kb != null && kb.vKey.wasPressedThisFrame)
                State.MoveType = State.MoveType == MoveType.Noclip ? MoveType.Walk : MoveType.Noclip;

            // Mouse look every frame (like the engine, angles go into each command)
            if (mouse != null && Cursor.lockState == CursorLockMode.Locked)
            {
                Vector2 d = mouse.delta.ReadValue();
                float k = 0.022f * Sensitivity;
                viewAngles.Y -= d.x * k;
                // Pitch is positive looking down, so moving the mouse up lowers it
                viewAngles.X += (InvertY ? 1 : -1) * d.y * k;
                viewAngles.X = Mathf.Clamp(viewAngles.X, -89, 89);
                if (viewAngles.Y > 180) viewAngles.Y -= 360;
                if (viewAngles.Y < -180) viewAngles.Y += 360;
            }

            // Fixed-rate simulation
            int msec = Mathf.Max(1, Mathf.RoundToInt(1000f / TickRate));
            accumulator += Time.deltaTime;
            int guard = 0;
            while (accumulator >= msec / 1000.0 && guard++ < 25)
            {
                accumulator -= msec / 1000.0;
                previousEye = currentEye;
                Tick(msec, kb);
                currentEye = Eye();
            }

            // Smooth the camera between ticks
            float alpha = (float)(accumulator / (msec / 1000.0));
            Vec3 eye = previousEye + (currentEye - previousEye) * alpha;
            cam.transform.position = GoldSrcSpace.ToUnity(eye);
            Vec3 view = new Vec3(viewAngles.X + State.PunchAngle.X + ViewOffset.X, viewAngles.Y + State.PunchAngle.Y + ViewOffset.Y, State.Angles.Z + ViewOffset.Z);
            cam.transform.rotation = GoldSrcSpace.AnglesToUnity(view);
            ViewModelRotation = GoldSrcSpace.AnglesToUnity(new Vec3(view.X - ViewOffset.X * 0.5f, view.Y - ViewOffset.Y, view.Z - ViewOffset.Z));
            cam.fieldOfView = Camera.HorizontalToVerticalFieldOfView(FieldOfView, cam.aspect);
        }

        void Tick(int msec, Keyboard kb)
        {
            var cmd = new MoveCommand { Msec = msec, ViewAngles = viewAngles };
            if (kb != null)
            {
                bool walk = kb.leftShiftKey.isPressed;
                float scale = walk ? 0.3f : 1;
                if (kb.wKey.isPressed) { cmd.ForwardMove += ForwardSpeed * scale; cmd.Buttons |= Buttons.Forward; }
                if (kb.sKey.isPressed) { cmd.ForwardMove -= ForwardSpeed * scale; cmd.Buttons |= Buttons.Back; }
                if (kb.dKey.isPressed) { cmd.SideMove += SideSpeed * scale; cmd.Buttons |= Buttons.MoveRight; }
                if (kb.aKey.isPressed) { cmd.SideMove -= SideSpeed * scale; cmd.Buttons |= Buttons.MoveLeft; }
                if (kb.spaceKey.isPressed) { cmd.Buttons |= Buttons.Jump; cmd.UpMove += UpSpeed; }
                if (kb.leftCtrlKey.isPressed || kb.cKey.isPressed) cmd.Buttons |= Buttons.Duck;
            }

            State.MaxSpeed = MaxSpeed;
            State.ClientMaxSpeed = MaxSpeed;
            State.OldAngles = State.Angles;
            movement.Move(State, cmd, World, vars);
            World.CurrentTime += msec / 1000.0;

            float speed = new Vec3(State.Velocity.X, State.Velocity.Y, 0).Length();
            if (speed > topSpeed) topSpeed = speed;
        }

        void OnGUI()
        {
            if (!ShowSpeed || State == null)
                return;
            if (hudStyle == null)
                hudStyle = new GUIStyle(GUI.skin.label) { fontSize = 18, normal = { textColor = Color.white } };
            float speed = new Vec3(State.Velocity.X, State.Velocity.Y, 0).Length();
            string mode = State.MoveType == MoveType.Noclip ? "  [noclip]" : "";
            GUI.Label(new Rect(16, Screen.height - 64, 600, 30),
                $"speed {speed:0}  top {topSpeed:0}  max {MaxSpeed:0} (bhop cap {MaxSpeed * PlayerMovement.BunnyJumpMaxSpeedFactor:0}){mode}", hudStyle);
            GUI.Label(new Rect(16, Screen.height - 36, 600, 30),
                "WASD move, Space jump, Ctrl crouch, Shift walk, V noclip, Esc mouse, F1 hide", hudStyle);
        }
    }
}
