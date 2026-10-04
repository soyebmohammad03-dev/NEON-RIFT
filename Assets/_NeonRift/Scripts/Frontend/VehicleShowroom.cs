using System.Collections;
using System.Collections.Generic;
using NeonRift.Vehicles;
using Unity.Cinemachine;
using UnityEngine;

namespace NeonRift.Frontend
{
    /// <summary>
    /// The crew garage behind Car Select. Presents the selected car on a turntable under the stage lights, the rest of
    /// the catalog parked in the shadows, and the transitions between them: on a switch the stage dims, the new car
    /// comes up with its headlights, brake lights and an engine blip; on confirm the turntable swings the car to the
    /// roller door, the door lifts on the street outside, and the car pulls out under its own sound while the camera
    /// drops in behind it. Instances are presentation-only (physics frozen, colliders off); the cars are the real
    /// catalog models with their real lights (<see cref="VehicleLights"/>) and engine recordings (<see cref="ShowroomEngine"/>).
    /// </summary>
    public sealed class VehicleShowroom : MonoBehaviour
    {
        [SerializeField] private Transform turntable;
        [Tooltip("Degrees per second while idle.")]
        [SerializeField] private float idleSpinSpeed = 7f;
        [Tooltip("Degrees per pixel dragged (scaled by 1080p reference height).")]
        [SerializeField] private float dragSensitivity = 0.35f;
        [Tooltip("Degrees per second at full stick/key deflection.")]
        [SerializeField] private float axisSpinSpeed = 120f;
        [Tooltip("How quickly drag momentum decays (per second).")]
        [SerializeField] private float momentumDamping = 4f;
        [Tooltip("Seconds after user input before idle spin resumes.")]
        [SerializeField] private float idleResumeDelay = 2.5f;

        [Header("Garage")]
        [Tooltip("Stage lights dimmed while cars are swapped (key, rims, soft boxes).")]
        [SerializeField] private Light[] stageLights = new Light[0];
        [Tooltip("Emissive strip renderers that dim with the stage lights.")]
        [SerializeField] private Renderer[] stageStrips = new Renderer[0];
        [SerializeField] private Transform[] parkedSlots = new Transform[0];
        [SerializeField] private ShowroomEngine engine;
        [SerializeField] private Transform door;
        [SerializeField] private float doorOpenHeight = 5.4f;
        [Tooltip("World direction the car drives out through the door.")]
        [SerializeField] private Vector3 exitDirection = Vector3.back;

        [Header("Cameras")]
        [SerializeField] private CinemachineCamera heroCamera;
        [SerializeField] private CinemachineCamera swapCamera;
        [SerializeField] private CinemachineCamera departCamera;
        [Tooltip("Close-up on the headlight: Car Select opens on it after the intro's push-in, then pulls back.")]
        [SerializeField] private CinemachineCamera openingCamera;
        [Tooltip("Slow drift of the hero camera around the turntable, degrees either side, and its period in seconds.")]
        [SerializeField] private Vector2 heroDrift = new(9f, 26f);

        private GameObject current;
        private VehicleDefinition currentDefinition;
        private readonly List<GameObject> parked = new();
        private float angularVelocity;
        private float lastUserInputTime = float.NegativeInfinity;
        private float[] stageBase = new float[0];
        private Color[] stripBase = new Color[0];
        private float stageLevel = 1f, stageTarget = 1f;
        private Vector3 heroOffset;
        private Coroutine swap;
        private bool departing;
        private MaterialPropertyBlock block;

        public bool IsBusy => swap != null || departing;
        public GameObject Current => current;
        public float StageLevel => stageLevel;

        private void Awake()
        {
            stageBase = new float[stageLights.Length];
            for (int i = 0; i < stageLights.Length; i++) stageBase[i] = stageLights[i] != null ? stageLights[i].intensity : 0f;
            stripBase = new Color[stageStrips.Length];
            for (int i = 0; i < stageStrips.Length; i++)
                stripBase[i] = stageStrips[i] != null && stageStrips[i].sharedMaterial != null ? stageStrips[i].sharedMaterial.GetColor("_EmissionColor") : Color.black;
            if (heroCamera != null) heroOffset = heroCamera.transform.position - turntable.position;
            block = new MaterialPropertyBlock();
        }

        // ---------------- Presentation ----------------

