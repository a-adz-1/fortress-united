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
        }
    }
}
