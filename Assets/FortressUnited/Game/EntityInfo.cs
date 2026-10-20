using System.Collections.Generic;
using UnityEngine;

namespace FortressUnited.Game
{
    /// <summary>Shows a map entity's keyvalues in the inspector.</summary>
    public sealed class EntityInfo : MonoBehaviour
    {
        public string ClassName;
        public List<string> Keys = new List<string>();

        void OnDrawGizmos()
        {
            Gizmos.color = ClassName.Contains("spawn") || ClassName.StartsWith("i_p_t") ? Color.cyan
                : ClassName.Contains("tfgoal") ? new Color(1, 0.5f, 0) : Color.yellow;
            Gizmos.DrawWireCube(transform.position, Vector3.one * 0.4f);
        }
    }
}