        /// <summary>Puts <paramref name="definition"/> on the turntable. With <paramref name="animate"/>: dim, swap, lights up, blip.</summary>
        public void Show(VehicleDefinition definition, bool animate = true)
        {
            if (departing) return;
            if (swap != null) StopCoroutine(swap);
            swap = StartCoroutine(Swap(definition, animate));
        }

        private IEnumerator Swap(VehicleDefinition definition, bool animate)
        {
            if (animate && current != null)
            {
                SetCamera(swapCamera, 12);
                stageTarget = 0.08f;
                var oldLights = current.GetComponent<VehicleLights>();
                if (oldLights != null) { oldLights.SetHeadlightLevel(0f); oldLights.SetTailOverride(0f); }
                if (engine != null) engine.Stop(0.25f);
                // The outgoing car whips away on the turntable as the stage goes dark.
                angularVelocity = 0f;
                for (float t = 0f; t < 0.34f; t += Time.unscaledDeltaTime)
                {
                    float k = t / 0.34f;
                    turntable.Rotate(0f, Mathf.Lerp(120f, 720f, k * k) * Time.unscaledDeltaTime, 0f, Space.World);
                    yield return null;
                }
            }
            if (current != null) Destroy(current);
            current = null;
            currentDefinition = definition;
            if (definition != null && definition.ShowroomPrefab != null)
            {
                current = Instantiate(definition.ShowroomPrefab, turntable);
                current.name = $"Showroom_{definition.Id}";
                MakePresentationOnly(current);
                CentreOnTurntable(current.transform);
                var lights = current.GetComponent<VehicleLights>();
                if (lights == null) lights = current.AddComponent<VehicleLights>();
                lights.Build(true);
                lights.enabled = true;
                lights.SetHeadlightLevel(0f);
                lights.SetTailOverride(0f);
            }
            stageTarget = 1f;
            SetCamera(heroCamera, 12);
            if (current == null) { swap = null; yield break; }
            if (animate)
            {
                // The new car spins in and settles into its pose as the lights come back.
                float settle = turntable.eulerAngles.y;
                float from = settle - 150f;
                for (float t = 0f; t < 0.55f; t += Time.unscaledDeltaTime)
                {
                    float k = 1f - Mathf.Pow(1f - Mathf.Clamp01(t / 0.55f), 3f);
                    turntable.rotation = Quaternion.Euler(0f, Mathf.Lerp(from, settle, k), 0f);
                    yield return null;
                }
                turntable.rotation = Quaternion.Euler(0f, settle, 0f);
                lastUserInputTime = Time.unscaledTime - idleResumeDelay + 0.8f;
            }
            var l = current.GetComponent<VehicleLights>();
            if (animate) yield return Wait(0.18f);
            // Headlights come up in two steps (DRL, then beam), tail lights on, a brake pulse with the blip.
            l.SetTailOverride(1f);
            for (float t = 0f; t < 0.35f; t += Time.unscaledDeltaTime) { l.SetHeadlightLevel(Mathf.Lerp(0f, 0.35f, t / 0.35f)); yield return null; }
            if (engine != null) { engine.Play(definition, 0.3f); engine.Blip(0.42f); }
            l.SetTailOverride(2f);
            yield return Wait(0.12f);
            for (float t = 0f; t < 0.4f; t += Time.unscaledDeltaTime) { l.SetHeadlightLevel(Mathf.Lerp(0.35f, 1f, t / 0.4f)); yield return null; }
            l.SetHeadlightLevel(1f);
            yield return Wait(0.35f);
            l.SetTailOverride(1f);
            swap = null;
        }

        /// <summary>Parks <paramref name="others"/> in the background slots, lights off, as silhouettes.</summary>
        public void ShowParked(IEnumerable<VehicleDefinition> others)
        {
            foreach (var p in parked) if (p != null) Destroy(p);
            parked.Clear();
            int i = 0;
            foreach (var def in others)
            {
                if (i >= parkedSlots.Length) break;
                if (def == null || def.ShowroomPrefab == null) continue;
                var slot = parkedSlots[i++];
                var go = Instantiate(def.ShowroomPrefab, slot);
                go.name = $"Parked_{def.Id}";
                MakePresentationOnly(go);
                go.transform.localRotation = Quaternion.identity;
                CentreOn(go.transform, slot.position);
                parked.Add(go);
            }
        }

