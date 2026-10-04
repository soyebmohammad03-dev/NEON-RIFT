using System.Collections.Generic;
using NeonRift.Audio;
using NeonRift.Game;
using NeonRift.Gameplay;
using NeonRift.Input;
using NeonRift.Vehicles;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using UnityEngine.UIElements;
using UnityEngine.Video;

namespace NeonRift.Intro
{
    /// <summary>
    /// Runs the opening cinematic in the city scene (the mission rig stays idle): stages the car lineup, plays the
    /// generated Timeline (camera shots, cues, audio), handles Skip, holds on the NEON RIFT title and, on Confirm,
    /// pushes the camera into the hero car and hands over to Car Select. With <see cref="IntroSettings.Mode.Video"/>
    /// a pre-rendered clip replaces the realtime shots and the realtime title takes over at its end.
    /// The cars are the catalog's gameplay prefabs through the normal spawn path: same models, physics and audio.
    /// </summary>
    public sealed class IntroSceneEntry : MonoBehaviour, IIntroEntryPoint, IIntroCueHandler
    {
        private enum Phase { Idle, Video, Playing, Title, PushIn, Leaving }

        [SerializeField] private PlayableDirector director;
        [SerializeField] private IntroOverlay overlay;
        [Tooltip("Lineup slots, left to right. The middle one holds the hero car (the selected or default vehicle).")]
        [SerializeField] private VehicleSpawnPoint[] lineup = new VehicleSpawnPoint[0];
        [SerializeField] private GameObject shots;
        [SerializeField] private CinemachineCamera pushCamera;
        [SerializeField] private Light[] stageLights = new Light[0];
        [SerializeField] private Volume surveillanceVolume;
        [SerializeField] private AudioSource ambienceSource, musicSource, effectsSource;
        [SerializeField] private AudioSource[] musicSources = new AudioSource[0];
        [SerializeField] private AudioClip ignitionClip;
        [SerializeField] private CityNavigation navigation;
        [SerializeField] private RacerProfile[] departureProfiles = new RacerProfile[0];
        [Tooltip("Intro-only street and sky life (traffic lanes, patrol drones), active while the intro plays.")]
        [SerializeField] private GameObject cityLife;
        [SerializeField] private Transform departureTarget;
        [Tooltip("Mission HUD in the same scene: hidden while the intro plays.")]
        [SerializeField] private UIDocument missionHud;
        [Tooltip("Timeline time where the title shot starts (Skip jumps here), s.")]
        [SerializeField] private float titleTime = 52.5f;
        [Tooltip("Timeline time when the title is fully revealed and the prompt appears, s.")]
        [SerializeField] private float holdTime = 56.5f;
        [SerializeField, Min(0.5f)] private float pushSeconds = 2.4f;

        [Header("Crew garage")]
        [Tooltip("The garage the crew leaves from (same building as Car Select). When set, the lineup stands in it.")]
        [SerializeField] private CrewGarage garage;
        [Tooltip("Confirm: the camera travels along these points (world) from the title shot into the garage, ending on " +
                 "Car Select's opening frame, so the scene change is a match cut.")]
        [SerializeField] private Vector3[] pushPath = new Vector3[0];
        [SerializeField] private Vector3[] pushLookPath = new Vector3[0];
        [SerializeField] private float pushEndFov = 34f;
        [Tooltip("Roll-out routes for the two bay cars (world), in departure order.")]
        [SerializeField] private Vector3[] departureA = new Vector3[0];
        [SerializeField] private Vector3[] departureB = new Vector3[0];
        [SerializeField] private float[] departureSpeeds = new float[0];
        [SerializeField] private float departureGap = 1.6f;

        private GameContext context;
        private IntroSettings settings;
        private Phase phase = Phase.Idle;
        private readonly List<VehicleController> cars = new();
        private VehicleController hero;
        private float pushStart;
        private Vector3 pushFrom, pushTo, lookFrom, lookTo;
        private Transform pushLook;
        private VideoPlayer video;
        private bool headlightsOn, enginesOn, departed, skipPending;
        private double lastTime;
        private NeonRiftControls.MenuActions menu;

        public bool IsPlaying => phase != Phase.Idle;
        public float Time01 => director != null && director.duration > 0 ? (float)(director.time / director.duration) : 0f;
        public string PhaseName => phase.ToString();
        public double TimelineTime => director != null ? director.time : 0.0;
        public IReadOnlyList<VehicleController> Cars => cars;

        // ---------------- Entry ----------------

