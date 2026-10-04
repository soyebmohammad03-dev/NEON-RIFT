using UnityEngine;

namespace NeonRift.Audio
{
    /// <summary>
    /// Final-stage safety limiter on the AudioListener: the summed mix never exceeds the ceiling (no digital clipping
    /// when sirens, a klaxon, the engine at the limiter and the lockdown score coincide). Instant attack on the peak
    /// of each frame across channels, smooth exponential release. Gain staging keeps it idle most of the time; it
    /// only catches the moments everything lines up.
    /// </summary>
    [RequireComponent(typeof(AudioListener))]
    public sealed class MasterLimiter : MonoBehaviour
    {
        [Tooltip("Output ceiling, dBFS.")]
        [SerializeField, Range(-12f, 0f)] private float ceilingDb = -1f;
        [Tooltip("Time to recover from gain reduction, ms.")]
        [SerializeField, Range(10f, 1000f)] private float releaseMs = 150f;

        private float gain = 1f, ceiling = 0.89f, release = 0.999f;
        private volatile float minGain = 1f;

        /// <summary>The deepest gain reduction since the last read, dB (0 = untouched). Resets when read.</summary>
        public float TakeReductionDb()
        {
            float g = minGain;
            minGain = 1f;
            return 20f * Mathf.Log10(Mathf.Max(g, 1e-4f));
        }

        private void OnEnable()
        {
            ceiling = Mathf.Pow(10f, ceilingDb / 20f);
            release = ReleaseCoefficient(releaseMs, AudioSettings.outputSampleRate);
            gain = 1f;
        }

        private void OnAudioFilterRead(float[] data, int channels)
        {
            float lowest = Process(data, channels, ref gain, ceiling, release);
            if (lowest < minGain) minGain = lowest;
        }

        /// <summary>Per-sample release factor for a time constant of <paramref name="ms"/> at <paramref name="sampleRate"/>.</summary>
        public static float ReleaseCoefficient(float ms, int sampleRate) =>
            Mathf.Exp(-1f / Mathf.Max(1f, ms * 0.001f * Mathf.Max(1, sampleRate)));

        /// <summary>
        /// Limits interleaved <paramref name="data"/> in place to <paramref name="ceiling"/>; <paramref name="gain"/>
        /// carries the state between buffers. Returns the lowest gain applied in this buffer.
        /// </summary>
        public static float Process(float[] data, int channels, ref float gain, float ceiling, float release)
        {
            float lowest = 1f;
            channels = Mathf.Max(1, channels);
            for (int i = 0; i + channels - 1 < data.Length; i += channels)
            {
                float peak = 0f;
                for (int c = 0; c < channels; c++)
                {
                    float a = Mathf.Abs(data[i + c]);
                    if (a > peak) peak = a;
                }
                float target = peak > ceiling ? ceiling / peak : 1f;
                // Instant attack, exponential release back toward unity.
                gain = target < gain ? target : Mathf.Min(target, 1f - (1f - gain) * release);
                for (int c = 0; c < channels; c++) data[i + c] *= gain;
                if (gain < lowest) lowest = gain;
            }
            return lowest;
        }
    }
}
