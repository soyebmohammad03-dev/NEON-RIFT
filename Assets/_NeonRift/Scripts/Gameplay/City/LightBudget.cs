using System;
using System.Collections.Generic;
using UnityEngine;

namespace NeonRift.Gameplay
{
    /// <summary>
    /// Keeps a city's worth of lamp lights affordable: every lamp has a (disabled) real-time light, and only the
    /// <see cref="maxActive"/> nearest the viewer (with a preference for what is in front of it) are switched on, fading in
    /// and out so nothing pops. Distant streets read through emissive heads, ground pools and haze. One update every
    /// <see cref="interval"/>; the per-frame cost is only the fading lights.
    /// </summary>
    public sealed class LightBudget : MonoBehaviour
    {
        [SerializeField] private Light[] lights = Array.Empty<Light>();
        [Tooltip("Whose position decides which lights are on (the gameplay camera).")]
        [SerializeField] private Transform viewer;
        [SerializeField, Range(4, 256)] private int maxActive = 56;
        [SerializeField, Min(10f)] private float maxDistance = 150f;
        [Tooltip("Lights behind the viewer count as this much further away.")]
        [SerializeField, Min(1f)] private float behindPenalty = 2.2f;
        [SerializeField, Min(0.05f)] private float interval = 0.2f;
        [SerializeField, Min(0.01f)] private float fadeSeconds = 0.6f;

        private float[] baseIntensity;
        private float[] level;
        private bool[] wanted;
        private readonly List<int> fading = new();
        private readonly List<(float score, int index)> candidates = new();
        private float nextSelect;

        public int ActiveCount { get; private set; }
        public int Count => lights.Length;

        private void Awake()
        {
            baseIntensity = new float[lights.Length];
            level = new float[lights.Length];
            wanted = new bool[lights.Length];
            for (int i = 0; i < lights.Length; i++)
            {
                if (lights[i] == null) continue;
                baseIntensity[i] = lights[i].intensity;
                lights[i].intensity = 0f;
                lights[i].enabled = false;
            }
        }

        public void SetViewer(Transform target) => viewer = target;

        private void Update()
        {
            if (viewer == null) return;
            if (Time.unscaledTime >= nextSelect)
            {
                nextSelect = Time.unscaledTime + interval;
                Select();
            }
            float step = Time.deltaTime / fadeSeconds;
            for (int k = fading.Count - 1; k >= 0; k--)
            {
                int i = fading[k];
                level[i] = Mathf.MoveTowards(level[i], wanted[i] ? 1f : 0f, step);
                var l = lights[i];
                l.intensity = baseIntensity[i] * level[i];
                l.enabled = level[i] > 0f;
                if (level[i] <= 0f || level[i] >= 1f) fading.RemoveAt(k);
            }
        }

        private void Select()
        {
            Vector3 p = viewer.position, f = viewer.forward;
            float maxSq = maxDistance * maxDistance;
            candidates.Clear();
            for (int i = 0; i < lights.Length; i++)
            {
                var l = lights[i];
                if (l == null) continue;
                Vector3 d = l.transform.position - p;
                float sq = d.sqrMagnitude;
                if (sq > maxSq) continue;
                if (Vector3.Dot(d, f) < 0f) sq *= behindPenalty * behindPenalty;
                candidates.Add((sq, i));
            }
            candidates.Sort((a, b) => a.score.CompareTo(b.score));
            for (int i = 0; i < wanted.Length; i++) SetWanted(i, false);
            int n = Mathf.Min(maxActive, candidates.Count);
            for (int k = 0; k < n; k++) SetWanted(candidates[k].index, true);
            ActiveCount = n;
        }

        private void SetWanted(int i, bool on)
        {
            if (wanted[i] == on) return;
            wanted[i] = on;
            if (!fading.Contains(i)) fading.Add(i);
        }

#if UNITY_EDITOR
        public void EditorConfigure(Light[] budgetLights, Transform view, int active)
        {
            lights = budgetLights ?? Array.Empty<Light>();
            viewer = view;
            maxActive = active;
        }
#endif
    }
}
