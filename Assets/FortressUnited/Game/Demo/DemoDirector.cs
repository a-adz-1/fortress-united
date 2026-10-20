using System.Collections;
using System.Collections.Generic;
using System.IO;
using FortressUnited.Core.Movement;
using UnityEngine;

namespace FortressUnited.Game
{
    /// <summary>
    /// Plays a scripted showcase on a real map and saves every frame, for
    /// in-game footage (Editor/DemoRecorder.cs runs it and makes a video).
    /// Finds the most open spot on the map and stages: team flags with
    /// hologram banners, an enemy medic going through its animations, a
    /// sentry built and upgraded to level 3 that tracks and destroys the
    /// medic, the body flatlining, a conc and frags (the second one
    /// destroying the sentry), team sniper dots, and first-person views of
    /// the plasma spanner and the repair kit's live screen. Time runs at a
    /// fixed frame rate, so the footage is smooth however slow the render.
    /// </summary>
    [DefaultExecutionOrder(2000)]   // saves each frame after everything (arms included) has moved
    public sealed class DemoDirector : MonoBehaviour
    {
        public string MapPath = "";
        public string OutputDir = "";
        public int Width = 1280, Height = 720, Fps = 30;
        [Tooltip("Stop after this many frames whatever happens.")]
        public int MaxFrames = 2400;

        public GameObject Medic, Sentry1, Sentry2, Sentry3, Conc, Frag, Spanner, Medkit;
        [Tooltip("Record the first-person melee showcase instead of the overview.")]
        public bool MeleeShowcase;
        [Tooltip("Where to stage: \"open\" (the most open outdoor area) or \"spawn\" (a roomy team spawn room).")]
        public string Stage = "open";
        public GameObject Arms, ShockProd, Killswitch;
        public Texture2D ShockProdSkin, KillswitchSkin;
        public Texture2D SentryBlueSkin1, SentryBlueSkin2, SentryBlueSkin3;
        public AnimationClip[] MedicClips;

        const float Unit = 0.0254f;
        Camera cam;
        RenderTexture rt;
        Texture2D readback;
        int frame;
        Vector3 origin, fwd, right;
        System.Func<float, (Vector3 pos, Vector3 look)> shot;
        float shotStart;
        SentryGun sentry;
        readonly Dictionary<RobotCharacter, float> health = new Dictionary<RobotCharacter, float>();
        readonly Dictionary<string, AnimationClip> clips = new Dictionary<string, AnimationClip>();
        SniperDot dotRed, dotBlue;

        void OnEnable() => Application.logMessageReceived += OnLog;
        void OnDisable() => Application.logMessageReceived -= OnLog;

        // Any error ends the recording rather than leaving it running
        void OnLog(string message, string stack, LogType type)
        {
            if (type == LogType.Exception || type == LogType.Error)
            {
                Debug.LogWarning("Demo: stopping after an error");
                Quit();
            }
        }

        void Start()
        {
            Time.captureFramerate = Fps;
            Directory.CreateDirectory(OutputDir);
            foreach (var c in MedicClips ?? new AnimationClip[0])
                if (c != null) clips[c.name] = c;

            var loader = gameObject.AddComponent<MapLoader>();
            loader.MapPath = MapPath;
            if (!loader.Load())
            {
                Debug.LogError("Demo: map didn't load");
                Quit();
                return;
            }
            // Light, sky and a camera with bloom
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.15f;
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(50, -30, 0);
            var sky = new Material(Shader.Find("Skybox/Procedural"));
            RenderSettings.skybox = sky;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.55f, 0.6f, 0.7f);
            RenderSettings.ambientEquatorColor = new Color(0.42f, 0.42f, 0.45f);
            RenderSettings.ambientGroundColor = new Color(0.25f, 0.24f, 0.22f);
            DynamicGI.UpdateEnvironment();

