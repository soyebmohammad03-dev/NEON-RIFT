using UnityEngine;

namespace NeonRift.Gameplay
{
    /// <summary>
    /// North-up minimap projection: world XZ around a centre point maps to panel pixels with +Z (north) pointing up
    /// and +X (east) pointing right. The map never rotates; only the player marker turns with
    /// <see cref="MarkerRotation"/>. Pure math so it can be unit tested without a panel.
    /// </summary>
    public readonly struct MinimapProjection
    {
        public readonly Vector3 Centre;
        public readonly Vector2 Size;
        /// <summary>Metres from the centre to the map's edge.</summary>
        public readonly float Range;

        public MinimapProjection(Vector3 centre, Vector2 size, float range)
        {
            Centre = centre;
            Size = size;
            Range = Mathf.Max(1f, range);
        }

        /// <summary>Pixels per metre.</summary>
        public float Scale => Size.x * 0.5f / Range;

        /// <summary>World position to panel pixels (panel y grows downwards, so north is −y).</summary>
        public Vector2 ToMap(Vector3 world)
        {
            float s = Scale;
            return Size * 0.5f + new Vector2((world.x - Centre.x) * s, -(world.z - Centre.z) * s);
        }

        /// <summary>Panel pixels back to world XZ (y = centre height).</summary>
        public Vector3 ToWorld(Vector2 map)
        {
            float s = Scale;
            var d = map - Size * 0.5f;
            return new Vector3(Centre.x + d.x / s, Centre.y, Centre.z - d.y / s);
        }

        /// <summary>
        /// Compass heading in degrees (0 = north/+Z, 90 = east/+X, clockwise) of a world-space forward vector.
        /// </summary>
        public static float Heading(Vector3 forward)
        {
            var flat = new Vector3(forward.x, 0f, forward.z);
            if (flat.sqrMagnitude < 1e-8f) return 0f;
            return Mathf.Repeat(Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg, 360f);
        }

        /// <summary>
        /// Rotates a marker-local offset (drawn pointing up, −y) by a compass heading so it points the same way as
        /// the vehicle on the north-up map. Clockwise on screen for a positive heading.
        /// </summary>
        public static Vector2 MarkerRotation(Vector2 local, float headingDegrees)
        {
            float r = headingDegrees * Mathf.Deg2Rad;
            float c = Mathf.Cos(r), s = Mathf.Sin(r);
            // Screen space has y down, so a clockwise turn is (x, y) → (x·c − y·s, x·s + y·c).
            return new Vector2(local.x * c - local.y * s, local.x * s + local.y * c);
        }
    }
}
