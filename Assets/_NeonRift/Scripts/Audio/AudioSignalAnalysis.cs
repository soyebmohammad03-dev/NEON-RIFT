using System;
using UnityEngine;

namespace NeonRift.Audio
{
    /// <summary>Signal measurements used to validate audio without listening: clicks, level, pitch, brightness.</summary>
    public static class AudioSignalAnalysis
    {
        /// <summary>RMS of a window.</summary>
        public static float Rms(float[] x, int start, int count)
        {
            double sum = 0;
            int end = Math.Min(x.Length, start + count);
            for (int i = start; i < end; i++) sum += x[i] * x[i];
            return end > start ? (float)Math.Sqrt(sum / (end - start)) : 0f;
        }

        /// <summary>
        /// Click detector: the largest second difference relative to the RMS of second differences in its window.
        /// Steady noise and tones score ~3–6; a waveform discontinuity (restart, loop seam, zipper) scores far higher.
        /// </summary>
        public static float ClickScore(float[] x, int window = 2048)
        {
            float worst = 0f;
            for (int start = 2; start + window < x.Length; start += window / 2)
            {
                double sum = 0;
                float peak = 0f;
                for (int i = start; i < start + window; i++)
                {
                    float d2 = x[i] - 2f * x[i - 1] + x[i - 2];
                    sum += d2 * d2;
                    peak = Mathf.Max(peak, Mathf.Abs(d2));
                }
                float rms = (float)Math.Sqrt(sum / window);
                if (rms > 1e-5f) worst = Mathf.Max(worst, peak / rms);
            }
            return worst;
        }

        /// <summary>Seam check for a loop: the jump across end→start relative to typical sample steps.</summary>
        public static float LoopSeamScore(float[] x)
        {
            if (x.Length < 3) return 0f;
            double sum = 0;
            for (int i = 1; i < x.Length; i++) sum += Math.Abs(x[i] - x[i - 1]);
            float mean = (float)(sum / (x.Length - 1));
            float seam = Mathf.Abs(x[0] - x[x.Length - 1]);
            return mean > 1e-6f ? seam / mean : 0f;
        }

        /// <summary>High-frequency content proxy: RMS of the first difference over RMS of the signal.</summary>
        public static float Brightness(float[] x, int start, int count)
        {
            double a = 0, d = 0;
            int end = Math.Min(x.Length, start + count);
            for (int i = Math.Max(1, start); i < end; i++)
            {
                a += x[i] * x[i];
                float diff = x[i] - x[i - 1];
                d += diff * diff;
            }
            return a > 1e-12 ? (float)Math.Sqrt(d / a) : 0f;
        }

        /// <summary>Fundamental frequency by normalised autocorrelation in [minHz, maxHz]; 0 when no clear period.</summary>
        public static float Pitch(float[] x, int start, int count, int sampleRate, float minHz, float maxHz)
        {
            int minLag = Mathf.Max(1, Mathf.FloorToInt(sampleRate / maxHz));
            int maxLag = Mathf.CeilToInt(sampleRate / minHz);
            int end = Math.Min(x.Length, start + count);
            if (end - start <= maxLag * 2) return 0f;
            float best = 0f;
            int bestLag = 0;
            for (int lag = minLag; lag <= maxLag; lag++)
            {
                double num = 0, e0 = 0, e1 = 0;
                for (int i = start; i + lag < end; i++)
                {
                    num += x[i] * x[i + lag];
                    e0 += x[i] * x[i];
                    e1 += x[i + lag] * x[i + lag];
                }
                float r = e0 > 0 && e1 > 0 ? (float)(num / Math.Sqrt(e0 * e1)) : 0f;
                if (r > best) { best = r; bestLag = lag; }
            }
            return best > 0.3f && bestLag > 0 ? (float)sampleRate / bestLag : 0f;
        }

        /// <summary>Writes mono 16-bit PCM WAV.</summary>
        public static void WriteWav(string path, float[] samples, int sampleRate)
        {
            using var stream = new System.IO.FileStream(path, System.IO.FileMode.Create);
            using var w = new System.IO.BinaryWriter(stream);
            int bytes = samples.Length * 2;
            w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
            w.Write(36 + bytes);
            w.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
            w.Write(16);
            w.Write((short)1);
            w.Write((short)1);
            w.Write(sampleRate);
            w.Write(sampleRate * 2);
            w.Write((short)2);
            w.Write((short)16);
            w.Write(System.Text.Encoding.ASCII.GetBytes("data"));
            w.Write(bytes);
            foreach (float s in samples) w.Write((short)Mathf.RoundToInt(Mathf.Clamp(s, -1f, 1f) * 32767f));
        }
    }
}
