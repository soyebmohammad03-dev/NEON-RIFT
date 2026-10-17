using System;
using Unity.Cinemachine;
using UnityEngine;

namespace NeonRift.HarborRain
{
    /// <summary>
    /// Short directed camera moves on a dedicated <see cref="CinemachineCamera"/> that outranks the chase camera while a
    /// shot plays (the brain blends in and out). Shots are a few key poses eased over a few seconds, always skippable,
    /// and only used where the player is not driving against the clock (the arrival reveal, the extraction). In-play
    /// beats (theft, roadblock, wave impact) use the chase camera's own Focus / CinematicBeat / Kick instead.
    /// </summary>
    public sealed class HarborCinematics : MonoBehaviour
    {
        [Serializable]
        public struct Key { public Vector3 position; public Vector3 lookAt; }

        [Serializable]
        public struct Shot
        {
            public string name;
            public Key[] keys;
            public float seconds;
        }

        [SerializeField] private CinemachineCamera shotCamera;
        [SerializeField] private Shot[] shots = Array.Empty<Shot>();

        public bool Playing => current >= 0;
        public string CurrentShot => current >= 0 ? shots[current].name : null;
        public int ShotCount => shots.Length;
        private int current = -1;
        private float t;
        private Action onDone;

        private void Awake()
        {
            if (shotCamera != null) shotCamera.gameObject.SetActive(false);
        }

        public bool Play(string name, Action done = null)
        {
            for (int i = 0; i < shots.Length; i++)
            {
                if (shots[i].name != name || shots[i].keys == null || shots[i].keys.Length < 2) continue;
                current = i;
                t = 0f;
                onDone = done;
                if (shotCamera != null)
                {
                    Pose(0f);
                    shotCamera.Priority = 50;
                    shotCamera.gameObject.SetActive(true);
                }
                return true;
            }
            done?.Invoke();
            return false;
        }

        /// <summary>Ends the current shot early (Interact / tap) and hands back to the chase camera.</summary>
        public void Skip()
        {
            if (current < 0) return;
            Finish();
        }

        private void Finish()
        {
            current = -1;
            if (shotCamera != null) shotCamera.gameObject.SetActive(false);
            var done = onDone;
            onDone = null;
            done?.Invoke();
        }

        private void Pose(float u)
        {
            var keys = shots[current].keys;
            float f = Mathf.Clamp01(u) * (keys.Length - 1);
            int i = Mathf.Min(keys.Length - 2, Mathf.FloorToInt(f));
            float k = f - i;
            k = k * k * (3f - 2f * k);
            var pos = Vector3.Lerp(keys[i].position, keys[i + 1].position, k);
            var look = Vector3.Lerp(keys[i].lookAt, keys[i + 1].lookAt, k);
            shotCamera.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(look - pos, Vector3.up));
        }

        private void LateUpdate()
        {
            if (current < 0 || shotCamera == null) return;
            t += Time.unscaledDeltaTime;
            float u = t / Mathf.Max(0.1f, shots[current].seconds);
            Pose(u);
            if (u >= 1f) Finish();
        }
    }
}