        public void Enter(GameContext gameContext)
        {
            context = gameContext;
            settings = context.Config.Intro;
            if (missionHud != null && missionHud.rootVisualElement != null) missionHud.rootVisualElement.style.display = DisplayStyle.None;
            if (shots != null) shots.SetActive(true);
            if (cityLife != null) cityLife.SetActive(true);
            overlay.gameObject.SetActive(true);
            overlay.Clear();
            foreach (var l in stageLights) if (l != null) l.enabled = true;
            RouteAudio();
            StageLineup();

            menu = context.Controls.Menu;
            menu.Skip.performed += OnSkip;
            menu.Confirm.performed += OnConfirm;
            menu.Enable();

            if (settings != null && settings.PlaybackMode == IntroSettings.Mode.Video) PlayVideo();
            else Play(0.0);
            Debug.Log($"[Intro] started ({(phase == Phase.Video ? "video" : "realtime")}), hero {(hero != null ? hero.name : "none")}, {cars.Count} cars");
        }

        public void Exit()
        {
            if (context == null) return;
            menu.Skip.performed -= OnSkip;
            menu.Confirm.performed -= OnConfirm;
            menu.Disable();
            if (director != null) director.Stop();
            if (video != null) Destroy(video);
            foreach (var c in cars) if (c != null) Destroy(c.gameObject);
            cars.Clear();
            if (garage != null) garage.ResetForMission();
            foreach (var l in stageLights) if (l != null) l.enabled = false;
            if (surveillanceVolume != null) surveillanceVolume.weight = 0f;
            if (pushCamera != null) pushCamera.gameObject.SetActive(false);
            if (shots != null) shots.SetActive(false);
            if (cityLife != null) cityLife.SetActive(false);
            overlay.gameObject.SetActive(false);
            Time.timeScale = 1f;
            phase = Phase.Idle;
            context = null;
        }

        private void OnDestroy()
        {
            if (context != null) Exit();
        }

        private void RouteAudio()
        {
            var mixer = context.Config.AudioMixer;
            if (mixer == null) return;
            if (ambienceSource != null) ambienceSource.outputAudioMixerGroup = mixer.Ambience;
            if (musicSource != null) musicSource.outputAudioMixerGroup = mixer.Music;
            foreach (var s in musicSources) if (s != null) s.outputAudioMixerGroup = mixer.Music;
            if (effectsSource != null) effectsSource.outputAudioMixerGroup = mixer.Sfx;
        }

        /// <summary>Hero (selected or default car) in the middle slot, the other catalog cars either side. Engines silent, lights off.</summary>
        private void StageLineup()
        {
            var catalog = context.Config.VehicleCatalog;
            if (catalog == null || catalog.Count == 0 || lineup.Length == 0) return;
            var heroDef = context.Session.SelectedVehicle != null ? context.Session.SelectedVehicle : catalog.Default;
            var order = new List<VehicleDefinition>();
            foreach (var v in catalog.Vehicles) if (v != heroDef) order.Add(v);
            int middle = lineup.Length / 2;
            int next = 0;
            if (garage != null) middle = 0;   // garage lineup: [0] hero on the turntable, [1] [2] the bays
            for (int i = 0; i < lineup.Length; i++)
            {
                var def = i == middle ? heroDef : next < order.Count ? order[next++] : null;
                if (def == null || lineup[i] == null) continue;
                var car = lineup[i].Spawn(def, $"Intro_{def.Id}", detailedLights: i == middle);
                if (car == null) continue;
                cars.Add(car);
                if (i == middle) hero = car;
                if (car.TryGetComponent(out VehicleLights lights)) lights.SetHeadlights(false);
                SetEngineAudio(car, false);
            }
        }

        private static void SetEngineAudio(VehicleController car, bool on)
        {
            foreach (var source in car.GetComponentsInChildren<AudioSource>(true)) source.mute = !on;
            if (car.TryGetComponent(out VehicleAudio audio)) audio.SetPlayerView(false);
        }

        // ---------------- Playback ----------------

        private void Play(double from)
        {
            Debug.Log($"[Intro] play from {from:0.0}s (was {(director != null ? director.time : -1):0.00}s, frame {Time.frameCount})");
            phase = Phase.Playing;
            director.time = from;
            director.Play();
            director.Evaluate();
        }

        private void PlayVideo()
        {
            phase = Phase.Video;
            overlay.SetFade(0f);
            var cam = Camera.main;
            video = cam.gameObject.AddComponent<VideoPlayer>();
            video.playOnAwake = false;
            video.renderMode = VideoRenderMode.CameraNearPlane;
            video.targetCamera = cam;
            video.clip = settings.Video;
            video.loopPointReached += _ => SkipToTitle();
            video.Play();
        }

