using System;
using UnityEngine;

namespace NeonRift.Gameplay
{
    /// <summary>
    /// The physical locks and status display of a hackable gate (or door, bridge, lift …). World events from the hack
    /// drive it: the status lamps go from sealed red to a pulsing amber while a hack runs, flash red on a lockout,
    /// turn green as the bolts draw back into the post with a ratchet and a pneumatic sigh, then the barrier itself
    /// opens. A lockdown re-seals: the bolts shoot home once the panels have closed. Presentation only — the
    /// <see cref="SecurityBarrier"/> owns the panels and collision.
    /// </summary>
    public sealed class GateLockSystem : MonoBehaviour, IMissionWorldComponent
    {
        private enum State { Sealed, Hacking, Lockout, Unlocking, Released }

        [Serializable]
        public struct Bolt
        {
            public Transform bolt;
            [Tooltip("Local offset from the locked pose to the withdrawn pose, m.")]
            public Vector3 withdrawOffset;
        }

        [Header("Events")]
        [SerializeField] private string[] hackOn = Array.Empty<string>();
        [SerializeField] private string[] abortOn = Array.Empty<string>();
        [SerializeField] private string[] lockoutOn = Array.Empty<string>();
        [SerializeField] private string[] unlockOn = Array.Empty<string>();
        [SerializeField] private string[] releasedOn = Array.Empty<string>();
        [SerializeField] private string[] sealOn = Array.Empty<string>();
        [Tooltip("After a seal event the bolts wait for the panels to close, s.")]
        [SerializeField, Min(0f)] private float sealDelay = 2.6f;
        [SerializeField, Min(0.1f)] private float lockoutSeconds = 4f;

        [Header("Parts")]
        [SerializeField] private Bolt[] bolts = Array.Empty<Bolt>();
        [SerializeField, Min(0.05f)] private float boltSeconds = 0.5f;
        [SerializeField] private Renderer[] lamps = Array.Empty<Renderer>();
        [SerializeField] private Light lampLight;
        [SerializeField] private Renderer[] statusScreens = Array.Empty<Renderer>();

        [Header("Camera")]
        [Tooltip("Optional: frames the gate while it is being hacked or unlocking and the player is close.")]
        [SerializeField] private VehicleChaseCamera chaseCamera;
        [SerializeField, Min(5f)] private float frameRange = 40f;

        [Header("Audio")]
        [SerializeField] private AudioSource source;
        [SerializeField] private AudioClip unlockClip;
        [SerializeField] private AudioClip latchClip;

        [Header("Colours")]
        [SerializeField, ColorUsage(false, true)] private Color sealedColor = new(2.2f, 0.12f, 0.2f);
        [SerializeField, ColorUsage(false, true)] private Color hackingColor = new(2.6f, 1.2f, 0.15f);
        [SerializeField, ColorUsage(false, true)] private Color releasedColor = new(0.2f, 2.4f, 0.9f);

        private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int ProgressId = Shader.PropertyToID("_Progress");
        private static readonly int StepsId = Shader.PropertyToID("_Steps");
        private static readonly int StepId = Shader.PropertyToID("_Step");
        private static readonly int ModeId = Shader.PropertyToID("_Mode");
        private static readonly int GlitchId = Shader.PropertyToID("_Glitch");
        private MissionWorld world;
        private Material lampMaterial;
        private MaterialPropertyBlock block;
        private Vector3[] lockedPositions = Array.Empty<Vector3>();
        private State state;
        private float stateTime, withdrawn, sealAt = -1f;
        private float lampIntensity;

        public string StateName => state.ToString();
        /// <summary>0 = bolts home (locked), 1 = withdrawn.</summary>
        public float BoltsWithdrawn => withdrawn;

        private void Awake()
        {
            block = new MaterialPropertyBlock();
            lockedPositions = new Vector3[bolts.Length];
            for (int i = 0; i < bolts.Length; i++) if (bolts[i].bolt != null) lockedPositions[i] = bolts[i].bolt.localPosition;
            if (lamps.Length > 0 && lamps[0] != null)
            {
                lampMaterial = new Material(lamps[0].sharedMaterial) { name = lamps[0].sharedMaterial.name + " (runtime)" };
                foreach (var r in lamps) if (r != null) r.sharedMaterial = lampMaterial;
            }
            if (lampLight != null) lampIntensity = lampLight.intensity;
        }

        private void OnDestroy()
        {
            if (lampMaterial != null) Destroy(lampMaterial);
        }

        public void Bind(MissionWorld missionWorld)
        {
            world = missionWorld;
            world.EventRaised += OnWorldEvent;
            Enter(State.Sealed);
            withdrawn = 0f;
            sealAt = -1f;
            PlaceBolts();
        }

        public void Unbind()
        {
            if (world != null) world.EventRaised -= OnWorldEvent;
            world = null;
            if (framing && chaseCamera != null) chaseCamera.SetFraming(false, transform.position);
            framing = false;
        }

        private bool framing;

        private void UpdateCamera()
        {
            if (chaseCamera == null || world == null) return;
            var player = world.PlayerBody;
            bool near = player != null && (player.position - transform.position).sqrMagnitude < frameRange * frameRange;
            bool on = near && (state is State.Hacking or State.Unlocking || state == State.Released && stateTime < 1.8f);
            if (on == framing) return;
            framing = on;
            // Gates sit in streets and alleys: stay nearly behind the car and rise, rather than swinging into a wall.
            chaseCamera.SetFraming(on, transform.position + Vector3.up * 3f, 10f, 4.5f);
        }

