using System.Collections;
using UnityEngine;

namespace FortressUnited.Game
{
    /// <summary>
    /// Sets up the greybox at play: loads the map, adds a sun and ambient
    /// light, and spawns a player at a team spawn point. Put this on one
    /// object in an otherwise empty scene (Fortress United > Create Greybox
    /// Scene does that).
    /// </summary>
    public sealed class GreyboxBootstrap : MonoBehaviour
    {
        [Tooltip("Team spawn to start at: 1 blue, 2 red, 0 any.")]
        public int Team = 1;
        [Tooltip("Class max speed for the player.")]
        public float MaxSpeed = 400;
        [Tooltip("Medikit view models by team (found by name in the editor if left empty).")]
        public GameObject MedkitBlue, MedkitRed;
        [Tooltip("Plasma spanner view models by team (found by name in the editor if left empty).")]
        public GameObject SpannerBlue, SpannerRed;
        [Tooltip("Class models by team, for dummies and bodies (found by name in the editor if left empty).")]
        public GameObject MedicBlue, MedicRed;
        [Tooltip("Conc grenade model (found by name in the editor if left empty).")]
        public GameObject ConcGrenadeModel;
        [Tooltip("Sentry levels 1-3 (found by name in the editor if left empty).")]
        public GameObject[] Sentries = new GameObject[3];
        public Texture2D[] SentryBlueSkins = new Texture2D[3], SentryRedSkins = new Texture2D[3];
        [Tooltip("Their animation clips (all of a model's clips are found in the editor if left empty).")]
        public AnimationClip[] MedicBlueClips, MedicRedClips;

        void Start()
        {
            var loader = GetComponent<MapLoader>();
            if (loader == null)
                loader = gameObject.AddComponent<MapLoader>();
            if (!loader.Load())
                return;

            if (FindAnyObjectByType<Light>() == null)
            {
                var sun = new GameObject("Sun").AddComponent<Light>();
                sun.type = LightType.Directional;
                sun.intensity = 1.1f;
                sun.shadows = LightShadows.Soft;
                sun.transform.rotation = Quaternion.Euler(50, -30, 0);
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(0.45f, 0.45f, 0.5f);
            }

            var spawns = loader.World.SpawnPoints(Team);
            var player = new GameObject("Player").AddComponent<PlayerController>();
            player.World = loader.World;
            player.MaxSpeed = MaxSpeed;
            if (spawns.Count > 0)
                player.Spawn(spawns[0].Origin, spawns[0].Yaw);
            else
                player.Spawn(new FortressUnited.Core.Movement.Vec3(0, 0, 100), 0);

            foreach (var cam in FindObjectsByType<Camera>(FindObjectsSortMode.None))
                if (cam.GetComponentInParent<PlayerController>() == null)
                    cam.gameObject.SetActive(false);

            StartCoroutine(SetUpVitals(player));
        }

        IEnumerator SetUpVitals(PlayerController player)
        {
            yield return null; // the player makes its camera in Start

            var vitals = new PlayerVitals();
            var display = new GameObject("Vitals Display").AddComponent<VitalsDisplay>();
            display.Vitals = vitals;

            var driver = player.gameObject.AddComponent<VitalsTestDriver>();
            driver.Player = player;
            driver.Vitals = vitals;

#if UNITY_EDITOR
            const string art = "Assets/FortressUnited/Art/";
            if (MedkitBlue == null) MedkitBlue = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(art + "Medkit/Medkit_Blue.fbx");
            if (MedkitRed == null) MedkitRed = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(art + "Medkit/Medkit_Red.fbx");
            if (SpannerBlue == null) SpannerBlue = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(art + "Spanner/Spanner_Blue.fbx");
            if (SpannerRed == null) SpannerRed = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(art + "Spanner/Spanner_Red.fbx");
            if (MedicBlue == null) MedicBlue = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(art + "Characters/Medic/Medic_Blue.fbx");
            if (MedicRed == null) MedicRed = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(art + "Characters/Medic/Medic_Red.fbx");
            if (ConcGrenadeModel == null) ConcGrenadeModel = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(art + "Grenades/ConcGrenade.fbx");
            for (int i = 0; i < 3; i++)
            {
                string sp = art + $"Sentry/Sentry_L{i + 1}";
                if (Sentries[i] == null) Sentries[i] = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(sp + ".fbx");
                if (SentryBlueSkins[i] == null) SentryBlueSkins[i] = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(sp + "_Blue_BaseColor.png");
                if (SentryRedSkins[i] == null) SentryRedSkins[i] = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(sp + "_Red_BaseColor.png");
            }
            if (MedicBlueClips == null || MedicBlueClips.Length == 0) MedicBlueClips = Clips(art + "Characters/Medic/Medic_Blue.fbx");
            if (MedicRedClips == null || MedicRedClips.Length == 0) MedicRedClips = Clips(art + "Characters/Medic/Medic_Red.fbx");
#endif
            bool red = Team == 2;
            // The other team's medic stands in as the enemy
            driver.EnemyModel = red ? MedicBlue : MedicRed;
            driver.EnemyClips.Clear();
            foreach (var clip in (red ? MedicBlueClips : MedicRedClips) ?? new AnimationClip[0])
                if (clip != null)
                    driver.EnemyClips[clip.name] = clip;
            driver.EnemyColour = TeamColours.For(!red);
            driver.TeamColour = TeamColours.For(red);
            driver.SentryModels = Sentries;
            driver.ConcModel = ConcGrenadeModel;
            driver.SentryTeamTextures = red ? SentryRedSkins : SentryBlueSkins;
            var cam = player.GetComponentInChildren<Camera>();
            if (cam != null)
                GreyboxPostFx.Add(cam);
            var slots = player.gameObject.AddComponent<ViewModelSlots>();
            var spanner = red ? SpannerRed : SpannerBlue;
            var medkit = red ? MedkitRed : MedkitBlue;
            if (cam == null)
                yield break;
            if (spanner != null)
            {
                var arcColor = TeamColours.For(red);
                slots.Add(SpannerViewModel.Create(spanner, cam, player, arcColor).gameObject);
            }
            if (medkit != null)
                slots.Add(MedkitViewModel.Create(medkit, cam, player, display).gameObject);
            if (slots.Slots.Count == 0)
                Debug.LogWarning("Fortress United: no view models found under Art/");
        }

#if UNITY_EDITOR
        static AnimationClip[] Clips(string path)
        {
            var list = new System.Collections.Generic.List<AnimationClip>();
            foreach (var o in UnityEditor.AssetDatabase.LoadAllAssetsAtPath(path))
                if (o is AnimationClip c && !c.name.StartsWith("__preview__"))
                    list.Add(c);
            return list.ToArray();
        }
#endif
    }
}
