using UnityEngine;

namespace NeonRift.Vehicles
{
    /// <summary>
    /// Tyre-relevant properties of a drivable collider (on the collider or a parent). Colliders without one count as
    /// dry asphalt.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DrivingSurface : MonoBehaviour
    {
        [Tooltip("Tyre grip relative to dry asphalt (1). Wet asphalt ≈ 0.7, grass ≈ 0.55, gravel ≈ 0.45.")]
        [SerializeField, Range(0.05f, 1.5f)] private float grip = 1f;
        [Tooltip("Multiplies tyre rolling resistance. Grass ≈ 4, gravel ≈ 10.")]
        [SerializeField, Range(1f, 40f)] private float rollingResistanceScale = 1f;

        public float Grip => grip;
        public float RollingResistanceScale => rollingResistanceScale;

#if UNITY_EDITOR
        public void EditorConfigure(float surfaceGrip, float rollingScale)
        {
            grip = surfaceGrip;
            rollingResistanceScale = rollingScale;
        }
#endif
    }
}