        private void Update()
        {
            if (phase == Phase.Idle || context == null) return;
            switch (phase)
            {
                case Phase.Video:
                case Phase.Playing:
                    if (skipPending && director.time >= 0.05) SkipToTitle();
                    if (director.time < lastTime - 1.0) Debug.LogWarning($"[Intro] timeline jumped back {lastTime:0.0}s → {director.time:0.0}s at frame {Time.frameCount}");
                    lastTime = director.time;
                    if (settings != null && settings.Skippable) overlay.SetSkipHint(phase == Phase.Video);
                    if (phase == Phase.Playing && director.time >= holdTime) EnterTitle();
                    break;
                case Phase.Title:
                    overlay.SetTitle(1f, 1f, 1f);
                    overlay.SetPrompt(true);
                    break;
                case Phase.PushIn:
                    UpdatePushIn();
                    break;
            }
        }

        private void EnterTitle()
        {
            phase = Phase.Title;
            if (settings != null) settings.MarkSeen();
            overlay.SetSkipHint(false);
            Debug.Log("[Intro] title reached; waiting for Confirm");
        }

        private void OnSkip(InputAction.CallbackContext _)
        {
            if (phase is not (Phase.Playing or Phase.Video)) return;
            if (settings != null && !settings.Skippable) return;
            Debug.Log($"[Intro] skipped at {(phase == Phase.Video ? "video" : director.time.ToString("0.0") + "s")}");
            SkipToTitle();
        }

        /// <summary>Jumps to the title shot with the world in the state the skipped shots would have left it.</summary>
        public void SkipToTitle()
        {
            // The Timeline graph is created on the director's first evaluation; a skip in that same frame would be
            // overwritten, so try again next frame.
            if (phase == Phase.Playing && director != null && director.time < 0.05)
            {
                skipPending = true;
                return;
            }
            skipPending = false;
            if (video != null)
            {
                video.Stop();
                Destroy(video);
                video = null;
            }
            Time.timeScale = 1f;
            if (surveillanceVolume != null) surveillanceVolume.weight = 0f;
            overlay.HideCard();
            overlay.SetSurveillance(false, 0f, null);
            overlay.SetFlash(0f);
            OnCueStart(IntroCueKind.Headlights);
            OnCueStart(IntroCueKind.Ignition);
            if (garage != null)
            {
                garage.LightsOnImmediate();
                garage.OpenDoor(immediate: true);
            }
            // The rivals had driven off before the title: in a skip they are simply gone.
            for (int i = cars.Count - 1; i >= 0; i--)
                if (cars[i] != hero)
                {
                    if (cars[i] != null) Destroy(cars[i].gameObject);
                    cars.RemoveAt(i);
                }
            departed = true;
            if (director.time < titleTime) Play(titleTime);
            else if (phase == Phase.Video) Play(titleTime);
        }

        private void OnConfirm(InputAction.CallbackContext _) => Confirm();

        /// <summary>From the title: push the camera into the hero car and go to Car Select.</summary>
        public void Confirm()
        {
            if (phase != Phase.Title || context.Flow.IsTransitioning) return;
            phase = Phase.PushIn;
            overlay.SetPrompt(false);
            var cam = Camera.main.transform;
            director.Pause();
            pushFrom = cam.position;
            lookFrom = cam.position + cam.forward * 20f;
            pushStartFov = Camera.main.fieldOfView;
            if (pushPath.Length >= 1 && pushLookPath.Length == pushPath.Length)
            {
                // Through the garage door to Car Select's opening frame (a match cut).
                route.Clear();
                lookRoute.Clear();
                route.Add(pushFrom);
                lookRoute.Add(lookFrom);
                route.AddRange(pushPath);
                lookRoute.AddRange(pushLookPath);
                pushTo = route[^1];
                lookTo = lookRoute[^1];
            }
            else if (hero != null)
            {
                route.Clear();
                Vector3 nose = hero.transform.position + hero.transform.forward * 2.8f + Vector3.up * 0.7f;
                pushTo = hero.transform.position + hero.transform.forward * 5.2f + hero.transform.right * 2.2f + Vector3.up * 1.0f;
                lookTo = nose;
            }
            else
            {
                route.Clear();
                pushTo = pushFrom + cam.forward * 15f;
                lookTo = lookFrom;
            }
            if (pushLook == null) pushLook = new GameObject("IntroPushLook").transform;
            pushLook.position = lookFrom;
            pushCamera.transform.SetPositionAndRotation(pushFrom, cam.rotation);
            pushCamera.LookAt = pushLook;
            pushCamera.Priority.Value = 1000;
            pushCamera.Lens.FieldOfView = pushStartFov;
            pushCamera.gameObject.SetActive(true);
            director.Stop();
            pushStart = Time.unscaledTime;
            Debug.Log($"[Intro] confirm: pushing in to the hero car ({(route.Count > 0 ? "through the garage door" : "direct")})");
        }

