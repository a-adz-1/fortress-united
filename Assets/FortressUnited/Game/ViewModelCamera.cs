using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace FortressUnited.Game
{
    /// <summary>
    /// Held weapons (view models) are drawn by their own camera with a fixed
    /// field of view, stacked over the world camera. Whatever FOV a player
    /// uses for the world (90, 120, 150+), the weapon in their hands keeps
    /// the same size and shape instead of stretching off the screen edge as
    /// in TFC, and it never pokes through walls (its depth is cleared).
    /// </summary>
    public static class ViewModelCamera
    {
        /// <summary>The layer view models are drawn on (any free layer works; it needs no name).</summary>
        public const int Layer = 30;

        /// <summary>Vertical FOV for view models: 54 is about 85 horizontal at 16:9.</summary>
        public static float Fov = 54;

        /// <summary>Adds (once) the view model camera to a world camera and returns it.</summary>
        public static Camera Ensure(Camera world)
        {
            var existing = world.transform.Find("View Model Camera");
            if (existing != null)
                return existing.GetComponent<Camera>();

            var go = new GameObject("View Model Camera");
            go.transform.SetParent(world.transform, false);
            var vm = go.AddComponent<Camera>();
            vm.cullingMask = 1 << Layer;
            vm.nearClipPlane = 0.01f;
            vm.farClipPlane = 10;
            vm.fieldOfView = Fov;
            var data = vm.GetUniversalAdditionalCameraData();
            data.renderType = CameraRenderType.Overlay;
            data.renderShadows = false;
            data.renderPostProcessing = true;      // bloom on glows

            world.cullingMask &= ~(1 << Layer);
            var worldData = world.GetUniversalAdditionalCameraData();
            worldData.cameraStack.Add(vm);
            go.AddComponent<Follow>().World = world;
            return vm;
        }

        /// <summary>Puts a held model (and everything under it) on the view model layer.</summary>
        public static void Hold(GameObject model, Camera world)
        {
            Ensure(world);
            foreach (var t in model.GetComponentsInChildren<Transform>(true))
                t.gameObject.layer = Layer;
        }

        /// <summary>Keeps the view model camera's FOV and aspect set.</summary>
        sealed class Follow : MonoBehaviour
        {
            public Camera World;
            Camera self;

            void LateUpdate()
            {
                if (self == null) self = GetComponent<Camera>();
                self.fieldOfView = Fov;
                if (World != null) self.aspect = World.aspect;
            }
        }
    }
}
