using UnityEngine;

namespace NeonRift.Gameplay
{
    /// <summary>A place rival drivers head for (named by mission data); its children are the slots they stop in.</summary>
    public sealed class RaceMarker : MonoBehaviour
    {
        [SerializeField] private string id;

        public string Id => id;

        /// <summary>Stop position for rival <paramref name="index"/> (cycles through the child slots).</summary>
        public Vector3 Slot(int index) => transform.childCount == 0 ? transform.position : transform.GetChild(index % transform.childCount).position;

        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.4f, 0.9f, 0.7f);
            for (int i = 0; i < Mathf.Max(1, transform.childCount); i++) Gizmos.DrawWireSphere(Slot(i) + Vector3.up, 1.5f);
        }

#if UNITY_EDITOR
        public void EditorConfigure(string markerId) => id = markerId;
#endif
    }
}