        private readonly List<Vector3> route = new(), lookRoute = new();
        private float pushStartFov = 50f;

        private void UpdatePushIn()
        {
            float t = Mathf.Clamp01((Time.unscaledTime - pushStart) / pushSeconds);
            float e = t * t * (3f - 2f * t);
            if (route.Count >= 2)
            {
                pushCamera.transform.position = CatmullRom(route, e);
                pushLook.position = CatmullRom(lookRoute, e);
                pushCamera.Lens.FieldOfView = Mathf.Lerp(pushStartFov, pushEndFov, e);
            }
            else
            {
                pushCamera.transform.position = Vector3.Lerp(pushFrom, pushTo, e);
                pushLook.position = Vector3.Lerp(lookFrom, lookTo, e);
            }
            overlay.SetTitle(1f - t * 3f, 1f - t * 3f, 1f - t * 3f);
            // Fade only at the very end: the frame Car Select opens on is the one we arrive at.
            overlay.SetFade(Mathf.InverseLerp(route.Count >= 2 ? 0.86f : 0.55f, 1f, t));
            if (t >= 1f && phase == Phase.PushIn)
            {
                phase = Phase.Leaving;
                context.Session.MarkIntroArrival();
                context.Flow.GoToCarSelect();
            }
        }

        /// <summary>Centripetal-ish Catmull-Rom through <paramref name="points"/> at 0..1 (uniform per segment).</summary>
        private static Vector3 CatmullRom(List<Vector3> points, float t)
        {
            int n = points.Count - 1;
            float f = Mathf.Clamp01(t) * n;
            int i = Mathf.Min(n - 1, Mathf.FloorToInt(f));
            float u = f - i;
            Vector3 p0 = points[Mathf.Max(0, i - 1)], p1 = points[i], p2 = points[i + 1], p3 = points[Mathf.Min(n, i + 2)];
            return 0.5f * (2f * p1 + (-p0 + p2) * u + (2f * p0 - 5f * p1 + 4f * p2 - p3) * u * u + (-p0 + 3f * p1 - 3f * p2 + p3) * u * u * u);
        }

        // ---------------- Cues ----------------

        public void OnCueStart(IntroCueKind kind)
        {
            switch (kind)
            {
                case IntroCueKind.Headlights:
                    if (headlightsOn) break;
                    headlightsOn = true;
                    foreach (var c in cars) if (c != null && c.TryGetComponent(out VehicleLights l)) l.SetHeadlights(true);
                    break;
                case IntroCueKind.Ignition:
                    if (enginesOn) break;
                    enginesOn = true;
                    if (ignitionClip != null && effectsSource != null && phase == Phase.Playing) effectsSource.PlayOneShot(ignitionClip);
                    foreach (var c in cars) if (c != null) SetEngineAudio(c, true);
                    break;
                case IntroCueKind.Departure:
                    Depart();
                    break;
                case IntroCueKind.GarageLights:
                    if (garage != null) garage.LightsOn();
                    break;
                case IntroCueKind.DoorOpen:
                    if (garage != null) garage.OpenDoor();
                    break;
            }
        }

        public void OnCue(IntroCueKind kind, string text, string subtitle, float progress, float seconds, float duration)
        {
            if (overlay == null) return;
            switch (kind)
            {
                case IntroCueKind.FadeIn:
                    overlay.SetFade(1f - Mathf.SmoothStep(0f, 1f, progress));
                    break;
                case IntroCueKind.Dip:
                    overlay.SetFade(Mathf.Sin(progress * Mathf.PI));
                    break;
                case IntroCueKind.Flash:
                    overlay.SetFlash((1f - progress) * 0.55f);
                    break;
                case IntroCueKind.Card:
                    float a = Mathf.Min(seconds / 0.6f, (duration - seconds) / 0.7f);
                    overlay.ShowCard(text, subtitle, Mathf.Clamp01(a));
                    break;
                case IntroCueKind.Title:
                    overlay.SetFade(0f);
                    overlay.SetTitle(Mathf.Clamp01(seconds / 1.6f), Mathf.Clamp01((seconds - 0.9f) / 1.2f), Mathf.Clamp01((seconds - 1.5f) / 1.1f));
                    break;
                case IntroCueKind.Surveillance:
                    float w = Mathf.Clamp01(Mathf.Min(seconds / 0.15f, (duration - seconds) / 0.15f));
                    overlay.SetSurveillance(true, seconds, seconds > duration * 0.45f ? text : null);
                    if (surveillanceVolume != null) surveillanceVolume.weight = w;
                    break;
                case IntroCueKind.SkipHint:
                    overlay.SetSkipHint(settings == null || settings.Skippable);
                    break;
                case IntroCueKind.Letterbox:
                    overlay.SetLetterbox(true);
                    break;
            }
        }