            cam = new GameObject("Demo Camera").AddComponent<Camera>();
            cam.fieldOfView = 62;
            cam.nearClipPlane = 0.03f;
            cam.farClipPlane = 600;
            cam.clearFlags = CameraClearFlags.Skybox;
            GreyboxPostFx.Add(cam);
            rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            cam.targetTexture = rt;
            cam.enabled = false;   // rendered by hand each frame, then saved
            readback = new Texture2D(Width, Height, TextureFormat.RGB24, false);

            if (Stage == "spawn") FindSpawnStage(); else FindStage();
            StartCoroutine(MeleeShowcase ? Melee() : Script());
        }

        // ------------------------------------------------------------ staging

        /// <summary>
        /// The most open outdoor floor in the map: a grid of points over the
        /// map's bounds, each dropped to the floor below; it scores by the
        /// shortest clear distance around it, only counting spots that are
        /// enclosed (walls within reach all round, so inside the map) with
        /// open sky above.
        /// </summary>
        void FindStage()
        {
            var bsp = GetComponent<MapLoader>().Bsp;
            Vector3 a = GoldSrcSpace.ToUnity(bsp.Models[0].Mins), b = GoldSrcSpace.ToUnity(bsp.Models[0].Maxs);
            Vector3 mn = Vector3.Min(a, b), mx = Vector3.Max(a, b);
            float best = -1;
            const int steps = 40;
            for (int ix = 0; ix <= steps; ix++)
                for (int iz = 0; iz <= steps; iz++)
                {
                    float x = Mathf.Lerp(mn.x, mx.x, ix / (float)steps), z = Mathf.Lerp(mn.z, mx.z, iz / (float)steps);
                    // From the top of the map down to the first floor
                    if (!Physics.Raycast(new Vector3(x, mx.y - 0.5f, z), Vector3.down, out var floor, mx.y - mn.y))
                        continue;
                    if (floor.normal.y < 0.9f)
                        continue;
                    Vector3 at = floor.point;
                    if (Physics.Raycast(at + Vector3.up * 0.5f, Vector3.up, 25))
                        continue;   // no sky above: indoors
                    float worst = float.MaxValue, openest = 0;
                    int walls = 0;
                    Vector3 openDir = Vector3.forward;
                    for (int i = 0; i < 16; i++)
                    {
                        var d = Quaternion.Euler(0, i * 22.5f, 0) * Vector3.forward;
                        float dist = 40;
                        if (Physics.Raycast(at + Vector3.up * 1.2f, d, out var h, 40)) { dist = h.distance; walls++; }
                        worst = Mathf.Min(worst, dist);
                        if (dist > openest) { openest = dist; openDir = d; }
                    }
                    if (walls < 12)
                        continue;   // open to the void: outside the playable map
                    float score = Mathf.Min(worst, 14);
                    if (score > best) { best = score; origin = at; fwd = openDir; }
                }
            right = Vector3.Cross(Vector3.up, fwd);
            Debug.Log($"Demo: stage at {origin}, {best:0.0} m clear all round");
        }

        /// <summary>
        /// A team spawn point with the most room in front of it (indoors is
        /// fine), facing its most open direction: a quieter place to stage.
        /// </summary>
        void FindSpawnStage()
        {
            var world = GetComponent<MapLoader>().World;
            float best = -1;
            foreach (var sp in world.SpawnPoints(1))
            {
                Vector3 at = GoldSrcSpace.ToUnity(sp.Origin);
                if (!Physics.Raycast(at + Vector3.up * 0.5f, Vector3.down, out var floor, 4))
                    continue;
                at = floor.point;
                // room: clear distance in the most open direction, with space either side
                for (int i = 0; i < 16; i++)
                {
                    var d = Quaternion.Euler(0, i * 22.5f, 0) * Vector3.forward;
                    float ahead = Physics.Raycast(at + Vector3.up * 1.2f, d, out var h, 15) ? h.distance : 15;
                    var l = Quaternion.Euler(0, -60, 0) * d; var r = Quaternion.Euler(0, 60, 0) * d;
                    float sides = Mathf.Min(Physics.Raycast(at + Vector3.up * 1.2f, l, out var hl, 6) ? hl.distance : 6,
                                            Physics.Raycast(at + Vector3.up * 1.2f, r, out var hr, 6) ? hr.distance : 6);
                    float score = Mathf.Min(ahead, 6) + Mathf.Min(sides, 3);
                    if (score > best) { best = score; origin = at; fwd = d; }
                }
            }
            if (best < 0) { FindStage(); return; }
            right = Vector3.Cross(Vector3.up, fwd);
            Debug.Log($"Demo: spawn stage at {origin}, score {best:0.0}");
        }