        /// <summary>Opens on the headlight close-up, then pulls back to the hero shot (continuity with the intro's push-in).</summary>
        public IEnumerator OpeningPullBack(float holdSeconds = 0.6f)
        {
            if (openingCamera == null) yield break;
            // Cut (no blend) to the close-up so the first visible frame matches the intro's last one, hold it while the
            // loading fade clears, then the slow pull-back to the hero shot.
            var brain = Camera.main != null ? Camera.main.GetComponent<CinemachineBrain>() : null;
            var blend = brain != null ? brain.DefaultBlend : default;
            if (brain != null) brain.DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.Cut, 0f);
            SetCamera(openingCamera, 20);
            yield return null;
            yield return null;
            if (brain != null) brain.DefaultBlend = blend;
            yield return Wait(holdSeconds);
            SetCamera(heroCamera, 12);
        }

        // ---------------- Input ----------------

        /// <summary>Pointer drag in screen pixels.</summary>
        public void Drag(float deltaPixels)
        {
            if (departing) return;
            float degrees = -deltaPixels * dragSensitivity * (1080f / Mathf.Max(1, Screen.height));
            turntable.Rotate(0f, degrees, 0f, Space.World);
            angularVelocity = degrees / Mathf.Max(Time.unscaledDeltaTime, 0.0001f);
            lastUserInputTime = Time.unscaledTime;
        }

        /// <summary>Continuous axis input (-1..1) from keyboard or gamepad.</summary>
        public void Spin(float axis)
        {
            if (departing || Mathf.Abs(axis) < 0.05f) return;
            angularVelocity = -axis * axisSpinSpeed;
            lastUserInputTime = Time.unscaledTime;
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (!departing)
            {
                bool idle = Time.unscaledTime - lastUserInputTime > idleResumeDelay;
                float target = idle ? idleSpinSpeed : 0f;
                angularVelocity = Mathf.Lerp(angularVelocity, target, 1f - Mathf.Exp(-momentumDamping * dt));
                turntable.Rotate(0f, angularVelocity * dt, 0f, Space.World);
                if (heroCamera != null && heroDrift.x > 0f)
                {
                    // A slow dolly drift around the turntable: the camera is never quite still.
                    float a = Mathf.Sin(Time.unscaledTime * Mathf.PI * 2f / Mathf.Max(1f, heroDrift.y)) * heroDrift.x;
                    heroCamera.transform.position = turntable.position + Quaternion.Euler(0f, a, 0f) * heroOffset;
                }
            }
            stageLevel = Mathf.MoveTowards(stageLevel, stageTarget, dt * 2.6f);
            for (int i = 0; i < stageLights.Length; i++) if (stageLights[i] != null) stageLights[i].intensity = stageBase[i] * stageLevel;
            for (int i = 0; i < stageStrips.Length; i++)
            {
                if (stageStrips[i] == null) continue;
                stageStrips[i].GetPropertyBlock(block);
                block.SetColor("_EmissionColor", stripBase[i] * Mathf.Lerp(0.35f, 1f, stageLevel));
                stageStrips[i].SetPropertyBlock(block);
            }
        }

        // ---------------- Departure ----------------

