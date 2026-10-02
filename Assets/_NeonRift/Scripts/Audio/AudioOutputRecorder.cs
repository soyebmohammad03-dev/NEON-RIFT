using System.Threading;
using UnityEngine;

namespace NeonRift.Audio
{
    /// <summary>
    /// Development tool: records the final mix at the AudioListener (mono) into a fixed buffer, for automated checks
    /// (clicks, pitch tracking) and for saving a WAV to listen to. Add it to the listener's GameObject.
    /// </summary>
    [RequireComponent(typeof(AudioListener))]
    public sealed class AudioOutputRecorder : MonoBehaviour
    {
        private float[] buffer = new float[1];
        private int written;
        private volatile bool recording;

        public int SampleRate { get; private set; }
        public bool Recording => recording;
        public int Written => Volatile.Read(ref written);
        public float Seconds => SampleRate > 0 ? Written / (float)SampleRate : 0f;

        public void Begin(float seconds)
        {
            SampleRate = AudioSettings.outputSampleRate;
            buffer = new float[Mathf.CeilToInt(seconds * SampleRate)];
            Volatile.Write(ref written, 0);
            recording = true;
        }

        public void End() => recording = false;

        /// <summary>Copy of what has been recorded so far.</summary>
        public float[] Samples()
        {
            int n = Written;
            var copy = new float[n];
            System.Array.Copy(buffer, copy, n);
            return copy;
        }

        /// <summary>Copies the most recent <c>destination.Length</c> samples; returns false if not enough are recorded.</summary>
        public bool CopyLatest(float[] destination)
        {
            int n = Written;
            if (n < destination.Length) return false;
            System.Array.Copy(buffer, n - destination.Length, destination, 0, destination.Length);
            return true;
        }

        private void OnAudioFilterRead(float[] data, int channels)
        {
            if (!recording) return;
            int w = written;
            for (int i = 0; i + channels - 1 < data.Length && w < buffer.Length; i += channels)
            {
                float sum = 0f;
                for (int c = 0; c < channels; c++) sum += data[i + c];
                buffer[w++] = sum / channels;
            }
            Volatile.Write(ref written, w);
            if (w >= buffer.Length) recording = false;
        }
    }
}
