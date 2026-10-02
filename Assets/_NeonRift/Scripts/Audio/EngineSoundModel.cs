using System;
using System.Collections.Generic;
using NeonRift.Vehicles;
using UnityEngine;

namespace NeonRift.Audio
{
    /// <summary>
    /// Turns engine speed and load into a volume and pitch for every loop of an <see cref="EngineAudioSettings"/>.
    /// Neighbouring rpm layers are crossfaded with an equal-power law in log-rpm space; on- and off-load sets are
    /// crossfaded by load. Layers that would need pitching beyond the limits fade out instead of stretching.
    /// Pure and allocation-free per call, so it is unit-testable and cheap enough for mobile.
    /// </summary>
    public sealed class EngineSoundModel
    {
        private readonly EngineSoundLayer[] layers;
        private readonly int[] onLayers;
        private readonly int[] offLayers;
        private readonly float minPitch;
        private readonly float maxPitch;

        public int LayerCount => layers.Length;
        public EngineSoundLayer Layer(int index) => layers[index];

        public EngineSoundModel(in EngineAudioSettings settings)
        {
            layers = settings.layers ?? Array.Empty<EngineSoundLayer>();
            minPitch = settings.minPitch > 0f ? settings.minPitch : 0.5f;
            maxPitch = settings.maxPitch > 1f ? settings.maxPitch : 2f;
            onLayers = Sorted(true);
            offLayers = Sorted(false);
        }

        private int[] Sorted(bool onLoad)
        {
            var list = new List<int>();
            for (int i = 0; i < layers.Length; i++) if (layers[i].onLoad == onLoad && layers[i].recordedRpm > 0f) list.Add(i);
            list.Sort((a, b) => layers[a].recordedRpm.CompareTo(layers[b].recordedRpm));
            return list.ToArray();
        }

        /// <param name="rpm">Engine speed, rpm.</param>
        /// <param name="load">0 = overrun, 1 = full load.</param>
        /// <param name="volumes">Receives a linear gain per layer (length ≥ <see cref="LayerCount"/>).</param>
        /// <param name="pitches">Receives a playback pitch per layer.</param>
        public void Evaluate(float rpm, float load, float[] volumes, float[] pitches)
        {
            Array.Clear(volumes, 0, layers.Length);
            load = Mathf.Clamp01(load);
            rpm = Mathf.Max(1f, rpm);
            // Equal-power load crossfade; if one set is missing the other carries everything.
            float onGain = offLayers.Length == 0 ? 1f : onLayers.Length == 0 ? 0f : Mathf.Sin(load * Mathf.PI * 0.5f);
            float offGain = offLayers.Length == 0 ? 0f : onLayers.Length == 0 ? 1f : Mathf.Cos(load * Mathf.PI * 0.5f);
            Blend(onLayers, rpm, onGain, volumes);
            Blend(offLayers, rpm, offGain, volumes);

            for (int i = 0; i < layers.Length; i++)
            {
                float pitch = rpm / layers[i].recordedRpm;
                // Fade a layer out over the last 15 % before a pitch limit rather than clamping audibly.
                float fade = Mathf.Clamp01((pitch - minPitch) / (minPitch * 0.15f)) * Mathf.Clamp01((maxPitch - pitch) / (maxPitch * 0.15f));
                pitches[i] = Mathf.Clamp(pitch, minPitch, maxPitch);
                volumes[i] *= fade * (layers[i].volume > 0f ? layers[i].volume : 1f);
            }
        }

        private void Blend(int[] set, float rpm, float gain, float[] volumes)
        {
            if (set.Length == 0 || gain <= 0f) return;
            float lowest = layers[set[0]].recordedRpm, highest = layers[set[set.Length - 1]].recordedRpm;
            if (set.Length == 1 || rpm <= lowest) { volumes[set[0]] += gain; return; }
            if (rpm >= highest) { volumes[set[set.Length - 1]] += gain; return; }
            for (int k = 0; k < set.Length - 1; k++)
            {
                float a = layers[set[k]].recordedRpm, b = layers[set[k + 1]].recordedRpm;
                if (rpm < a || rpm > b) continue;
                float t = Mathf.InverseLerp(Mathf.Log(a), Mathf.Log(b), Mathf.Log(rpm));
                volumes[set[k]] += gain * Mathf.Cos(t * Mathf.PI * 0.5f);
                volumes[set[k + 1]] += gain * Mathf.Sin(t * Mathf.PI * 0.5f);
                return;
            }
        }
    }
}