        Vector3 Floor(Vector3 p)
        {
            return Physics.Raycast(p + Vector3.up * 2, Vector3.down, out var h, 6) ? h.point : p;
        }

        void Flag(Vector3 at, bool red)
        {
            var root = new GameObject(red ? "Red Flag" : "Blue Flag").transform;
            root.position = Floor(at);
            root.rotation = Quaternion.LookRotation(-fwd);
            var pole = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(pole.GetComponent<Collider>());
            pole.transform.SetParent(root, false);
            pole.transform.localPosition = new Vector3(0, 1.1f, 0);
            pole.transform.localScale = new Vector3(0.06f, 1.1f, 0.06f);
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.color = new Color(0.08f, 0.09f, 0.1f);
            m.SetFloat("_Metallic", 0.8f);
            m.SetFloat("_Smoothness", 0.6f);
            m.EnableKeyword("_EMISSION");
            pole.GetComponent<Renderer>().sharedMaterial = m;
            var tip = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(tip.GetComponent<Collider>());
            tip.transform.SetParent(root, false);
            tip.transform.localPosition = new Vector3(0, 2.25f, 0);
            tip.transform.localScale = Vector3.one * 0.12f;
            var tm = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            tm.color = TeamColours.Glow(TeamColours.For(red), 3);
            tip.GetComponent<Renderer>().sharedMaterial = tm;
            HologramBanner.Create(root, new Vector3(0.04f, 2.1f, 0), 0.75f, 1.25f, red);
        }

        RobotCharacter EnemyMedic(Vector3 at, float yaw, bool walk)
        {
            var c = RobotCharacter.Spawn(Medic, Floor(at), yaw, TeamColours.Red, clips);
            var cap = c.gameObject.AddComponent<CapsuleCollider>();
            cap.center = new Vector3(0, 0.9f, 0);
            cap.height = 1.8f;
            cap.radius = 0.28f;
            if (walk)
                c.gameObject.AddComponent<DummyWalker>().Distance = 3.5f;
            health[c] = 150;
            return c;
        }

        void OnHit(Transform t, Vector3 point, float damage)
        {
            if (t == null) return;
            var building = t.GetComponentInParent<SentryGun>();
            if (building != null) { building.TakeDamage(point, damage); return; }
            t.GetComponentInParent<RobotFx>()?.Hit(point, damage);
            var rb = t.GetComponentInParent<Rigidbody>();
            if (rb != null && !rb.isKinematic)
                rb.AddForceAtPosition((point - origin).normalized * damage * 0.05f, point, ForceMode.VelocityChange);
            var c = t.GetComponentInParent<RobotCharacter>();
            if (c != null && health.TryGetValue(c, out float hp))
            {
                hp -= damage;
                health[c] = hp;
                if (hp <= 0)
                {
                    health.Remove(c);
                    SentryGun.Targets.Remove(c.transform);
                    Vector3 away = sentry != null ? (c.transform.position - sentry.transform.position).normalized : -c.transform.forward;
                    c.Die(away * 3 + Vector3.up * 2.5f, 165);
                    StartCoroutine(ShortLife(c.gameObject));
                }
            }
        }

        IEnumerator ShortLife(GameObject body)
        {
            yield return null;
            var corpse = body.GetComponent<Corpse>();
            if (corpse == null) { yield return new WaitForSeconds(0.4f); corpse = body.GetComponent<Corpse>(); }
            if (corpse != null) corpse.Lifetime = 8;   // beam out within the video
        }

