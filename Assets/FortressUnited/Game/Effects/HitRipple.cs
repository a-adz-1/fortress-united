using System.Collections.Generic;
using UnityEngine;

namespace FortressUnited.Game
{
    /// <summary>
    /// Shield ripples on a robot's armour when it's hit, in place of blood:
    /// rings of the team colour run out across the body from the hit point,
    /// and big hits flash the whole outline. Adds the ShieldRipple material
    /// over every mesh of the character.
    /// </summary>
    public sealed class HitRipple : MonoBehaviour
    {
        public Color Colour = new Color(0.3f, 0.7f, 1f);
        public float Intensity = 2.5f;

        Material material;
        readonly Vector4[] hits = new Vector4[4];
        Vector4 strength = Vector4.zero;
        int next;
        float flash;

        static readonly int HitPos = Shader.PropertyToID("_HitPos");
        static readonly int HitStrength = Shader.PropertyToID("_HitStrength");
        static readonly int Flash = Shader.PropertyToID("_Flash");
        static readonly int ColourId = Shader.PropertyToID("_Color");
        static readonly int Now = Shader.PropertyToID("_Now");
        float lastHit = -1000;

        public static HitRipple Attach(GameObject character, Color colour)
        {
            var r = character.AddComponent<HitRipple>();
            r.Colour = colour;
            r.Setup();
            return r;
        }

        void Setup()
        {
            var shader = Shader.Find("FortressUnited/ShieldRipple");
            if (shader == null)
            {
                Debug.LogWarning("Fortress United: ShieldRipple shader not found");
                return;
            }
            material = new Material(shader) { name = "ShieldRipple" };
            material.SetColor(ColourId, Colour * Intensity);
            for (int i = 0; i < hits.Length; i++)
                hits[i] = new Vector4(0, 0, 0, -1000);
            Push();

            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                if (!(r is MeshRenderer || r is SkinnedMeshRenderer))
                    continue;
                var mats = new List<Material>(r.sharedMaterials) { material };
                r.sharedMaterials = mats.ToArray();
            }
        }

        /// <summary>A hit at a world position. Damage scales the ripple; ~25 is a normal hit.</summary>
        public void Hit(Vector3 point, float damage)
        {
            if (material == null)
                return;
            hits[next] = new Vector4(point.x, point.y, point.z, Time.timeSinceLevelLoad);
            strength[next] = Mathf.Clamp(damage / 25f, 0.4f, 2.5f);
            next = (next + 1) % hits.Length;
            lastHit = Time.timeSinceLevelLoad;
            flash = Mathf.Max(flash, Mathf.Clamp(damage / 50f, 0.2f, 1.2f));
            Push();
        }

        void Update()
        {
            // Only tick while something is showing
            if (material == null || (flash <= 0 && Time.timeSinceLevelLoad - lastHit > 2))
                return;
            flash = Mathf.MoveTowards(flash, 0, Time.deltaTime * 3);
            material.SetFloat(Flash, flash);
            material.SetFloat(Now, Time.timeSinceLevelLoad);
        }

        void Push()
        {
            material.SetVectorArray(HitPos, hits);
            material.SetVector(HitStrength, strength);
            material.SetFloat(Flash, flash);
            material.SetFloat(Now, Time.timeSinceLevelLoad);
        }

        void OnDestroy()
        {
            if (material != null)
                Destroy(material);
        }
    }
}