        public void OnCueEnd(IntroCueKind kind)
        {
            if (overlay == null) return;
            switch (kind)
            {
                case IntroCueKind.FadeIn:
                case IntroCueKind.Dip:
                    overlay.SetFade(0f);
                    break;
                case IntroCueKind.Flash:
                    overlay.SetFlash(0f);
                    break;
                case IntroCueKind.Card:
                    overlay.HideCard();
                    break;
                case IntroCueKind.Surveillance:
                    overlay.SetSurveillance(false, 0f, null);
                    if (surveillanceVolume != null) surveillanceVolume.weight = 0f;
                    break;
                case IntroCueKind.SkipHint:
                    overlay.SetSkipHint(false);
                    break;
                case IntroCueKind.Title:
                    // The title stays up while we hold on it and during the push-in.
                    if (phase is Phase.Playing) overlay.SetTitle(0f, 0f, 0f);
                    break;
            }
        }

        /// <summary>The rival cars pull away through the normal AI driver, so they leave with real physics and engine audio.</summary>
        private void Depart()
        {
            if (departed) return;
            if (garage != null && departureA.Length > 1)
            {
                // Out of the bays, through the door and north up W Avenue on scripted lines (real physics and audio).
                departed = true;
                int k = 0;
                foreach (var c in cars)
                {
                    if (c == null || c == hero) continue;
                    var path = k == 0 ? departureA : departureB;
                    var speeds = new List<float>(departureSpeeds);
                    c.SetInputSource(new WaypointDriver(c, path, speeds, k * departureGap));
                    k++;
                }
                return;
            }
            if (departureTarget == null || navigation == null) return;
            departed = true;
            int p = 0;
            foreach (var c in cars)
            {
                if (c == null || c == hero) continue;
                var profile = departureProfiles.Length > 0 ? departureProfiles[p++ % departureProfiles.Length] : null;
                if (profile == null) continue;
                var driver = new RacerDriver(c, profile, navigation) { Traffic = cars };
                driver.SetGoal(departureTarget.position, hold: false);
                c.SetInputSource(driver);
            }
        }

#if UNITY_EDITOR
        public void EditorConfigureCityLife(GameObject life) => cityLife = life;

        public void EditorConfigure(PlayableDirector playableDirector, IntroOverlay introOverlay, VehicleSpawnPoint[] slots, GameObject shotRoot,
                                    CinemachineCamera push, Light[] lights, Volume cctv, AudioSource ambience, AudioSource music, AudioSource effects,
                                    AudioSource[] moreMusic, AudioClip ignition, CityNavigation nav, RacerProfile[] profiles, Transform target,
                                    UIDocument hud, float title, float hold)
        {
            garage = null;
            director = playableDirector;
            overlay = introOverlay;
            lineup = slots;
            shots = shotRoot;
            pushCamera = push;
            stageLights = lights;
            surveillanceVolume = cctv;
            ambienceSource = ambience;
            musicSource = music;
            effectsSource = effects;
            musicSources = moreMusic;
            ignitionClip = ignition;
            navigation = nav;
            departureProfiles = profiles;
            departureTarget = target;
            missionHud = hud;
            titleTime = title;
            holdTime = hold;
        }

        public void EditorConfigureGarage(CrewGarage crewGarage, Vector3[] path, Vector3[] look, float endFov, float seconds,
                                          Vector3[] routeA, Vector3[] routeB, float[] speeds, float gap)
        {
            garage = crewGarage;
            pushPath = path;
            pushLookPath = look;
            pushEndFov = endFov;
            pushSeconds = seconds;
            departureA = routeA;
            departureB = routeB;
            departureSpeeds = speeds;
            departureGap = gap;
        }
#endif
    }
}