        void Throw<T>(GameObject prefab, Vector3 from, Vector3 to, float flight, bool red) where T : HandGrenade
        {
            var hand = new GameObject("hand").transform;
            hand.position = from;
            var g = HandGrenade.Prime<T>(prefab, hand, TeamColours.For(red), (at, inHand) => Blast<T>(at));
            g.GoesOffAt = Time.time + flight + 0.4f;
            g.Throw(HandGrenade.Lob(from, to, flight));
            Destroy(hand.gameObject);
        }

        void Blast<T>(Vector3 at)
        {
            if (typeof(T) == typeof(FragGrenade))
            {
                foreach (var col in Physics.OverlapSphere(at, FragGrenade.Reach * Unit))
                {
                    Vector3 p = col.ClosestPoint(at);
                    float d = FragGrenade.DamageAt(Vector3.Distance(p, at) / Unit, false);
                    if (d > 1) OnHit(col.transform, p, d);
                }
            }
            else
            {
                foreach (var col in Physics.OverlapSphere(at, ConcGrenade.BlastRadius * Unit))
                {
                    var fx = col.GetComponentInParent<RobotFx>();
                    if (fx != null) { fx.Hit(col.ClosestPoint(at), 8); fx.Glitch = 1; StartCoroutine(Unglitch(fx)); }
                }
            }
        }

        IEnumerator Unglitch(RobotFx fx)
        {
            yield return new WaitForSeconds(2.5f);
            if (fx != null) fx.Glitch = 0;
        }

        void Shot(System.Func<float, (Vector3, Vector3)> s)
        {
            shot = s;
            shotStart = Time.time;
        }

        // ------------------------------------------------------------ camera

        /// <summary>
        /// How far a line from a point gets before it meets the map itself
        /// (characters, sentries and bodies don't block the camera).
        /// </summary>
        float WallDistance(Vector3 from, Vector3 dir, float max)
        {
            float nearest = max;
            foreach (var h in Physics.SphereCastAll(from, 0.25f, dir, max))
                if (h.collider is MeshCollider && h.collider.GetComponentInParent<MapLoader>() != null && h.distance > 0 && h.distance < nearest)
                    nearest = h.distance;
            return nearest;
        }

        /// <summary>
        /// A camera spot about dist from the target and height above it with a
        /// clear view, as close to the preferred direction as there is.
        /// </summary>
        Vector3 ClearView(Vector3 target, float dist, float height, Vector3 prefer)
        {
            Vector3 best = target - prefer.normalized * dist + Vector3.up * height;
            float bestScore = float.MinValue;
            for (int i = 0; i < 32; i++)
            {
                var dir = Quaternion.Euler(0, i * 11.25f, 0) * Vector3.forward;
                Vector3 cand = target + dir * dist + Vector3.up * height;
                Vector3 to = cand - target;
                float clear = WallDistance(target, to.normalized, to.magnitude) / to.magnitude;
                // Mostly the clear spots; among those, nearest the preferred side
                float score = (clear >= 0.999f ? 10 : clear * 5) + Vector3.Dot(dir, -prefer.normalized);
                if (score > bestScore) { bestScore = score; best = target + to.normalized * (to.magnitude * Mathf.Min(1, clear)); }
            }
            return best;
        }

        /// <summary>A shot from a clear spot that drifts sideways a little while looking at a point.</summary>
        void Hold(Vector3 target, float dist, float height, Vector3 prefer, float drift = 0.25f)
        {
            Vector3 at = ClearView(target, dist, height, prefer);
            Vector3 side = Vector3.Cross(Vector3.up, target - at).normalized;
            Shot(t => (at + side * drift * t, target));
        }

        /// <summary>Follows something from a clear angle chosen at the start.</summary>
        void Follow(Transform subject, float dist, float height, Vector3 prefer, float lookHeight = 1.0f)
        {
            Vector3 start = subject.position + Vector3.up * lookHeight;
            Vector3 offset = ClearView(start, dist, height, prefer) - start;
            Shot(t =>
            {
                Vector3 look = subject != null ? subject.position + Vector3.up * lookHeight : start;
                return (look + offset, look);
            });
        }

