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

        PlayableGraph graph;

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
        }

        /// <summary>Ragdoll bones for Corpse.Create.</summary>
        public BodyBones Bones => BodyBones.FromAnimator(Animator);

        void OnDestroy() => Stop();
    }

    /// <summary>Walks a character back and forth along a line, for a moving target.</summary>
    public sealed class DummyWalker : MonoBehaviour
    {
        public float Speed = 1.3f;     // m/s, about the walk cycle's stride
        public float Distance = 3;
        Vector3 start, dir;
        float travelled;
        int sign = 1;

        void Start()
        {
            start = transform.position;
            dir = transform.forward;
        }

        void Update()
        {
            travelled += Speed * Time.deltaTime * sign;
            if (travelled > Distance || travelled < 0)
            {
                sign = -sign;
                travelled = Mathf.Clamp(travelled, 0, Distance);
            }
            transform.position = start + dir * travelled;
            transform.rotation = Quaternion.LookRotation(dir * sign, Vector3.up);
        }
    }
}