        /// <summary>
        /// Confirm: swing the car to the door, lift the door, rev, release the brakes and pull out with the camera
        /// behind. Yields when the car is through the door (the scene transition fades from there).
        /// </summary>
        public IEnumerator Depart()
        {
            if (current == null) yield break;
            while (swap != null) yield return null;
            departing = true;
            var lights = current.GetComponent<VehicleLights>();
            var rig = current.GetComponent<VehicleRig>();
            if (lights != null) { lights.SetHeadlightLevel(1f); lights.SetTailOverride(2f); }

            // Turntable swings the nose to the door while the door starts up.
            float startYaw = turntable.eulerAngles.y;
            float carYaw = current.transform.eulerAngles.y;
            float exitYaw = Quaternion.LookRotation(exitDirection).eulerAngles.y;
            float targetYaw = startYaw + Mathf.DeltaAngle(carYaw, exitYaw);
            Vector3 doorClosed = door != null ? door.localPosition : Vector3.zero;
            if (engine != null) engine.Blip(0.35f);
            for (float t = 0f; t < 1.6f; t += Time.unscaledDeltaTime)
            {
                float k = Mathf.SmoothStep(0f, 1f, t / 1.6f);
                turntable.rotation = Quaternion.Euler(0f, Mathf.LerpAngle(startYaw, targetYaw, k), 0f);
                if (door != null) door.localPosition = doorClosed + Vector3.up * (doorOpenHeight * Mathf.SmoothStep(0f, 1f, t / 2.4f));
                yield return null;
            }
            // Square to the door exactly before pulling away: the car drives out along its own nose.
            turntable.rotation = Quaternion.Euler(0f, targetYaw, 0f);
            Quaternion heading = Quaternion.LookRotation(Vector3.ProjectOnPlane(exitDirection, Vector3.up).normalized);
            current.transform.rotation = heading;
            SetCamera(departCamera, 30);
            if (engine != null) engine.Blip(0.7f, 0.12f);
            yield return Wait(0.55f);
            if (lights != null) lights.SetTailOverride(1f);
            if (engine != null) engine.Blip(0.85f, 0.9f);

            // Pull away: eased acceleration along the exit, wheels turning with the distance covered.
            var car = current.transform;
            Vector3 start = car.position;
            float distance = 0f, speed = 0f;
            for (float t = 0f; t < 2.6f; t += Time.unscaledDeltaTime)
            {
                // Capped so a frame hitch never jumps the car down the street.
                float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
                if (door != null) door.localPosition = doorClosed + Vector3.up * doorOpenHeight;
                speed = Mathf.Min(speed + 7.5f * dt, 24f);
                distance += speed * dt;
                car.SetPositionAndRotation(start + heading * Vector3.forward * distance, heading);
                if (rig != null)
                    foreach (var w in rig.Wheels)
                        if (w.Spin != null && w.Radius > 0.01f) w.Spin.Rotate(speed * dt / w.Radius * Mathf.Rad2Deg, 0f, 0f, Space.Self);
                yield return null;
            }
        }

        // ---------------- Helpers ----------------

        private static IEnumerator Wait(float seconds)
        {
            for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime) yield return null;
        }

        private void SetCamera(CinemachineCamera cam, int priority)
        {
            foreach (var c in new[] { heroCamera, swapCamera, departCamera, openingCamera })
                if (c != null) c.Priority = c == cam ? priority : 0;
        }

        private static void MakePresentationOnly(GameObject instance)
        {
            foreach (var body in instance.GetComponentsInChildren<Rigidbody>(true))
            {
                body.isKinematic = true;
                body.detectCollisions = false;
                // An interpolated body writes its (one physics step old) pose back to the transform every frame, which
                // fights the turntable and the departure: the car lagged the swing and left the garage at an angle.
                body.interpolation = RigidbodyInterpolation.None;
            }
            foreach (var col in instance.GetComponentsInChildren<Collider>(true))
                col.enabled = false;
            foreach (var behaviour in instance.GetComponentsInChildren<MonoBehaviour>(true))
                if (behaviour is not VehicleRig) behaviour.enabled = false;
            foreach (var source in instance.GetComponentsInChildren<AudioSource>(true))
                source.enabled = false;
        }

        private void CentreOnTurntable(Transform model) => CentreOn(model, turntable.position);

        private static void CentreOn(Transform model, Vector3 point)
        {
            var renderers = System.Array.FindAll(model.GetComponentsInChildren<Renderer>(), r => r.enabled);
            if (renderers.Length == 0) return;
            var bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            model.position += point - new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
        }

#if UNITY_EDITOR
        public void EditorConfigure(Transform table, Light[] lights, Renderer[] strips, Transform[] slots, ShowroomEngine showroomEngine,
                                    Transform garageDoor, float doorHeight, Vector3 exit, CinemachineCamera hero, CinemachineCamera swapCam,
                                    CinemachineCamera depart, CinemachineCamera opening)
        {
            turntable = table;
            stageLights = lights;
            stageStrips = strips;
            parkedSlots = slots;
            engine = showroomEngine;
            door = garageDoor;
            doorOpenHeight = doorHeight;
            exitDirection = exit;
            heroCamera = hero;
            swapCamera = swapCam;
            departCamera = depart;
            openingCamera = opening;
        }
#endif
    }
}