        // ------------------------------------------------------------ the script

        IEnumerator Script()
        {
            Vector3 sentryAt = Floor(origin + fwd * 1.5f);
            Vector3 lane = origin + fwd * 6.5f - right * 3;
            Flag(origin - right * 3 + fwd * 3.5f, false);
            Flag(origin + right * 3 + fwd * 3.5f, true);
            var walker = EnemyMedic(lane, Quaternion.LookRotation(right).eulerAngles.y, true);

            // 1. Establishing: high and wide
            Hold(origin + fwd * 3 + Vector3.up, 11, 4.5f, fwd, 0.6f);
            yield return new WaitForSeconds(3.5f);

            // 2. The medic: walk, walk backward, crouch walk, turn and run, jump, swim
            Follow(walker.transform, 3.4f, 0.6f, -right, 1.0f);
            yield return new WaitForSeconds(9.0f);

            // 3. The sentry: built, then upgraded to level 2 and 3
            Hold(sentryAt + Vector3.up * 0.9f, 4.2f, 1.0f, fwd + right * 0.6f, 0.2f);
            sentry = SentryGun.Build(Sentry1, sentryAt, Quaternion.LookRotation(fwd).eulerAngles.y, TeamColours.Blue, SentryBlueSkin1, 1);
            sentry.OnHit = OnHit;
            yield return new WaitForSeconds(2.6f);
            Upgrade(2, SentryBlueSkin2);
            yield return new WaitForSeconds(2.0f);
            Upgrade(3, SentryBlueSkin3);
            yield return new WaitForSeconds(2.2f);

            // 4. It finds the medic: tracks, fires, rockets, takes it down
            Vector3 between = Vector3.Lerp(sentryAt, walker.transform.position, 0.4f) + Vector3.up * 1.0f;
            Hold(between, 6.5f, 2.2f, (walker.transform.position - sentryAt).normalized + right * 0.8f, 0.3f);
            SentryGun.Targets.Clear();
            SentryGun.Targets.Add(walker.transform);
            float until = Time.time + 6;
            while (Time.time < until && health.ContainsKey(walker))
                yield return null;
            yield return new WaitForSeconds(1.2f);

            // 5. The body: flatline on the chest screen, sparks, powering down
            var chest = walker != null ? walker.Animator.GetBoneTransform(HumanBodyBones.Chest) : null;
            Vector3 bodyAt = chest != null ? chest.position : lane;
            Hold(bodyAt, 1.7f, 0.9f, sentryAt - bodyAt, 0.15f);
            yield return new WaitForSeconds(3.5f);

            // 6. A conc lands by a second medic: pulse, thrown armour glitching
            SentryGun.Targets.Clear();   // hold fire for the grenades
            var target = EnemyMedic(origin + fwd * 5 + right * 2.2f, Quaternion.LookRotation(-fwd).eulerAngles.y, false);
            Hold(target.transform.position + Vector3.up * 1.0f, 4.0f, 1.2f, fwd - right * 0.5f, 0.25f);
            Vector3 thrower = origin - fwd * 2 + Vector3.up * 1.4f;
            Throw<ConcGrenade>(Conc, thrower, Floor(target.transform.position + right * 0.7f - fwd * 0.6f) + Vector3.up * 0.1f, 1.1f, false);
            yield return new WaitForSeconds(3.6f);

            // 7. Frags at the sentry: the second one destroys it
            Hold(sentryAt + Vector3.up * 0.8f, 6.0f, 2.0f, fwd - right * 0.4f, 0.4f);
            Throw<FragGrenade>(Frag, thrower + right * 3, Floor(sentryAt + right * 1.2f) + Vector3.up * 0.1f, 1.0f, true);
            yield return new WaitForSeconds(2.4f);
            Throw<FragGrenade>(Frag, thrower + right * 3, Floor(sentryAt - fwd * 0.6f) + Vector3.up * 0.1f, 1.0f, true);
            yield return new WaitForSeconds(4.6f);

            // 8. Sniper dots, one per team, sweeping over the medic
            dotBlue = SniperDot.Create(false);
            dotRed = SniperDot.Create(true);
            Vector3 dotFocus = target != null ? target.transform.position + Vector3.up * 1.2f : origin + fwd * 5;
            Hold(dotFocus, 3.4f, 0.4f, fwd + right * 0.4f, 0.15f);
            float dotsUntil = Time.time + 4.5f;
            while (Time.time < dotsUntil)
            {
                float t = Time.time;
                Vector3 bFrom = origin - right * 6 + Vector3.up * 3, rFrom = origin + right * 7 + fwd * 10 + Vector3.up * 4;
                Vector3 bAim = dotFocus + right * Mathf.Sin(t * 1.3f) * 0.6f + Vector3.up * Mathf.Sin(t * 2.1f) * 0.4f;
                Vector3 rAim = dotFocus + right * Mathf.Cos(t * 1.1f) * 0.7f - Vector3.up * (0.5f + Mathf.Sin(t * 1.7f) * 0.3f);
                dotBlue.Aim(bFrom, (bAim - bFrom).normalized, cam);
                dotRed.Aim(rFrom, (rAim - rFrom).normalized, cam);
                yield return null;
            }
            Destroy(dotBlue.gameObject);
            Destroy(dotRed.gameObject);

            // 9. First person: the plasma spanner, then the repair kit's live screen
            shot = null;
            cam.transform.position = origin - fwd * 1.5f + Vector3.up * 1.6f;
            cam.transform.rotation = Quaternion.LookRotation(fwd + Vector3.down * 0.15f);
            var spanner = SpannerViewModel.Create(Spanner, cam, null, TeamColours.Blue);
            yield return new WaitForSeconds(0.8f);
            spanner.Swing();
            yield return new WaitForSeconds(1.5f);
            spanner.Swing();
            yield return new WaitForSeconds(1.6f);
            Destroy(spanner.gameObject);

            var vitals = new PlayerVitals { Speed = 520, MaxSpeed = 400 };
            var display = new GameObject("Vitals Display").AddComponent<VitalsDisplay>();
            display.Vitals = vitals;
            MedkitViewModel.Create(Medkit, cam, null, display);
            for (float t = 0; t < 4.5f; t += Time.deltaTime)
            {
                vitals.Speed = 300 + 300 * Mathf.Sin(t * 1.5f);   // bunny hopping heart rate
                if (t > 2.5f) vitals.Concussion = 60;
                yield return null;
            }
            Quit();
        }

