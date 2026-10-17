using NeonRift.Vehicles;
using UnityEngine;

namespace NeonRift.HarborRain
{
    /// <summary>Road spray behind a vehicle: emission follows speed, much heavier while driving through floodwater.</summary>
    public sealed class VehicleSpray : MonoBehaviour
    {
        private ParticleSystem spray;
        private VehicleController vehicle;
        private AudioSource hiss;

        /// <summary>Set each frame by the mission: 0 = wet road, 1 = deep water.</summary>
        public float WaterDepth { get; set; }

        public static VehicleSpray Attach(VehicleController target, ParticleSystem prefab, AudioClip sprayClip = null, UnityEngine.Audio.AudioMixerGroup group = null)
        {
            if (target == null || prefab == null) return null;
            var spray = Instantiate(prefab, target.transform);
            spray.transform.localPosition = new Vector3(0f, 0.25f, -1.9f);
            spray.transform.localRotation = Quaternion.Euler(-25f, 180f, 0f);
            var c = spray.gameObject.AddComponent<VehicleSpray>();
            c.spray = spray;
            c.vehicle = target;
            if (sprayClip != null)
            {
                // Wet-tyre hiss: 3D at the car, louder with speed and standing water.
                c.hiss = spray.gameObject.AddComponent<AudioSource>();
                c.hiss.clip = sprayClip;
                c.hiss.loop = true;
                c.hiss.spatialBlend = 0.85f;
                c.hiss.minDistance = 4f;
                c.hiss.maxDistance = 60f;
                c.hiss.volume = 0f;
                c.hiss.outputAudioMixerGroup = group;
                c.hiss.Play();
            }
            return c;
        }

        private void Update()
        {
            if (vehicle == null || vehicle.Body == null) return;
            float speed = vehicle.Body.linearVelocity.magnitude;
            bool grounded = false;
            foreach (var w in vehicle.Wheels) grounded |= w.IsGrounded;
            float rate = grounded && speed > 2.5f ? speed * Mathf.Lerp(2.5f, 14f, WaterDepth) : 0f;
            var e = spray.emission;
            e.rateOverTime = rate;
            var main = spray.main;
            main.startSpeedMultiplier = Mathf.Lerp(1.5f, 5f, WaterDepth) + speed * 0.05f;
            if (hiss != null)
            {
                float target = grounded ? Mathf.Clamp01(speed / 30f) * Mathf.Lerp(0.45f, 1f, WaterDepth) : 0f;
                hiss.volume = Mathf.MoveTowards(hiss.volume, target, Time.deltaTime * 2f);
                hiss.pitch = 0.8f + Mathf.Clamp01(speed / 40f) * 0.5f;
            }
        }
    }
}
