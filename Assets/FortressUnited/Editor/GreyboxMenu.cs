using System.IO;
using FortressUnited.Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FortressUnited.EditorTools
{
    /// <summary>
    /// Fortress United menu: pick a map and play it in the greybox scene.
    /// The chosen map path is stored per machine (not in the scene), so the
    /// scene can be committed while everyone uses their own map files.
    /// </summary>
    public static class GreyboxMenu
    {
        const string ScenePath = "Assets/FortressUnited/Scenes/Greybox.unity";

        [MenuItem("Fortress United/Open Map...", priority = 1)]
        static void OpenMap()
        {
            string last = PlayerPrefs.GetString(MapLoader.LastMapKey, "");
            string dir = string.IsNullOrEmpty(last) ? "" : Path.GetDirectoryName(last);
            string path = EditorUtility.OpenFilePanel("Open a GoldSrc / TFC map", dir, "bsp");
            if (string.IsNullOrEmpty(path))
                return;
            PlayerPrefs.SetString(MapLoader.LastMapKey, path);
            PlayerPrefs.Save();

            OpenGreyboxScene();
            if (!EditorApplication.isPlaying)
                EditorApplication.EnterPlaymode();
        }

        [MenuItem("Fortress United/Play Last Map", priority = 2)]
        static void PlayLast()
        {
            if (string.IsNullOrEmpty(PlayerPrefs.GetString(MapLoader.LastMapKey, "")))
            {
                OpenMap();
                return;
            }
            OpenGreyboxScene();
            EditorApplication.EnterPlaymode();
        }

        [MenuItem("Fortress United/Create Greybox Scene", priority = 20)]
        static void CreateScene() => OpenGreyboxScene(forceCreate: true);

        static void OpenGreyboxScene(bool forceCreate = false)
        {
            if (!forceCreate && File.Exists(ScenePath))
            {
                if (EditorSceneManager.GetActiveScene().path != ScenePath)
                {
                    if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                        return;
                    EditorSceneManager.OpenScene(ScenePath);
                }
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var go = new GameObject("Greybox");
            go.AddComponent<MapLoader>();
            go.AddComponent<GreyboxBootstrap>();
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log($"Fortress United: created {ScenePath}");
        }
    }
}
