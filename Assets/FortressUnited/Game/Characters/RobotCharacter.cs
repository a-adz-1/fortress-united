using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace FortressUnited.Game
{
    /// <summary>
    /// A class model (rigged, Humanoid) placed in the world: plays an
    /// animation clip, carries the robot effects, and knows its chest
    /// screen spot (the ChestScreen marker from prepare_character.py).
    /// </summary>
    public sealed class RobotCharacter : MonoBehaviour
    {
        public Animator Animator { get; private set; }
        public RobotFx Fx { get; private set; }
        /// <summary>Where a flatline screen goes: sized and facing out of the chest.</summary>
        public Transform ScreenAnchor { get; private set; }
        /// <summary>Movement-driven animation, when spawned with a clip set.</summary>
        public CharacterAnimator Anim { get; private set; }

        PlayableGraph graph;

        public static RobotCharacter Spawn(GameObject prefab, Vector3 feet, float yaw, Color team, IDictionary<string, AnimationClip> clips)
        {
            var c = Spawn(prefab, feet, yaw, team);
            if (clips != null && clips.Count > 0)
                c.Anim = CharacterAnimator.Attach(c.Animator, clips);
            return c;
        }

        public static RobotCharacter Spawn(GameObject prefab, Vector3 feet, float yaw, Color team, AnimationClip clip = null)
        {
            var go = Instantiate(prefab, feet, Quaternion.Euler(0, yaw, 0));
            go.name = prefab.name;
            var c = go.AddComponent<RobotCharacter>();
            c.Animator = go.GetComponentInChildren<Animator>();
            if (c.Animator == null)
                c.Animator = go.AddComponent<Animator>();
            c.Animator.applyRootMotion = false;
            c.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>())
                smr.updateWhenOffscreen = true;   // bounds follow the bones, also as a ragdoll

            Transform chest = null;
            foreach (var t in go.GetComponentsInChildren<Transform>())
                if (t.name == "ChestScreen") chest = t;
            if (chest != null)
            {
                // A quad (which faces -Z) on the chest panel, facing forward
                c.ScreenAnchor = new GameObject("ScreenAnchor").transform;
                c.ScreenAnchor.SetParent(chest, false);
                c.ScreenAnchor.rotation = Quaternion.LookRotation(-go.transform.forward, go.transform.up);
                Vector3 parentScale = chest.lossyScale;
                c.ScreenAnchor.localScale = new Vector3(0.11f / parentScale.x, 0.045f / parentScale.y, 1 / parentScale.z);
            }
            c.Fx = RobotFx.Attach(go, team, chest);
            if (clip != null)
                c.Play(clip);
            return c;
        }

        public void Play(AnimationClip clip)
        {
            Stop();
            AnimationPlayableUtilities.PlayClip(Animator, clip, out graph);
        }

        public void Stop()
        {
            if (graph.IsValid())
                graph.Destroy();
            if (Anim != null)
            {
                Anim.Stop();
                Destroy(Anim);
                Anim = null;
            }
        }

        /// <summary>
        /// Destroyed: plays the start of the death clip (the hit landing), then
        /// hands over to the ragdoll, thrown with the given velocity.
        /// </summary>
        public void Die(Vector3 velocity, float heartRate, float deathClipSeconds = 0.3f)
        {
            float length = Anim != null ? Anim.Die() : 0;
            StartCoroutine(Ragdoll(velocity, heartRate, length > 0 ? Mathf.Min(deathClipSeconds, length) : 0));
        }

        IEnumerator Ragdoll(Vector3 velocity, float heartRate, float delay)
        {
            if (delay > 0)
                yield return new WaitForSeconds(delay);
            var bones = Bones;
            Stop();
            foreach (var col in GetComponents<Collider>())
                Destroy(col);
            foreach (var w in GetComponents<DummyWalker>())
                Destroy(w);
            Corpse.Create(gameObject, bones, velocity, heartRate, ScreenAnchor);
        }

        /// <summary>Ragdoll bones for Corpse.Create.</summary>
        public BodyBones Bones => BodyBones.FromAnimator(Animator);

        void OnDestroy() => Stop();
    }

    /// <summary>
    /// Moves a character through a demo routine for the greybox, so every
    /// animation shows: walk out, walk back (backwards), crouch walk out,
    /// turn and run back, stand, jump, swim on the spot. Feeds its
    /// CharacterAnimator.
    /// </summary>
    public sealed class DummyWalker : MonoBehaviour
    {
        public float Distance = 3;

        struct Step { public string Name; public float Speed, Seconds; public bool Crouch, Jump, Swim; }
        static readonly Step[] Routine =
        {
            new Step { Name = "walk", Speed = 1.3f },
            new Step { Name = "walk backward", Speed = -1.0f },
            new Step { Name = "crouch walk", Speed = 0.8f, Crouch = true },
            new Step { Name = "turn", Seconds = 0.4f },     // face back the way it came
            new Step { Name = "run", Speed = 4.5f },
            new Step { Name = "stand", Seconds = 1.5f },
            new Step { Name = "jump", Seconds = 1.6f, Jump = true },
            new Step { Name = "swim", Seconds = 3, Swim = true },
            new Step { Name = "stand", Seconds = 1 },
        };

        RobotCharacter character;
        Vector3 start, axis;
        float along, stepTime, height, verticalSpeed;
        int step;
        bool facingOut = true;

        public string Doing => Routine[step].Name;

        void Start()
        {
            character = GetComponent<RobotCharacter>();
            start = transform.position;
            axis = transform.forward;
        }

        void Update()
        {
            var s = Routine[step];
            float dt = Time.deltaTime;
            stepTime += dt;
            Vector3 facing = facingOut ? axis : -axis;
            float moved = s.Speed * dt;          // + forward, - backward, relative to facing
            float delta = facingOut ? moved : -moved;
            along += delta;

            bool done = s.Seconds > 0 ? stepTime >= s.Seconds : (along >= Distance || along <= 0);
            if (s.Jump)
            {
                if (stepTime < dt * 1.5f) verticalSpeed = 4.2f;
                verticalSpeed -= 9.8f * dt;
                height = Mathf.Max(0, height + verticalSpeed * dt);
            }
            along = Mathf.Clamp(along, 0, Distance);
            transform.position = start + axis * along + Vector3.up * (s.Swim ? 0.6f : height);
            transform.rotation = Quaternion.LookRotation(facing, Vector3.up);

            if (character != null && character.Anim != null)
            {
                var anim = character.Anim;
                anim.Velocity = new Vector3(0, 0, s.Swim ? 1.0f : s.Speed);
                anim.Crouched = s.Crouch;
                anim.InWater = s.Swim;
                anim.Grounded = !(s.Jump && (height > 0.01f || stepTime < 0.1f));
            }

            if (done)
            {
                step = (step + 1) % Routine.Length;
                stepTime = 0;
                height = 0;
                // Out along the line facing out; after the turn, run back facing in
                if (step == 0) facingOut = true;
                if (Routine[step].Name == "turn") facingOut = false;
            }
        }
    }
}
