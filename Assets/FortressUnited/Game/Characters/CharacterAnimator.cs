using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace FortressUnited.Game
{
    /// <summary>
    /// Picks a class model's animation from how it is moving (set Velocity,
    /// Grounded, Crouched and InWater every frame) and crossfades between
    /// them: standing, walk, walk backward, crouch walk, jump, swim. Heal
    /// plays on the upper body only, so it works while moving. Death plays
    /// over everything (bodies then hand over to the ragdoll).
    /// Clips are found by name (as prepare_character.py --anim names them).
    /// Missing clips fall back to the nearest one there is.
    /// </summary>
    public sealed class CharacterAnimator : MonoBehaviour
    {
        /// <summary>Velocity in the character's own space (m/s): +z forward, +x right.</summary>
        public Vector3 Velocity;
        public bool Grounded = true, Crouched, InWater;
        public float Crossfade = 0.15f;

        // Speed (m/s) each cycle was animated at, for matching the feet to the ground
        static readonly Dictionary<string, float> NativeSpeed = new Dictionary<string, float>
        {
            { "Walking", 1.3f }, { "WalkBackward", 1.0f }, { "CrouchWalk", 0.8f }, { "Run", 3.5f }, { "Swim", 1.0f },
        };

        enum State { Stand, Walk, WalkBack, CrouchWalk, Run, Jump, Swim, Death }

        PlayableGraph graph;
        AnimationMixerPlayable mixer;
        AnimationLayerMixerPlayable layers;
        readonly Dictionary<State, AnimationClipPlayable> states = new Dictionary<State, AnimationClipPlayable>();
        readonly Dictionary<State, int> inputs = new Dictionary<State, int>();
        readonly Dictionary<State, string> clipNames = new Dictionary<State, string>();
        AnimationClipPlayable heal;
        float healWeight, healUntil = -1;
        State current = State.Stand;
        bool dead;

        public static CharacterAnimator Attach(Animator animator, IDictionary<string, AnimationClip> clips)
        {
            var a = animator.gameObject.AddComponent<CharacterAnimator>();
            a.Build(animator, clips);
            return a;
        }

        void Build(Animator animator, IDictionary<string, AnimationClip> clips)
        {
            AnimationClip Get(params string[] names)
            {
                foreach (var n in names)
                    if (clips.TryGetValue(n, out var c) && c != null)
                        return c;
                return null;
            }

            graph = PlayableGraph.Create($"{name} animation");
            graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
            mixer = AnimationMixerPlayable.Create(graph, 0);

            void Add(State s, AnimationClip clip, bool hold = false)
            {
                if (clip == null) return;
                var p = AnimationClipPlayable.Create(graph, clip);
                if (hold) p.SetSpeed(0);   // a still pose
                inputs[s] = mixer.AddInput(p, 0, 0);
                states[s] = p;
                clipNames[s] = clip.name;
            }
            // Standing: the first frame of the heal clip is a neutral stance
            Add(State.Stand, Get("Idle", "Heal", "Walking"), hold: !clips.ContainsKey("Idle"));
            Add(State.Walk, Get("Walking"));
            Add(State.WalkBack, Get("WalkBackward", "Walking"));
            Add(State.CrouchWalk, Get("CrouchWalk", "Walking"));
            Add(State.Run, Get("Run", "Walking"));
            Add(State.Jump, Get("Jump"));
            Add(State.Swim, Get("Swim", "Walking"));
            Add(State.Death, Get("Death"));
            if (inputs.TryGetValue(State.Stand, out int stand))
                mixer.SetInputWeight(stand, 1);

            layers = AnimationLayerMixerPlayable.Create(graph, 2);
            graph.Connect(mixer, 0, layers, 0);
            layers.SetInputWeight(0, 1);
            var healClip = Get("Heal");
            if (healClip != null)
            {
                heal = AnimationClipPlayable.Create(graph, healClip);
                graph.Connect(heal, 0, layers, 1);
                layers.SetLayerMaskFromAvatarMask(1, UpperBody());
            }
            layers.SetInputWeight(1, 0);

            var output = AnimationPlayableOutput.Create(graph, "Animation", animator);
            output.SetSourcePlayable(layers);
            graph.Play();
        }

        static AvatarMask UpperBody()
        {
            var mask = new AvatarMask();
            for (var part = AvatarMaskBodyPart.Root; part < AvatarMaskBodyPart.LastBodyPart; part++)
                mask.SetHumanoidBodyPartActive(part, false);
            foreach (var part in new[] { AvatarMaskBodyPart.Body, AvatarMaskBodyPart.Head, AvatarMaskBodyPart.LeftArm,
                         AvatarMaskBodyPart.RightArm, AvatarMaskBodyPart.LeftFingers, AvatarMaskBodyPart.RightFingers })
                mask.SetHumanoidBodyPartActive(part, true);
            return mask;
        }

        /// <summary>The repair kit push, on the upper body.</summary>
        public void Heal()
        {
            if (!heal.IsValid())
                return;
            heal.SetTime(0);
            healUntil = Time.time + (float)heal.GetAnimationClip().length * 0.8f;
        }

        /// <summary>Plays the death clip over everything; returns its length.</summary>
        public float Die()
        {
            dead = true;
            if (states.TryGetValue(State.Death, out var p))
            {
                p.SetTime(0);
                current = State.Death;
                return p.GetAnimationClip().length;
            }
            return 0;
        }

        State Pick(out float speedScale)
        {
            float speed = new Vector2(Velocity.x, Velocity.z).magnitude;
            State s;
            if (InWater) s = State.Swim;
            else if (!Grounded) s = State.Jump;
            else if (speed < 0.15f) s = State.Stand;
            else if (Crouched) s = State.CrouchWalk;
            else if (Velocity.z < -0.1f && Velocity.z < -Mathf.Abs(Velocity.x) * 0.5f) s = State.WalkBack;
            else if (speed > 2.6f && states.ContainsKey(State.Run) && clipNames[State.Run] == "Run") s = State.Run;
            else s = State.Walk;
            if (!states.ContainsKey(s))
                s = states.ContainsKey(State.Walk) ? State.Walk : State.Stand;

            speedScale = 1;
            if (clipNames.TryGetValue(s, out var clip) && NativeSpeed.TryGetValue(clip, out float native))
                speedScale = s == State.Swim ? Mathf.Clamp(speed / native, 0.5f, 1.6f) : Mathf.Clamp(speed / native, 0.5f, 2.5f);
            return s;
        }

        void Update()
        {
            if (!graph.IsValid())
                return;

            if (!dead)
            {
                State next = Pick(out float speedScale);
                if (next != current && next == State.Jump && states.TryGetValue(State.Jump, out var jump))
                    jump.SetTime(0);
                current = next;
                if (states.TryGetValue(current, out var playing) && current != State.Stand)
                    playing.SetSpeed(speedScale);
            }

            // Crossfade towards the current state
            float step = Time.deltaTime / Mathf.Max(0.01f, Crossfade);
            float total = 0;
            foreach (var kv in inputs)
            {
                float w = Mathf.MoveTowards(mixer.GetInputWeight(kv.Value), kv.Key == current ? 1 : 0, step);
                mixer.SetInputWeight(kv.Value, w);
                total += w;
            }
            if (total > 0)
                foreach (var kv in inputs)
                    mixer.SetInputWeight(kv.Value, mixer.GetInputWeight(kv.Value) / total);

            // Heal on the upper body
            if (heal.IsValid())
            {
                healWeight = Mathf.MoveTowards(healWeight, Time.time < healUntil && !dead ? 1 : 0, step);
                layers.SetInputWeight(1, healWeight);
            }
        }

        public void Stop()
        {
            if (graph.IsValid())
                graph.Destroy();
        }

        void OnDestroy() => Stop();
    }
}
