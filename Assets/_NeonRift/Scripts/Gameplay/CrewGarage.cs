using System;
using UnityEngine;

namespace NeonRift.Gameplay
{
    /// <summary>
    /// The crew's garage in the city (the same building as Car Select): its lights come on in banks, its roller door
    /// lifts and closes. Used by the intro; dark and shut during missions (its lights are switched off so they never
    /// cost anything or spill through the walls).
    /// </summary>
    public sealed class CrewGarage : MonoBehaviour
    {
        [Serializable]
        public struct Bank
        {
            public Light[] lights;
        }

        [SerializeField] private Transform turntable;
        [SerializeField] private Transform door;
        [SerializeField] private float doorTravel = 5.4f;
        [SerializeField, Min(0.1f)] private float doorSeconds = 3f;
        [Tooltip("Lights in the order they come on (fills, washers, stage).")]
        [SerializeField] private Bank[] banks = Array.Empty<Bank>();
        [SerializeField] private Renderer[] strips = Array.Empty<Renderer>();
        [SerializeField] private Transform[] introSlots = Array.Empty<Transform>();
        [SerializeField] private AudioSource speaker;
        [SerializeField] private AudioClip lightClunk;
        [SerializeField] private AudioClip doorMotor;

        private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
        private float[] intensities = Array.Empty<float>();
        private Light[] all = Array.Empty<Light>();
        private Vector3 doorClosed;
        private float doorTarget, doorOpen, lightsOnAt = -1f;
        private Material stripMaterial;
        private Color stripColour;

        public Transform Turntable => turntable;
        /// <summary>Intro slots: [0] the hero on the turntable, then the two bays (facing the door).</summary>
        public Transform[] IntroSlots => introSlots;
        public bool DoorOpen => doorOpen > 0.99f;

        private void Awake()
        {
            var list = new System.Collections.Generic.List<Light>();
            foreach (var b in banks) if (b.lights != null) list.AddRange(b.lights);
            all = list.ToArray();
            intensities = new float[all.Length];
            for (int i = 0; i < all.Length; i++) if (all[i] != null) intensities[i] = all[i].intensity;
            if (door != null) doorClosed = door.localPosition;
            if (strips.Length > 0 && strips[0] != null)
            {
                stripMaterial = new Material(strips[0].sharedMaterial) { name = strips[0].sharedMaterial.name + " (runtime)" };
                stripColour = stripMaterial.GetColor(EmissionColor);
                foreach (var r in strips) if (r != null) r.sharedMaterial = stripMaterial;
            }
            ResetForMission();
        }

        private void OnDestroy()
        {
            if (stripMaterial != null) Destroy(stripMaterial);
        }

        /// <summary>Dark, door shut (the mission state).</summary>
        public void ResetForMission()
        {
            lightsOnAt = -1f;
            foreach (var l in all) if (l != null) l.enabled = false;
            if (stripMaterial != null) stripMaterial.SetColor(EmissionColor, Color.black);
            doorTarget = doorOpen = 0f;
            PlaceDoor();
            enabled = false;
        }

        /// <summary>Switches the banks on one after another over <paramref name="seconds"/> (a clunk each).</summary>
        public void LightsOn(float seconds = 2.4f)
        {
            lightsOnAt = Time.time;
            lightSeconds = seconds;
            lastBank = -1;
            enabled = true;
        }

        /// <summary>Everything on at once (skipping).</summary>
        public void LightsOnImmediate()
        {
            lightsOnAt = Time.time - 100f;
            lightSeconds = 0.01f;
            enabled = true;
            Update();
        }

        public void OpenDoor(bool immediate = false)
        {
            doorTarget = 1f;
            if (immediate) { doorOpen = 1f; PlaceDoor(); }
            else if (speaker != null && doorMotor != null) speaker.PlayOneShot(doorMotor, 0.8f);
            enabled = true;
        }

        private float lightSeconds = 2.4f;
        private int lastBank = -1;

        private void Update()
        {
            if (lightsOnAt >= 0f)
            {
                float t = (Time.time - lightsOnAt) / Mathf.Max(0.01f, lightSeconds);
                int index = 0;
                for (int b = 0; b < banks.Length; b++)
                {
                    float on = Mathf.Clamp01((t - b / (float)banks.Length) * banks.Length * 3f);
                    // Fluorescent strike: a flicker before settling.
                    float flicker = on > 0f && on < 1f ? (Mathf.Repeat(Time.time * 23f + b, 1f) < 0.5f ? 0.3f : 1f) : 1f;
                    foreach (var l in banks[b].lights)
                    {
                        if (l != null)
                        {
                            l.enabled = on > 0f;
                            l.intensity = intensities[index] * on * flicker;
                        }
                        index++;
                    }
                    if (on > 0f && b > lastBank)
                    {
                        lastBank = b;
                        if (speaker != null && lightClunk != null) speaker.PlayOneShot(lightClunk, 0.7f);
                    }
                }
                if (stripMaterial != null) stripMaterial.SetColor(EmissionColor, stripColour * Mathf.Clamp01(t * 1.5f));
            }
            doorOpen = Mathf.MoveTowards(doorOpen, doorTarget, Time.deltaTime / doorSeconds);
            PlaceDoor();
        }

        private void PlaceDoor()
        {
            if (door == null) return;
            float e = doorOpen * doorOpen * (3f - 2f * doorOpen);
            door.localPosition = doorClosed + Vector3.up * (doorTravel * e);
        }

#if UNITY_EDITOR
        public void EditorConfigure(Transform table, Transform rollerDoor, float travel, Bank[] lightBanks, Renderer[] stripRenderers, Transform[] slots,
                                    AudioSource audio, AudioClip clunk, AudioClip motor)
        {
            turntable = table;
            door = rollerDoor;
            doorTravel = travel;
            banks = lightBanks;
            strips = stripRenderers;
            introSlots = slots;
            speaker = audio;
            lightClunk = clunk;
            doorMotor = motor;
        }
#endif
    }
}
