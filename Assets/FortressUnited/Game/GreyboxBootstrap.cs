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
            if (MedkitBlue == null)
                MedkitBlue = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/FortressUnited/Art/Medkit/Medkit_Blue.fbx");
            if (MedkitRed == null)
                MedkitRed = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/FortressUnited/Art/Medkit/Medkit_Red.fbx");
#endif
            var prefab = Team == 2 ? MedkitRed : MedkitBlue;
            var cam = player.GetComponentInChildren<Camera>();
            if (prefab != null && cam != null)
                driver.Medkit = MedkitViewModel.Create(prefab, cam, player, display).gameObject;
            else
                Debug.LogWarning("Fortress United: no medikit model, vitals run without a view model");
        }
    }
}
