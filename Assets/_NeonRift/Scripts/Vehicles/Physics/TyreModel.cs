using UnityEngine;

namespace NeonRift.Vehicles
{
    /// <summary>
    /// Combined-slip tyre force. Longitudinal and lateral slip are normalised by their peak values and combined into one
    /// slip magnitude, so braking or accelerating while cornering trades lateral grip for longitudinal grip (friction ellipse).
    /// </summary>
    public static class TyreModel
    {
        /// <summary>Normalised force for normalised slip <paramref name="s"/>: 0 at 0, 1 at 1 (peak), decaying toward the sliding ratio.</summary>
        public static float Curve(float s, float slidingGripRatio, float breakawaySharpness)
        {
            if (s <= 1f) return Mathf.Sin(s * Mathf.PI * 0.5f);
            float x = (s - 1f) * breakawaySharpness;
            return slidingGripRatio + (1f - slidingGripRatio) * Mathf.Exp(-x * x);
        }

        /// <summary>Grip multiplier from load sensitivity: more load than static gives proportionally less friction.</summary>
        public static float LoadFactor(in TyreSettings tyre, float load, float staticLoad)
        {
            if (staticLoad <= 0f) return 1f;
            return Mathf.Clamp(1f - tyre.loadSensitivity * (load / staticLoad - 1f), 0.5f, 1.3f);
        }

        /// <summary>Normalised combined slip (1 = at the peak).</summary>
        public static float CombinedSlip(in TyreSettings tyre, float slipRatio, float slipAngleTangent)
        {
            float sx = slipRatio / tyre.peakSlipRatio;
            float sy = slipAngleTangent / Mathf.Tan(tyre.peakSlipAngle * Mathf.Deg2Rad);
            return Mathf.Sqrt(sx * sx + sy * sy);
        }

        /// <summary>
        /// Tyre force in the contact frame: x = longitudinal (+ forward), y = lateral (+ right).
        /// </summary>
        /// <param name="slipRatio">(wheel surface speed − ground speed) / reference speed.</param>
        /// <param name="slipAngleTangent">Lateral / longitudinal ground speed (tangent of the slip angle; + = sliding right).</param>
        /// <param name="load">Normal load, N.</param>
        /// <param name="staticLoad">Static normal load used for load sensitivity, N.</param>
        /// <param name="surfaceGrip">Surface friction relative to reference asphalt.</param>
        public static Vector2 Force(in TyreSettings tyre, float slipRatio, float slipAngleTangent, float load, float staticLoad, float surfaceGrip)
        {
            if (load <= 0f) return Vector2.zero;
            float sx = slipRatio / tyre.peakSlipRatio;
            float sy = slipAngleTangent / Mathf.Tan(tyre.peakSlipAngle * Mathf.Deg2Rad);
            float s = Mathf.Sqrt(sx * sx + sy * sy);
            if (s < 1e-6f) return Vector2.zero;
            float f = Curve(s, tyre.slidingGripRatio, tyre.breakawaySharpness) * LoadFactor(tyre, load, staticLoad) * surfaceGrip * load / s;
            return new Vector2(tyre.longitudinalGrip * f * sx, -tyre.lateralGrip * f * sy);
        }

        /// <summary>Peak longitudinal force the tyre can give with its current lateral force in use, N.</summary>
        public static float AvailableLongitudinal(in TyreSettings tyre, float lateralForce, float load, float staticLoad, float surfaceGrip)
        {
            float peak = LoadFactor(tyre, load, staticLoad) * surfaceGrip * load;
            float maxLat = tyre.lateralGrip * peak;
            if (maxLat <= 0f) return 0f;
            float used = Mathf.Clamp01(Mathf.Abs(lateralForce) / maxLat);
            return tyre.longitudinalGrip * peak * Mathf.Sqrt(1f - used * used);
        }

        /// <summary>Scales (fx, fy) back onto the friction ellipse if it lies outside.</summary>
        public static Vector2 ClampToEllipse(in TyreSettings tyre, Vector2 force, float load, float staticLoad, float surfaceGrip)
        {
            float peak = LoadFactor(tyre, load, staticLoad) * surfaceGrip * load;
            if (peak <= 0f) return Vector2.zero;
            float nx = force.x / (tyre.longitudinalGrip * peak);
            float ny = force.y / (tyre.lateralGrip * peak);
            float n = nx * nx + ny * ny;
            return n > 1f ? force / Mathf.Sqrt(n) : force;
        }
    }
}
