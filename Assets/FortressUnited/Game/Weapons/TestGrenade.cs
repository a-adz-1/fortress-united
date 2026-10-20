using UnityEngine;

namespace FortressUnited.Game
{
    /// <summary>
    /// A stand-in hand grenade for the greybox until the real grenades exist:
    /// thrown with physics, bounces, goes off after its fuse with a blast of
    /// TFC's hand grenade strength (180, falling off over 2.5x that in units).
    /// Everything in range is passed to the hit callback, so robots ripple,
    /// sentries take damage and bodies get thrown.
    /// </summary>
    public sealed class TestGrenade : MonoBehaviour
    {
        const float Unit = 0.0254f;
        public float Fuse = 2.5f;
        public float Damage = 180;

        System.Action<Transform, Vector3, float> onHit;
        float bornAt;
        Light blink;

        public static TestGrenade Throw(Vector3 from, Vector3 velocity, System.Action<Transform, Vector3, float> onHit)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "Test Grenade";
            go.transform.position = from;
            go.transform.localScale = Vector3.one * 0.12f;
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            mat.color = new Color(0.2f, 0.22f, 0.25f);
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = 0.5f;
            rb.linearVelocity = velocity;
            rb.angularVelocity = Random.insideUnitSphere * 10;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            var g = go.AddComponent<TestGrenade>();
            g.onHit = onHit;
            g.bornAt = Time.time;
            g.blink = new GameObject("Blink").AddComponent<Light>();
            g.blink.transform.SetParent(go.transform, false);
            g.blink.color = new Color(1f, 0.3f, 0.2f);
            g.blink.range = 1;
            g.blink.shadows = LightShadows.None;
            return g;
        }

        void Update()
        {
            float left = bornAt + Fuse - Time.time;
            // Blinks faster as it's about to go
            blink.intensity = Mathf.Repeat(Time.time * (left < 1 ? 8 : 3), 1) < 0.5f ? 2 : 0;
            if (left > 0)
                return;

            Vector3 at = transform.position;
            float radius = Damage * 2.5f * Unit;
            GetComponent<Collider>().enabled = false;
            foreach (var col in Physics.OverlapSphere(at, radius))
            {
                Vector3 p = col.ClosestPoint(at);
                float dmg = Damage * (1 - Vector3.Distance(p, at) / radius);
                if (dmg <= 1)
                    continue;
                // Only what the blast can see
                if (Physics.Linecast(at, p, out var block) && block.collider != col && !block.transform.IsChildOf(col.transform.root))
                    continue;
                onHit?.Invoke(col.transform, p, dmg);
                if (col.attachedRigidbody != null && !col.attachedRigidbody.isKinematic)
                    col.attachedRigidbody.AddExplosionForce(dmg * 0.15f, at, radius, 0.4f, ForceMode.VelocityChange);
            }

            var boom = new GameObject("Grenade Blast");
            boom.transform.position = at;
            boom.AddComponent<SentryExplosion>().Play(new Color(1f, 0.6f, 0.3f), at.y - 0.05f);
            Destroy(gameObject);
        }
    }
}
