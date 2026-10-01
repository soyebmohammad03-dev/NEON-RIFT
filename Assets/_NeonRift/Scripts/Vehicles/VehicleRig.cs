using System;
using System.Collections.Generic;
using UnityEngine;

namespace NeonRift.Vehicles
{
    public enum WheelPosition
    {
        FrontLeft,
        FrontRight,
        RearLeft,
        RearRight
    }

    /// <summary>
    /// Visual wheel set-up. <see cref="Pivot"/> sits at the wheel centre and is steered about local Y;
    /// <see cref="Spin"/> rotates about local X. Static parts (calipers) are children of the pivot only.
    /// </summary>
    [Serializable]
    public struct WheelRig
    {
        public WheelPosition Position;
        public Transform Pivot;
        public Transform Spin;
        [Min(0f)] public float Radius;
        [Min(0f)] public float Width;
        [Tooltip("Motion-blurred rim variants, hidden at rest.")]
        public Renderer[] MotionBlurRenderers;

        public bool IsFront => Position == WheelPosition.FrontLeft || Position == WheelPosition.FrontRight;
        public bool IsLeft => Position == WheelPosition.FrontLeft || Position == WheelPosition.RearLeft;
    }

    /// <summary>
    /// Describes the visual structure of a vehicle prefab (body, wheels, lamps) so physics, lighting
    /// and presentation systems can drive it without knowing the source model's hierarchy.
    /// Root pivot: ground level, centred, +Z forward, metres.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VehicleRig : MonoBehaviour
    {
        [SerializeField] private Transform body;
        [SerializeField] private WheelRig[] wheels = Array.Empty<WheelRig>();
        [SerializeField] private Renderer[] headlightRenderers = Array.Empty<Renderer>();
        [SerializeField] private Renderer[] taillightRenderers = Array.Empty<Renderer>();
        [SerializeField] private Vector3 dimensions;

        public Transform Body => body;
        public IReadOnlyList<WheelRig> Wheels => wheels;
        public IReadOnlyList<Renderer> HeadlightRenderers => headlightRenderers;
        public IReadOnlyList<Renderer> TaillightRenderers => taillightRenderers;
        /// <summary>Overall width, height, length in metres.</summary>
        public Vector3 Dimensions => dimensions;

        public float Wheelbase
        {
            get
            {
                float front = 0f, rear = 0f;
                int nf = 0, nr = 0;
                foreach (var w in wheels)
                {
                    if (w.Pivot == null) continue;
                    if (w.IsFront) { front += w.Pivot.localPosition.z; nf++; }
                    else { rear += w.Pivot.localPosition.z; nr++; }
                }
                return nf > 0 && nr > 0 ? front / nf - rear / nr : 0f;
            }
        }

#if UNITY_EDITOR
        public void EditorConfigure(Transform bodyTransform, WheelRig[] wheelRigs, Renderer[] headlights, Renderer[] taillights, Vector3 size)
        {
            body = bodyTransform;
            wheels = wheelRigs;
            headlightRenderers = headlights;
            taillightRenderers = taillights;
            dimensions = size;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            foreach (var w in wheels)
                if (w.Pivot != null)
                    Gizmos.DrawWireSphere(w.Pivot.position, w.Radius);
        }
#endif
    }
}