        private void OnWorldEvent(string eventId)
        {
            if (MissionWorld.Matches(hackOn, eventId) && state is State.Sealed) Enter(State.Hacking);
            else if (MissionWorld.Matches(abortOn, eventId) && state is State.Hacking) Enter(State.Sealed);
            else if (MissionWorld.Matches(lockoutOn, eventId)) Enter(State.Lockout);
            else if (MissionWorld.Matches(unlockOn, eventId) && state is not State.Released)
            {
                Enter(State.Unlocking);
                if (source != null && unlockClip != null) source.PlayOneShot(unlockClip);
            }
            else if (MissionWorld.Matches(releasedOn, eventId)) Enter(State.Released);
            if (MissionWorld.Matches(sealOn, eventId) && state is not State.Sealed) sealAt = Time.time + sealDelay;
        }

        private void Enter(State next)
        {
            state = next;
            stateTime = 0f;
        }

        private void Update()
        {
            stateTime += Time.deltaTime;
            UpdateCamera();
            if (sealAt >= 0f && Time.time >= sealAt)
            {
                sealAt = -1f;
                Enter(State.Sealed);
                if (source != null && latchClip != null) source.PlayOneShot(latchClip);
            }
            if (state == State.Lockout && stateTime >= lockoutSeconds) Enter(State.Sealed);

            float target = state is State.Unlocking or State.Released ? 1f : 0f;
            // Hacking: the bolts chatter against their seats as the controller is probed.
            float chatter = state == State.Hacking ? 0.04f * Mathf.Max(0f, Mathf.Sin(Time.time * 17f)) : 0f;
            float before = withdrawn;
            withdrawn = Mathf.MoveTowards(withdrawn, target, Time.deltaTime / boltSeconds);
            if (before < 1f && withdrawn >= 1f && source != null && latchClip != null) source.PlayOneShot(latchClip, 0.6f);
            PlaceBolts(chatter);

            Color colour; float pulse;
            switch (state)
            {
                case State.Hacking: colour = hackingColor; pulse = 0.55f + 0.45f * Mathf.Sin(Time.time * 7f); break;
                case State.Lockout: colour = sealedColor * 1.4f; pulse = Mathf.Repeat(Time.time * 5f, 1f) < 0.5f ? 1f : 0.1f; break;
                case State.Unlocking: colour = Color.Lerp(hackingColor, releasedColor, withdrawn); pulse = 1f; break;
                case State.Released: colour = releasedColor; pulse = 0.8f; break;
                default: colour = sealedColor; pulse = 0.85f; break;
            }
            if (lampMaterial != null) lampMaterial.SetColor(EmissionColor, colour * pulse);
            if (lampLight != null)
            {
                float max = Mathf.Max(colour.r, Mathf.Max(colour.g, colour.b));
                lampLight.color = max > 0f ? colour / max : Color.black;
                lampLight.intensity = lampIntensity * pulse;
            }
            foreach (var screen in statusScreens)
            {
                if (screen == null) continue;
                block.Clear();
                block.SetColor(ColorId, colour * 0.9f);
                block.SetFloat(ProgressId, state == State.Released ? 1f : withdrawn);
                block.SetFloat(StepsId, 3f);
                block.SetFloat(StepId, state switch { State.Hacking => 1f, State.Unlocking => 2f, State.Released => 99f, _ => 0f });
                block.SetFloat(ModeId, state switch { State.Hacking => 1f, State.Released => 2f, State.Lockout => 3f, _ => 0f });
                block.SetFloat(GlitchId, state == State.Lockout ? 0.8f : 0f);
                screen.SetPropertyBlock(block);
            }
        }

        private void PlaceBolts(float chatter = 0f)
        {
            float t = withdrawn * withdrawn * (3f - 2f * withdrawn);
            for (int i = 0; i < bolts.Length; i++)
            {
                if (bolts[i].bolt == null) continue;
                Vector3 dir = bolts[i].withdrawOffset;
                bolts[i].bolt.localPosition = lockedPositions[i] + dir * t + dir.normalized * chatter * ((i & 1) == 0 ? 1f : -0.6f);
            }
        }

#if UNITY_EDITOR
        public void EditorConfigure(string[] onHack, string[] onAbort, string[] onLockout, string[] onUnlock, string[] onReleased, string[] onSeal,
                                    Bolt[] lockBolts, Renderer[] lampRenderers, Light light, Renderer[] screens, AudioSource audio,
                                    AudioClip unlock, AudioClip latch, float lockout)
        {
            hackOn = onHack ?? Array.Empty<string>();
            abortOn = onAbort ?? Array.Empty<string>();
            lockoutOn = onLockout ?? Array.Empty<string>();
            unlockOn = onUnlock ?? Array.Empty<string>();
            releasedOn = onReleased ?? Array.Empty<string>();
            sealOn = onSeal ?? Array.Empty<string>();
            bolts = lockBolts ?? Array.Empty<Bolt>();
            lamps = lampRenderers ?? Array.Empty<Renderer>();
            lampLight = light;
            statusScreens = screens ?? Array.Empty<Renderer>();
            source = audio;
            unlockClip = unlock;
            latchClip = latch;
            lockoutSeconds = lockout;
        }

        public void EditorSetChaseCamera(VehicleChaseCamera camera) => chaseCamera = camera;
#endif
    }
}
