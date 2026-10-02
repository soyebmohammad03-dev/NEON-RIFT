using UnityEngine;

namespace NeonRift.Audio
{
    /// <summary>Maps tyre state to sound intensities (0..1). Pure; shared by every car.</summary>
    public static class TyreSoundModel
    {
        /// <summary>Squeal or scrub intensity for one tyre from its normalised combined slip (1 = peak grip).</summary>
        public static float SlipIntensity(float combinedSlip, Vector2 slipRange, float loadRatio, float speed)
        {
            float slip = Smooth(slipRange.x, slipRange.y, combinedSlip);
            // A barely loaded tyre makes little noise; a stationary one none (no squeal from wheelspin regularisation at rest).
            float load = Mathf.Clamp01(loadRatio);
            float moving = Smooth(1.5f, 6f, speed);
            return slip * load * moving;
        }

        /// <summary>
        /// Share of squeal for a tyre's slip direction: full when sliding sideways, reduced when the slip is purely
        /// longitudinal (straight-line lock-up or wheelspin, which sound more like scrub).
        /// </summary>
        public static float SquealDirectionWeight(float slipRatio, float slipAngleDegrees)
        {
            float lateral = Mathf.Abs(Mathf.Tan(Mathf.Clamp(slipAngleDegrees, -80f, 80f) * Mathf.Deg2Rad)) / 0.105f; // ≈ 6° peak
            float longitudinal = Mathf.Abs(slipRatio) / 0.1f;
            float total = lateral + longitudinal;
            float share = total > 1e-4f ? lateral / total : 0f;
            return 0.3f + 0.7f * share;
        }

        public static float Smooth(float edge0, float edge1, float x)
        {
            if (edge1 <= edge0) return x >= edge1 ? 1f : 0f;
            float t = Mathf.Clamp01((x - edge0) / (edge1 - edge0));
            return t * t * (3f - 2f * t);
        }
    }
}
