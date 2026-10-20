using FortressUnited.Core.Movement;
using UnityEngine;

namespace FortressUnited.Game
{
    /// <summary>
    /// The frag (TFC's normal hand grenade, tf_wpn_grenades.cpp and
    /// combat.cpp RadiusDamage); fuse and throw as HandGrenade.
    /// - 180 damage, falling off half a point per unit (DMG_RADIUS_QUAKE):
    ///   none beyond 360 units. Walls block it. Your own hurts you at 75%.
    /// - Players are knocked away from it at 8 units/s per point of damage
    ///   (player.cpp TakeDamage): a point-blank frag of your own throws you
    ///   at about 1080 units/s, the grenade jump.
    /// The look is ours: fireball, sparks, smoke and a shock ring.
    /// </summary>
    public sealed class FragGrenade : HandGrenade
    {
        public const float Damage = 180;
        public const float Reach = Damage / 0.5f;   // units where the damage runs out

        /// <summary>TFC's damage at a distance in units (self = the thrower).</summary>
        public static float DamageAt(float distUnits, bool self)
        {
            float d = Mathf.Max(0, Damage - distUnits * 0.5f);
            return self ? d * 0.75f : d;
        }

        /// <summary>TFC's knockback on a player for that damage (units/s), away from the blast.</summary>
        public static Vec3 Knockback(Vec3 playerOrigin, Vec3 blast, float damage)
        {
            Vec3 dir = playerOrigin - blast;
            float len = dir.Length();
            dir = len > 0.001f ? dir * (1 / len) : new Vec3(0, 0, 1);
            return dir * damage * 8;
        }

        protected override void Effect(Vector3 at)
        {
            var boom = new GameObject("Frag Blast");
            boom.transform.position = at;
            float ground = Physics.Raycast(at + Vector3.up * 0.1f, Vector3.down, out var hit, 3) ? hit.point.y : at.y;
            boom.AddComponent<SentryExplosion>().Play(Color.Lerp(new Color(1f, 0.6f, 0.3f), Team, 0.3f), ground, 0.7f);
        }
    }
}