        // ------------------------------------------------------------ melee showcase

        /// <summary>Turns the first-person view smoothly to look at a point.</summary>
        IEnumerator Look(Vector3 at, float seconds)
        {
            var from = cam.transform.rotation;
            var to = Quaternion.LookRotation(at - cam.transform.position);
            for (float t = 0; t < seconds; t += Time.deltaTime)
            {
                cam.transform.rotation = Quaternion.Slerp(from, to, Mathf.SmoothStep(0, 1, t / seconds));
                yield return null;
            }
            cam.transform.rotation = to;
        }

        /// <summary>
        /// First person: each melee weapon in turn, held by the medic's arms,
        /// idling then swinging at a single target dummy (no sentries, no kills).
        /// </summary>
        IEnumerator Melee()
        {
            shot = null;
            cam.transform.position = origin + Vector3.up * 1.6f;
            cam.fieldOfView = Camera.HorizontalToVerticalFieldOfView(100, (float)Width / Height);
            ViewModelCamera.Ensure(cam);
            SentryGun.Targets.Clear();

            var dummy = EnemyMedic(Floor(origin + fwd * 1.35f), Quaternion.LookRotation(-fwd).eulerAngles.y, false);
            health[dummy] = 100000;   // takes hits without dying
            cam.transform.rotation = Quaternion.LookRotation(dummy.transform.position + Vector3.up * 1.15f - cam.transform.position);
            var arms = FirstPersonArms.Create(Arms, cam);

            var weapons = new System.Func<SpannerViewModel>[]
            {
                () => SpannerViewModel.CreateProd(ShockProd, cam, null, TeamColours.Blue, ShockProdSkin),
                () => SpannerViewModel.Create(Spanner, cam, null, TeamColours.Blue),
                () => SpannerViewModel.CreateKillswitch(Killswitch, cam, null, TeamColours.Blue, KillswitchSkin),
            };
            foreach (var make in weapons)
            {
                var w = make();
                w.Reach = 2.2f;
                arms.RightTarget = w.Grip;
                arms.RightElbow = w.ElbowHint;
                yield return new WaitForSeconds(1.0f);       // idle
                for (int i = 0; i < 3; i++)
                {
                    w.Swing();
                    yield return new WaitForSeconds(w.Motion.SwingSeconds + 0.25f);
                }
                yield return new WaitForSeconds(0.4f);
                Destroy(w.gameObject);
                yield return null;
            }

            // The medkit: its use jab infects an enemy (the robot virus)
            var vitals = new PlayerVitals { MaxSpeed = 400 };
            var display = new GameObject("Vitals Display").AddComponent<VitalsDisplay>();
            display.Vitals = vitals;
            var kit = MedkitViewModel.Create(Medkit, cam, null, display);
            kit.Reach = 2.2f;
            kit.OnStrike = hit =>
            {
                var fx = hit.collider.GetComponentInParent<RobotFx>();
                if (fx == null) return;
                fx.Hit(hit.point, 10);
                fx.Virus = 1;
            };
            arms.RightTarget = kit.Grip;
            arms.RightElbow = kit.ElbowHint;
            arms.LeftTarget = kit.LeftGrip;      // two-handed, like TFC's
            arms.LeftElbow = kit.LeftElbowHint;
            arms.MaxHandTurn = 150;              // fists upright on the side handles
            yield return new WaitForSeconds(1.0f);
            kit.Swing();
            yield return new WaitForSeconds(kit.Motion.SwingSeconds + 0.3f);
            kit.Swing();
            yield return new WaitForSeconds(kit.Motion.SwingSeconds + 4.5f);   // watch the infection
            Quit();
        }

