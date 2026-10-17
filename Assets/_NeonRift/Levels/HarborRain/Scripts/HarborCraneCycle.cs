using System;
using UnityEngine;

namespace NeonRift.HarborRain
{
    /// <summary>
    /// A ship-to-shore crane re-stowing a box on the berth's ship: the spreader lowers onto the box in deck bay A,
    /// locks on (lamp green), hoists, the trolley runs back over the apron and holds the box there (for the truck that
    /// isn't coming in this storm), runs out to deck bay B, lowers, unlocks, waits, and then does it all in reverse.
    /// One axis moves at a time with eased starts and stops; every trolley stop leaves the load swinging slightly. The
    /// hoist ropes stretch between the trolley sheaves and the spreader each frame. Over the apron the box is never
    /// lower than <see cref="minClearance"/> above the ground, so it can never meet the player's car.
    /// </summary>
    public sealed class HarborCraneCycle : MonoBehaviour
    {
        [SerializeField] private Transform trolley;
        [SerializeField] private Transform spreader;
        [SerializeField] private Transform box;
        [SerializeField] private Transform[] ropes = Array.Empty<Transform>();
        [SerializeField] private Renderer lamp;
        [SerializeField] private float bayAZ = 322f, bayBZ = 342f, apronZ = 268f;
        [SerializeField] private float travelY = 36f, deckAY = 24f, deckBY = 24f;
        [SerializeField] private float minClearance = 7f;
        [SerializeField] private float trolleySpeed = 4.5f, hoistSpeed = 2.4f;
        [SerializeField] private float startDelay;

        private const float BoxBelow = 2.65f;   // spreader origin (headblock top) to the box centre
        private struct Key { public float z, y, pause; public int lockState; }   // lockState: 1 lock, -1 unlock, 0 none
        private Key[] keys;
        private int k;
        private float t, swing, swingT, duration;
        private Vector2 from, to;
        private bool carrying, paused;
        private MaterialPropertyBlock block;
        private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");

        public bool Carrying => carrying;
        /// <summary>Lowest box bottom (m above ground) seen while the box was over the apron (landward of the quay edge).</summary>
        public float MinApronClearance { get; private set; } = float.MaxValue;
        public int Moves { get; private set; }
        public float BoxBottom => box != null ? box.position.y - 1.3f : 0f;
        public float ApronZ => apronZ;

        private void OnEnable()
        {
            float holdY = minClearance + 1.3f + 1.3f + BoxBelow;
            Key K(float z, float y, float pause = 0f, int l = 0) => new() { z = z, y = y, pause = pause, lockState = l };
            keys = new[]
            {
                K(bayAZ, deckAY, 0.4f, 1), K(bayAZ, travelY), K(apronZ, travelY, 0.5f), K(apronZ, holdY, 6f), K(apronZ, travelY), K(bayBZ, travelY, 0.5f),
                K(bayBZ, deckBY, 0.4f, -1), K(bayBZ, travelY, 7f),
                K(bayBZ, deckBY, 0.4f, 1), K(bayBZ, travelY), K(apronZ, travelY, 0.5f), K(apronZ, holdY, 6f), K(apronZ, travelY), K(bayAZ, travelY, 0.5f),
                K(bayAZ, deckAY, 0.4f, -1), K(bayAZ, travelY, 7f),
            };
            // Start above bay A with the box resting on deck there.
            Pose(bayAZ, travelY);
            if (box != null) box.SetPositionAndRotation(new Vector3(spreader.position.x, deckAY - BoxBelow, bayAZ), Quaternion.identity);
            carrying = false;
            k = -1;
            t = -startDelay;
            paused = true;
            duration = 0.1f;
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            t += dt;
            if (paused)
            {
                float hold = k >= 0 ? keys[k].pause + (keys[k].lockState != 0 ? 2f : 0f) : 2f;
                if (k >= 0 && keys[k].lockState != 0 && t > 1.2f) carrying = keys[k].lockState > 0;   // locks turn part-way through the stop
                if (t >= hold) { k = (k + 1) % keys.Length; Begin(keys[k]); }
            }
            else
            {
                float u = Mathf.Clamp01(t / duration), e = u * u * (3f - 2f * u);
                var p = Vector2.Lerp(from, to, e);
                Pose(p.x, p.y);
                if (u >= 1f)
                {
                    if (Mathf.Abs(to.x - from.x) > 0.1f) { swing = carrying ? 1.8f : 2.6f; swingT = 0f; }
                    paused = true;
                    t = 0f;
                }
            }
            swingT += dt;
            float sway = Mathf.Sin(swingT * 1.9f) * swing * Mathf.Exp(-swingT * 0.6f);
            if (spreader != null) spreader.localRotation = Quaternion.Euler(sway, 0f, 0f);
            if (trolley != null && spreader != null)
                foreach (var r in ropes)
                    if (r != null) r.localScale = new Vector3(1f, Mathf.Max(0.5f, trolley.position.y - spreader.position.y), 1f);
            if (box != null && carrying)
            {
                box.SetPositionAndRotation(spreader.position - spreader.up * BoxBelow, spreader.rotation);
                if (box.position.z < 300f) MinApronClearance = Mathf.Min(MinApronClearance, box.position.y - 1.3f);
            }
            if (lamp != null)
            {
                block ??= new MaterialPropertyBlock();
                lamp.GetPropertyBlock(block);
                bool flash = Mathf.Repeat(Time.time * 2f, 1f) < 0.5f;
                bool locking = paused && k >= 0 && keys[k].lockState != 0;
                block.SetColor(EmissionColor, locking ? new Color(0.3f, 5f, 0.8f) : !paused && flash ? new Color(5f, 2.6f, 0.2f) : new Color(0.6f, 0.3f, 0.02f));
                lamp.SetPropertyBlock(block);
            }
        }

        private void Begin(Key key)
        {
            from = new Vector2(trolley.position.z, spreader.position.y);
            to = new Vector2(key.z, key.y);
            float speed = Mathf.Abs(to.x - from.x) > 0.1f ? trolleySpeed : hoistSpeed;
            duration = Mathf.Max(0.2f, Vector2.Distance(from, to) / speed);
            paused = false;
            t = 0f;
            Moves++;
        }

        private void Pose(float z, float y)
        {
            if (trolley != null) { var p = trolley.position; trolley.position = new Vector3(p.x, p.y, z); }
            if (spreader != null) { var p = spreader.position; spreader.position = new Vector3(p.x, y, z); }
        }
    }
}