        void Upgrade(int level, Texture2D skin)
        {
            var at = sentry.transform.position;
            float yaw = sentry.transform.eulerAngles.y;
            Destroy(sentry.gameObject);
            sentry = SentryGun.Build(level == 2 ? Sentry2 : Sentry3, at, yaw, TeamColours.Blue, skin, level, upgrade: true);
            sentry.OnHit = OnHit;
        }

        // ------------------------------------------------------------ recording

        void LateUpdate()
        {
            if (cam == null)
                return;
            if (frame >= MaxFrames)
            {
                Quit();
                return;
            }
            if (shot != null)
            {
                var (pos, look) = shot(Time.time - shotStart);
                // Keep a clear line from what it's looking at, so it never ends up in a wall
                Vector3 toCam = pos - look;
                float free = WallDistance(look, toCam.normalized, toCam.magnitude);
                if (free < toCam.magnitude)
                    pos = look + toCam.normalized * Mathf.Max(0.5f, free - 0.2f);
                // Cut cleanly on a new shot, then ease
                bool cut = Time.time - shotStart < 0.0001f || frame == 0;
                cam.transform.position = cut ? pos : Vector3.Lerp(cam.transform.position, pos, 0.25f);
                cam.transform.rotation = cut ? Quaternion.LookRotation(look - pos) : Quaternion.Slerp(cam.transform.rotation, Quaternion.LookRotation(look - pos), 0.25f);
            }
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            readback.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            RenderTexture.active = prev;
            File.WriteAllBytes(Path.Combine(OutputDir, $"frame_{frame:00000}.jpg"), readback.EncodeToJPG(90));
            frame++;
        }

        bool quitting;

        void Quit()
        {
            if (quitting)
                return;
            quitting = true;
            Debug.Log($"Demo: recorded {frame} frames to {OutputDir}");
#if UNITY_EDITOR
            UnityEditor.EditorApplication.Exit(0);
#else
            Application.Quit();
#endif
        }
    }
}
