using System.Collections.Generic;
using System.Linq;
using NeonRift.EditorTools.Audio;
using NeonRift.EditorTools.District;
using NeonRift.EditorTools.Frontend;
using NeonRift.EditorTools.Vehicles;
using NeonRift.Game;
using NeonRift.Gameplay;
using NeonRift.Intro;
using NeonRift.World;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.Timeline;
using UnityEngine.UIElements;

namespace NeonRift.EditorTools.Intro
{
    /// <summary>
    /// Generates the opening cinematic into the Night Run scene: a rig (shot cameras, car lineup slots, stage lights,
    /// surveillance grade, audio sources, overlay) and a Timeline asset built from the shot table below. Camera moves are
    /// animation clips (eased position and look direction), cuts and blends are Cinemachine shot clips, story beats are
    /// <see cref="IntroCueTrack"/> clips and the soundtrack is audio tracks. Edit the table and rebuild with
    /// <c>Neon Rift ▸ Intro ▸ Build Intro (open Night Run scene)</c>, or it is rebuilt with the city.
    /// </summary>
    public static partial class IntroBuilder
    {
        public const string DataFolder = "Assets/_NeonRift/Data/Intro";
        public const string TimelinePath = DataFolder + "/Intro_NightRun.playable";
        public const string SettingsPath = "Assets/_NeonRift/Data/Config/IntroSettings.asset";

        /// <summary>Lineup on W Avenue, south of the spawn: three cars abreast, facing north.</summary>
        private static readonly Vector3 LineupCentre = new(0f, 0f, -285f);
        private const float LineupSpacing = 3.6f;

        public const float TitleTime = 75.2f, HoldTime = 79.2f, Duration = 84f;

        private readonly struct Shot
        {
            public readonly string Name;
            public readonly float Start, End, Blend, Fov;
            public readonly Vector3 From, To, LookFrom, LookTo;

            public Shot(string name, float start, float end, float blend, float fov, Vector3 from, Vector3 to, Vector3 lookFrom, Vector3 lookTo)
            {
                Name = name; Start = start; End = end; Blend = blend; Fov = fov;
                From = from; To = to; LookFrom = lookFrom; LookTo = lookTo;
            }
        }

        private readonly struct Cue
        {
            public readonly IntroCueKind Kind;
            public readonly float Start, End;
            public readonly string Text, Subtitle;

            public Cue(IntroCueKind kind, float start, float end, string text = null, string subtitle = null)
            {
                Kind = kind; Start = start; End = end; Text = text; Subtitle = subtitle;
            }
        }

        [MenuItem("Neon Rift/Intro/Build Intro (open Night Run scene)")]
        private static void BuildFromMenu()
        {
            var scene = SceneManager.GetActiveScene();
            Debug.Log(Build(scene));
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        [MenuItem("Neon Rift/Intro/Reset 'Intro Seen' (play it on next launch)")]
        private static void ResetSeen()
        {
            var settings = AssetDatabase.LoadAssetAtPath<IntroSettings>(SettingsPath);
            if (settings != null) settings.ResetSeen();
            Debug.Log("[Intro] 'seen' flag cleared.");
        }

        /// <summary>Builds (or rebuilds) the intro rig and Timeline in <paramref name="scene"/> (the Night Run city).</summary>
        public static string Build(Scene scene)
        {
            var log = new System.Text.StringBuilder("[Intro] build\n");
            VehiclePrefabBuilder.EnsureFolder(DataFolder);
            log.Append(IntroAudioGenerator.Generate(out var clips));
            var ambience = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/_NeonRift/Audio/Generated/Mission/City_AmbienceLoop.wav");
            var settings = EnsureSettings();

            foreach (var old in scene.GetRootGameObjects().Where(g => g.name == "Intro").ToList()) Object.DestroyImmediate(old);
            var root = new GameObject("Intro");
            SceneManager.MoveGameObjectToScene(root, scene);

            // ---- World references
            var director = Object.FindAnyObjectByType<MissionDirector>();
            Vector3 core = director != null && new SerializedObject(director).FindProperty("alertOrigin").objectReferenceValue is Transform t
                ? t.position : new Vector3(157f, 0f, 150f);
            var gate = Object.FindObjectsByType<SecurityBarrier>().FirstOrDefault(b => b.name == "CompoundGate");
            Vector3 gatePos = gate != null ? gate.transform.position : new Vector3(160f, 0f, 194f);
            var navigation = Object.FindAnyObjectByType<CityNavigation>();
            var staging = Object.FindObjectsByType<RaceMarker>().FirstOrDefault(m => m.Id == NightRunBuilder.RivalStagingId);
            var hud = Object.FindAnyObjectByType<MissionHud>();
            var brain = Object.FindAnyObjectByType<CinemachineBrain>();
            Vector3 signal = SignalHead(log);

            // ---- Lineup: in the crew garage (hero on the turntable, two cars in the bays facing the door)
            var garage = Object.FindAnyObjectByType<CrewGarage>();
            var slots = new VehicleSpawnPoint[3];
            for (int i = 0; i < 3; i++)
            {
                var go = new GameObject($"LineupSlot_{i}");
                go.transform.SetParent(root.transform, false);
                if (garage != null && garage.IntroSlots.Length == 3)
                    go.transform.SetPositionAndRotation(garage.IntroSlots[i].position, garage.IntroSlots[i].rotation);
                else go.transform.SetPositionAndRotation(LineupCentre + Vector3.right * (i - 1) * LineupSpacing, Quaternion.identity);
                slots[i] = go.AddComponent<VehicleSpawnPoint>();
            }
            if (garage == null) log.AppendLine("  WARNING: no CrewGarage in the scene; lineup falls back to W Avenue");

            // ---- Surveillance grade (weight driven by the Surveillance cue)
            var cctvProfile = SurveillanceProfile();
            var cctvGo = new GameObject("SurveillanceGrade");
            cctvGo.transform.SetParent(root.transform, false);
            var cctv = cctvGo.AddComponent<Volume>();
            cctv.isGlobal = true;
            cctv.priority = 50f;
            cctv.weight = 0f;
            cctv.sharedProfile = cctvProfile;

            // ---- Audio sources (routed to mixer groups at runtime)
            AudioSource Source(string name, bool loop)
            {
                var go = new GameObject(name);
                go.transform.SetParent(root.transform, false);
                var s = go.AddComponent<AudioSource>();
                s.playOnAwake = false;
                s.spatialBlend = 0f;
                s.loop = loop;
                // The garage line-up's engine loops fill the voice limit: the soundtrack must never be virtualised.
                s.priority = 0;
                return s;
            }
            var ambienceSource = Source("Audio_Ambience", true);
            var droneSource = Source("Audio_Drone", true);
            var pulseSource = Source("Audio_Pulse", true);
            var riserSource = Source("Audio_Riser", false);
            var hitSource = Source("Audio_Hits", false);
            var scanSource = Source("Audio_Scan", false);
            var effects = Source("Audio_Effects", false);

            // ---- Overlay
            var overlayGo = new GameObject("IntroOverlay");
            overlayGo.transform.SetParent(root.transform, false);
            var doc = overlayGo.AddComponent<UIDocument>();
            doc.panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>("Assets/_NeonRift/UI/NeonRiftPanelSettings.asset");
            doc.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/_NeonRift/UI/IntroOverlay.uxml");
            doc.sortingOrder = 100;
            var overlay = overlayGo.AddComponent<IntroOverlay>();
            overlayGo.SetActive(false);

            // ---- Shot cameras
            var shotRoot = new GameObject("Shots");
            shotRoot.transform.SetParent(root.transform, false);
            var cctvCam = Object.FindObjectsByType<SecurityCamera>().OrderBy(cam => Vector3.Distance(cam.transform.position, new Vector3(8f, 0f, -108f))).FirstOrDefault();
            Vector3 cctvHead = cctvCam != null ? cctvCam.transform.TransformPoint(new Vector3(0f, 5.25f, 0.9f)) : new Vector3(8.7f, 5.4f, -107.7f);
            var shots = Shots(core, gatePos, signal, cctvHead);
            var cameras = new List<CinemachineCamera>();
            foreach (var s in shots)
            {
                var go = new GameObject($"Shot_{s.Name}");
                go.transform.SetParent(shotRoot.transform, false);
                go.transform.SetPositionAndRotation(s.From, Quaternion.LookRotation(s.LookFrom - s.From));
                var cam = go.AddComponent<CinemachineCamera>();
                cam.Lens.FieldOfView = s.Fov;
                cam.Lens.NearClipPlane = 0.1f;
                cam.Lens.FarClipPlane = SkylineBackdrop.RequiredFarClip;
                cam.Priority = 0;
                go.AddComponent<Animator>();
                cameras.Add(cam);
            }
            shotRoot.SetActive(false);

            var pushGo = new GameObject("PushCamera");
            pushGo.transform.SetParent(root.transform, false);
            var push = pushGo.AddComponent<CinemachineCamera>();
            push.Lens.FarClipPlane = SkylineBackdrop.RequiredFarClip;
            push.Lens.NearClipPlane = 0.1f;
            pushGo.AddComponent<CinemachineHardLookAt>();
            pushGo.SetActive(false);

            // ---- Entry + director
            var entry = root.AddComponent<IntroSceneEntry>();
            var playable = root.AddComponent<PlayableDirector>();
            playable.playOnAwake = false;
            playable.extrapolationMode = DirectorWrapMode.Hold;
            playable.timeUpdateMode = DirectorUpdateMode.GameTime;

            var timeline = BuildTimeline(playable, shots, cameras, brain, entry, clips, ambience,
                                         ambienceSource, droneSource, pulseSource, riserSource, hitSource, scanSource);
            playable.playableAsset = timeline;

            var profiles = new[]
            {
                AssetDatabase.LoadAssetAtPath<RacerProfile>("Assets/_NeonRift/Data/Racing/Racer_Vex.asset"),
                AssetDatabase.LoadAssetAtPath<RacerProfile>("Assets/_NeonRift/Data/Racing/Racer_Kade.asset")
            };
            entry.EditorConfigure(playable, overlay, slots, shotRoot, push, new Light[0], cctv, ambienceSource, droneSource, effects,
                                  new[] { pulseSource, riserSource, hitSource, scanSource }, clips.Ignition, navigation, profiles,
                                  staging != null ? staging.transform : null, hud != null ? hud.GetComponent<UIDocument>() : null, TitleTime, HoldTime);
            if (garage != null)
            {
                // Confirm: from the title shot on W Avenue through the open door, round the hero, to Car Select's opening frame.
                var path = new[] { G(0.6f, 1.9f, -24f), G(1.4f, 1.6f, -14.5f), G(3.4f, 1.1f, -2.5f), G(GarageBuilder.OpeningPosition) };
                var look = new[] { G(0f, 1.3f, -6f), G(0f, 1.0f, -2f), G(0.8f, 0.8f, 1.6f), G(GarageBuilder.OpeningLook) };
                // Out of the bays, through the door, across the southbound lanes and north up W Avenue.
                // From the bay along its heading (angled in), through the door centre, out over the apron.
                Vector3[] Route(float side) => new[]
                {
                    G(side * 5.4f, 0f, -13.8f), G(side * 1.2f, 0f, -17f), G(side * 0.3f, 0f, -20f), G(0f, 0f, -23.5f),
                    new Vector3(1.2f, 0f, -246f), new Vector3(3.5f, 0f, -228f), new Vector3(3.5f, 0f, -170f), new Vector3(3.5f, 0f, -60f)
                };
                entry.EditorConfigureGarage(garage, path, look, GarageBuilder.OpeningFov, 4.6f, Route(-1f), Route(1f),
                                            new[] { 3.5f, 4.5f, 5f, 6f, 8f, 13f, 22f, 26f }, 1.7f);
            }
            entry.EditorConfigureCityLife(BuildCityLife(root.transform, playable, core, gatePos, log));
            RegisterInConfig(settings);
            log.AppendLine($"  {shots.Count} shots, timeline {Duration:0.0}s, title at {TitleTime:0.0}s, hold at {HoldTime:0.0}s; core {core:F0}, gate {gatePos:F0}, signal {signal:F1}");
            return log.ToString();
        }

        // ---------------- Shot table ----------------

        /// <summary>Garage-local point to world (the crew garage on W Avenue).</summary>
        private static Vector3 G(Vector3 local) => NightRunBuilder.GarageToWorld(local);
        private static Vector3 G(float x, float y, float z) => G(new Vector3(x, y, z));

        /// <summary>
        /// The story, about 84 s. Black → the city → down through the skyline → street life → the security grid and the
        /// Data Core → the grid watching W Avenue → the hidden garage: lights strike, three cars, crew preparation →
        /// engines, the door lifts, two cars roll out → montage as security notices → NEON RIFT / NIGHT RUN over the open
        /// garage door → Confirm pushes through the door to Car Select's opening frame.
        /// </summary>
        private static List<Shot> Shots(Vector3 core, Vector3 gate, Vector3 signal, Vector3 cctv)
        {
            Vector3 bay = new(-8.2f, 0f, -8.5f);
            Vector3 bayFront = Quaternion.Euler(0f, 152f, 0f) * Vector3.forward;
            var list = new List<Shot>
            {
                // 1–2. Out of black: the city from far away, then down through the skyline towards W Avenue.
                new("01_City", 0f, 9f, 0f, 40f, new Vector3(-700f, 320f, -1050f), new Vector3(-590f, 280f, -915f), new Vector3(200f, 60f, 150f), new Vector3(170f, 66f, 120f)),
                new("02_Descent", 9f, 16f, 2.2f, 48f, new Vector3(-260f, 180f, -560f), new Vector3(-62f, 34f, -352f), new Vector3(60f, 30f, -120f), new Vector3(8f, 8f, -220f)),
                // 3. Street level: the avenue's shopfronts and steam, then signals and signs on Market Street.
                new("03a_Avenue", 16f, 20.5f, 1.2f, 52f, new Vector3(-4.5f, 1.3f, -215f), new Vector3(-4.5f, 1.7f, -185f), new Vector3(-3f, 1.8f, -120f), new Vector3(-2f, 2.2f, -100f)),
                new("03b_Market", 20.5f, 25f, 0f, 50f, new Vector3(8.5f, 3.2f, -103f), new Vector3(16f, 2.8f, -102.5f), new Vector3(90f, 4f, -100f), new Vector3(112f, 4.5f, -99f)),
                // 4. Security infrastructure: a street camera turning, the compound gate.
                new("04a_Camera", 25f, 28f, 0f, 34f, cctv + new Vector3(1.6f, -1.4f, 2.6f), cctv + new Vector3(1.1f, -1.0f, 1.9f), cctv, cctv + Vector3.down * 0.05f),
                new("04b_Gate", 28f, 31f, 0f, 44f, gate + new Vector3(16f, 1.6f, 13f), gate + new Vector3(12f, 2.2f, 9f), gate + new Vector3(-2f, 3.4f, 0f), gate + new Vector3(-3f, 3.6f, 0f)),
                // 5. The Data Core facility and the core itself.
                new("05a_Facility", 31f, 35f, 0f, 50f, gate + new Vector3(50f, 30f, 51f), gate + new Vector3(32f, 23f, 33f), core + new Vector3(3f, 10f, 25f), core + new Vector3(1f, 8f, 10f)),
                new("05b_Core", 35f, 39.5f, 1.5f, 45f, core + new Vector3(16f, 4f, 22f), core + new Vector3(11f, 6f, -10f), core + new Vector3(0f, 3f, 0f), core + new Vector3(0f, 4f, 0f)),
                // 6. The grid watches W Avenue: a camera across the road on an anonymous roller door.
                new("06_Surveillance", 39.5f, 43.5f, 0f, 46f, new Vector3(8.6f, 7.6f, -273f), new Vector3(8.6f, 7.6f, -272f), new Vector3(-11f, 2f, -257f), new Vector3(-11f, 2f, -253f)),
                // 7. The hidden garage: the door from the street, then inside as the lights strike.
                new("07a_Exterior", 43.5f, 47f, 0f, 46f, new Vector3(6f, 1.0f, -232f), new Vector3(1.5f, 1.25f, -243f), new Vector3(-11f, 2.4f, -255f), new Vector3(-11.2f, 2.7f, -255f)),
                new("07b_Reveal", 47f, 52.5f, 0f, 50f, G(9f, 5.5f, 12.5f), G(6.5f, 4f, 10.5f), G(0f, 0.8f, -3f), G(0f, 0.7f, -4f)),
                // 8. Three cars: the hero's headlight, a slide along it, a rival in its bay.
                new("08a_Headlight", 52.5f, 55f, 0f, 34f, G(1.9f, 0.55f, 4.8f), G(1.25f, 0.66f, 3.6f), G(0.75f, 0.62f, 2.2f), G(GarageBuilder.OpeningLook)),
                new("08b_Slide", 55f, 57.5f, 0f, 40f, G(2.7f, 0.85f, -3.5f), G(2.7f, 0.95f, 2f), G(0.5f, 0.7f, -1.2f), G(0.4f, 0.75f, 2.6f)),
                new("08c_Bay", 57.5f, 60f, 0f, 40f, G(bay + bayFront * 5f + new Vector3(1.2f, 0.7f, 0f)), G(bay + bayFront * 4.4f + new Vector3(0.8f, 0.85f, 0.4f)), G(bay + Vector3.up * 0.6f), G(bay + Vector3.up * 0.7f)),
                // 9. Crew preparation: the planning wall (the city, the target), then ignition.
                new("09a_Planning", 60f, 62.5f, 0f, 42f, G(9.8f, 2.7f, 3.6f), G(10.6f, 2.85f, 4.6f), G(12.9f, 2.8f, 6f), G(12.9f, 2.85f, 6.2f)),
                new("09b_Ignition", 62.5f, 64f, 0f, 38f, G(0.45f, 0.5f, -5.6f), G(0.35f, 0.55f, -5.9f), G(0f, 0.55f, -2.4f), G(0f, 0.55f, -2.4f)),
                // 10. The door lifts onto the street; two cars roll out into W Avenue.
                new("10a_Door", 64f, 67.5f, 0f, 46f, G(4.5f, 1.6f, 2.5f), G(3.8f, 1.5f, 0.5f), G(0f, 2.2f, -18f), G(0f, 1.8f, -20f)),
                new("10b_RollOut", 67.5f, 71f, 0f, 52f, new Vector3(3.2f, 0.9f, -268f), new Vector3(2.6f, 1.05f, -265f), new Vector3(-9f, 1.1f, -256f), new Vector3(0f, 1f, -242f)),
                // 11. Montage: the grid notices.
                new("11a_Gate", 71f, 71.7f, 0f, 42f, gate + new Vector3(14f, 1.8f, 11f), gate + new Vector3(13f, 1.9f, 10f), gate + new Vector3(-3f, 3.4f, 0f), gate + new Vector3(-3f, 3.4f, 0f)),
                new("11b_Signal", 71.7f, 72.4f, 0f, 32f, signal + new Vector3(-3.2f, -1.4f, -7.5f), signal + new Vector3(-2.8f, -1.3f, -6.6f), signal, signal),
                new("11c_Core", 72.4f, 73.1f, 0f, 38f, core + new Vector3(15f, 2.5f, 19f), core + new Vector3(13f, 3f, 16f), core + new Vector3(0f, 4f, 0f), core + new Vector3(0f, 4.5f, 0f)),
                new("11d_Camera", 73.1f, 73.8f, 0f, 30f, cctv + new Vector3(1.4f, -0.8f, 2.2f), cctv + new Vector3(1.2f, -0.7f, 1.9f), cctv, cctv),
                new("11e_Avenue", 73.8f, 74.5f, 0f, 48f, new Vector3(3.5f, 1.2f, -236f), new Vector3(3.5f, 1.2f, -232f), new Vector3(3f, 1f, -180f), new Vector3(3f, 1f, -170f)),
                new("11f_Compound", 74.5f, 75.2f, 0f, 46f, gate + new Vector3(30f, 22f, 31f), gate + new Vector3(28f, 21f, 29f), core + new Vector3(0f, 8f, 0f), core + new Vector3(0f, 8f, 0f)),
                // 12. Title: across W Avenue, looking into the open, lit garage with the hero on the turntable.
                new("12_Title", TitleTime, Duration, 0f, 50f, new Vector3(1.2f, 1.05f, -262.5f), new Vector3(-0.8f, 1.15f, -259.2f), G(0f, 2.6f, -4f), G(0f, 2.4f, -2f)),
            };
            return list;
        }

        private static List<Cue> Cues()
        {
            var cues = new List<Cue>
            {
                new(IntroCueKind.Letterbox, 0f, Duration),
                new(IntroCueKind.FadeIn, 1.2f, 6f),
                new(IntroCueKind.SkipHint, 2f, TitleTime),
                new(IntroCueKind.Card, 25.5f, 29.8f, "IN NEON RIFT, INFORMATION IS POWER.", "AND THE GRID WATCHES EVERY STREET."),
                new(IntroCueKind.Card, 34.2f, 39.2f, "AT ITS HEART: THE SECTOR 7 DATA CORE.", "EVERY SECRET IN THE CITY PASSES THROUGH IT."),
                new(IntroCueKind.Surveillance, 39.5f, 43.5f, "UNUSUAL ACTIVITY  ·  W AVENUE  ·  UNIT 7"),
                new(IntroCueKind.Dip, 43.2f, 43.8f),
                new(IntroCueKind.Dip, 46.7f, 47.3f),
                new(IntroCueKind.GarageLights, 47.6f, 47.8f),
                new(IntroCueKind.Card, 48.4f, 52.2f, "TONIGHT, A CREW GOES IN FOR IT.", "EXTRACT THE PACKAGE. OUTRUN THE LOCKDOWN."),
                new(IntroCueKind.Headlights, 53f, 53.2f),
                new(IntroCueKind.Card, 60.2f, 63.6f, "THREE CARS. ONE RUN.", "NO SECOND CHANCE."),
                new(IntroCueKind.Ignition, 62.6f, 62.8f),
                new(IntroCueKind.DoorOpen, 64.2f, 64.4f),
                new(IntroCueKind.Departure, 65.6f, 65.8f),
                new(IntroCueKind.Surveillance, 73.1f, 73.8f, "VEHICLE MOVEMENT  ·  SECTOR 7 PERIMETER"),
                new(IntroCueKind.Flash, TitleTime, TitleTime + 0.35f),
                new(IntroCueKind.Title, TitleTime + 0.5f, Duration),
            };
            for (float t = 71f; t < TitleTime - 0.1f; t += 0.7f) cues.Add(new(IntroCueKind.Flash, t, t + 0.12f));
            return cues;
        }

        // ---------------- Timeline ----------------

        private static TimelineAsset BuildTimeline(PlayableDirector director, List<Shot> shots, List<CinemachineCamera> cameras, CinemachineBrain brain,
                                                   IntroSceneEntry entry, IntroAudioGenerator.Clips clips, AudioClip ambience,
                                                   AudioSource ambienceSource, AudioSource droneSource, AudioSource pulseSource,
                                                   AudioSource riserSource, AudioSource hitSource, AudioSource scanSource)
        {
            // Rebuilt in place so the asset keeps its GUID (the director references it).
            var timeline = AssetDatabase.LoadAssetAtPath<TimelineAsset>(TimelinePath);
            if (timeline == null)
            {
                timeline = ScriptableObject.CreateInstance<TimelineAsset>();
                AssetDatabase.CreateAsset(timeline, TimelinePath);
            }
            foreach (var track in timeline.GetRootTracks().ToList()) timeline.DeleteTrack(track);
            foreach (var sub in AssetDatabase.LoadAllAssetsAtPath(TimelinePath).OfType<AnimationClip>()) Object.DestroyImmediate(sub, true);
            timeline.editorSettings.frameRate = 60;

            // Cameras: one animation track per shot camera, then the Cinemachine track that cuts/blends between them.
            for (int i = 0; i < shots.Count; i++)
            {
                var s = shots[i];
                float from = s.Start - s.Blend;
                var anim = timeline.CreateTrack<AnimationTrack>(null, $"Move {s.Name}");
                anim.trackOffset = TrackOffset.ApplySceneOffsets;
                var clip = MoveClip(s, s.End - from, s.Blend);
                AssetDatabase.AddObjectToAsset(clip, timeline);
                var tc = anim.CreateClip(clip);
                tc.start = from;
                tc.duration = s.End - from;
                director.SetGenericBinding(anim, cameras[i].GetComponent<Animator>());
            }
            var cmTrack = timeline.CreateTrack<CinemachineTrack>(null, "Shots");
            for (int i = 0; i < shots.Count; i++)
            {
                var s = shots[i];
                var tc = cmTrack.CreateDefaultClip();
                tc.start = s.Start - s.Blend;
                tc.duration = s.End - tc.start;
                tc.displayName = s.Name;
                var shot = (CinemachineShot)tc.asset;
                shot.VirtualCamera.exposedName = GUID.Generate().ToString();
                director.SetReferenceValue(shot.VirtualCamera.exposedName, cameras[i]);
            }
            if (brain != null) director.SetGenericBinding(cmTrack, brain);

            // Story cues.
            var cueTrack = timeline.CreateTrack<IntroCueTrack>(null, "Cues");
            foreach (var c in Cues())
            {
                var tc = cueTrack.CreateClip<IntroCueClip>();
                tc.start = c.Start;
                tc.duration = c.End - c.Start;
                tc.displayName = c.Kind + (c.Text != null ? $": {c.Text}" : string.Empty);
                var asset = (IntroCueClip)tc.asset;
                asset.kind = c.Kind;
                asset.text = c.Text;
                asset.subtitle = c.Subtitle;
            }
            // Cue clips on one track may overlap (cards over shots, flashes during cuts); Timeline allows it on custom tracks.
            director.SetGenericBinding(cueTrack, entry);

            // Soundtrack.
            void Audio(string name, AudioSource source, params (AudioClip clip, float start, float end, bool loop)[] items)
            {
                var track = timeline.CreateTrack<AudioTrack>(null, name);
                foreach (var (clip, start, end, loop) in items)
                {
                    if (clip == null) continue;
                    var tc = track.CreateClip(clip);
                    tc.start = start;
                    tc.duration = end - start;
                    ((AudioPlayableAsset)tc.asset).loop = loop;
                }
                director.SetGenericBinding(track, source);
            }
            Audio("Ambience", ambienceSource, (ambience, 0f, Duration, true));
            // Soundtrack: the licensed trailer track supplied with the project, from the first frame to the end of the
            // title hold (falls back to the game's own theme if it is missing).
            var trailer = AssetDatabase.LoadAssetAtPath<AudioClip>(NeonRift.EditorTools.Audio.MissionAudioGenerator.IntroMusicPath);
            if (trailer != null)
            {
                Audio("Soundtrack", droneSource, (trailer, 0f, Mathf.Min(Duration, trailer.length), false));
                foreach (var tc in timeline.GetOutputTracks().First(t => t.name == "Soundtrack").GetClips())
                {
                    tc.easeInDuration = 0.4f;
                    tc.easeOutDuration = 2.5f;
                }
                Debug.Log($"[Intro] soundtrack: {trailer.name} ({trailer.length:0} s)");
            }
            else
            {
                const float bedStart = 0.5f, grooveStart = 62.6f;
                Audio("Score bed", droneSource, (clips.Drone, bedStart, TitleTime + 1f, true));
                Audio("Score groove", pulseSource, (clips.Pulse, grooveStart, Duration, true));
                // Both are loops of the same 8-bar theme: the groove joins as far into its loop as the bed has played.
                if (clips.Pulse != null)
                    foreach (var tc in timeline.GetOutputTracks().First(t => t.name == "Score groove").GetClips())
                        tc.clipIn = (grooveStart - bedStart) % clips.Pulse.length;
            }
            Audio("Riser", riserSource, (clips.Riser, TitleTime - 6f, TitleTime, false));
            var hits = new List<(AudioClip, float, float, bool)>
            {
                (clips.Impact, 31f, 34f, false), (clips.Impact, 47.6f, 50.6f, false)
            };
            for (float t = 71f; t < TitleTime - 0.1f; t += 0.7f) hits.Add((clips.Whoosh, t, t + 0.7f, false));
            hits.Add((clips.Impact, TitleTime, TitleTime + 3f, false));
            Audio("Hits", hitSource, hits.ToArray());
            Audio("Scan", scanSource, (clips.Scan, 39.7f, 43.7f, false));

            EditorUtility.SetDirty(timeline);
            AssetDatabase.SaveAssets();
            return timeline;
        }

        /// <summary>Eased move: position and look direction sampled into keys (look direction stored as a quaternion).</summary>
        private static AnimationClip MoveClip(Shot s, float length, float leadIn)
        {
            var clip = new AnimationClip { name = $"Intro_{s.Name}", frameRate = 60 };
            var px = new AnimationCurve(); var py = new AnimationCurve(); var pz = new AnimationCurve();
            var rx = new AnimationCurve(); var ry = new AnimationCurve(); var rz = new AnimationCurve(); var rw = new AnimationCurve();
            const int keys = 12;
            Quaternion previous = Quaternion.identity;
            for (int k = 0; k <= keys; k++)
            {
                float t = length * k / keys;
                // The move runs over the shot itself; during a blend lead-in the camera already moves at the start speed.
                float u = Mathf.Clamp01((t - leadIn) / Mathf.Max(0.01f, length - leadIn));
                float e = u * u * (3f - 2f * u);
                Vector3 p = Vector3.Lerp(s.From, s.To, e);
                Vector3 look = Vector3.Lerp(s.LookFrom, s.LookTo, e);
                var q = Quaternion.LookRotation(look - p, Vector3.up);
                if (k > 0 && Quaternion.Dot(q, previous) < 0f) q = new Quaternion(-q.x, -q.y, -q.z, -q.w);   // keep the shortest path
                previous = q;
                px.AddKey(t, p.x); py.AddKey(t, p.y); pz.AddKey(t, p.z);
                rx.AddKey(t, q.x); ry.AddKey(t, q.y); rz.AddKey(t, q.z); rw.AddKey(t, q.w);
            }
            clip.SetCurve(string.Empty, typeof(Transform), "localPosition.x", px);
            clip.SetCurve(string.Empty, typeof(Transform), "localPosition.y", py);
            clip.SetCurve(string.Empty, typeof(Transform), "localPosition.z", pz);
            clip.SetCurve(string.Empty, typeof(Transform), "localRotation.x", rx);
            clip.SetCurve(string.Empty, typeof(Transform), "localRotation.y", ry);
            clip.SetCurve(string.Empty, typeof(Transform), "localRotation.z", rz);
            clip.SetCurve(string.Empty, typeof(Transform), "localRotation.w", rw);
            clip.EnsureQuaternionContinuity();
            return clip;
        }

        // ---------------- Helpers ----------------

        /// <summary>
        /// A real signal head on W Avenue north of the lineup, read from the built intersection lens mesh (the highest lens
        /// cluster beside the avenue), so the shot always frames an actual signal.
        /// </summary>
        private static Vector3 SignalHead(System.Text.StringBuilder log)
        {
            var lens = Object.FindObjectsByType<MeshRenderer>()
                             .FirstOrDefault(r => r.sharedMaterial != null && r.sharedMaterial.name == "District_SignalNsRed");
            if (lens != null)
            {
                var points = lens.GetComponent<MeshFilter>().sharedMesh.vertices
                                 .Select(v => lens.transform.TransformPoint(v))
                                 .Where(p => p.x > 2f && p.x < 12f && p.y > 4.5f && p.z > LineupCentre.z + 60f && p.z < 150f)
                                 .ToList();
                if (points.Count > 0)
                {
                    float z = points.Min(p => p.z);
                    var head = points.Where(p => p.z < z + 1.5f).ToList();
                    var centre = new Vector3(head.Average(p => p.x), head.Average(p => p.y), head.Average(p => p.z));
                    log.AppendLine($"  signal shot frames the lens at {centre:F1}");
                    return centre;
                }
            }
            log.AppendLine("  no signal lens found beside W Avenue; signal shot looks along the avenue");
            return new Vector3(3f, 5.6f, -150f);
        }

        private static Light StageLight(Transform parent, string name, Vector3 position, Color colour, float intensity, float range)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(position, Quaternion.LookRotation(LineupCentre + Vector3.up * 0.6f - position));
            var l = go.AddComponent<Light>();
            l.type = LightType.Spot;
            l.spotAngle = 70f;
            l.innerSpotAngle = 35f;
            l.color = colour;
            l.intensity = intensity;
            l.range = range;
            l.shadows = LightShadows.None;
            l.enabled = false;
            return l;
        }

        private static VolumeProfile SurveillanceProfile()
        {
            string path = $"{DataFolder}/Intro_Surveillance.asset";
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, path);
            }
            foreach (var old in profile.components.ToList()) Object.DestroyImmediate(old, true);
            profile.components.Clear();
            var colour = profile.Add<ColorAdjustments>(true);
            colour.saturation.Override(-85f);
            colour.colorFilter.Override(new Color(0.75f, 1f, 0.82f));
            colour.contrast.Override(25f);
            var grain = profile.Add<FilmGrain>(true);
            grain.type.Override(FilmGrainLookup.Medium4);
            grain.intensity.Override(0.6f);
            var vignette = profile.Add<Vignette>(true);
            vignette.intensity.Override(0.45f);
            profile.Add<LensDistortion>(true).intensity.Override(-0.25f);
            foreach (var c in profile.components)
            {
                c.name = c.GetType().Name;
                AssetDatabase.AddObjectToAsset(c, profile);
            }
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            return profile;
        }

        private static IntroSettings EnsureSettings()
        {
            var settings = AssetDatabase.LoadAssetAtPath<IntroSettings>(SettingsPath);
            if (settings != null) return settings;
            settings = ScriptableObject.CreateInstance<IntroSettings>();
            settings.EditorConfigure(IntroSettings.Policy.FirstLaunchOnly, canSkip: true);
            AssetDatabase.CreateAsset(settings, SettingsPath);
            return settings;
        }

        private static void RegisterInConfig(IntroSettings settings)
        {
            foreach (string guid in AssetDatabase.FindAssets("t:GameConfig"))
            {
                var config = AssetDatabase.LoadAssetAtPath<GameConfig>(AssetDatabase.GUIDToAssetPath(guid));
                var so = new SerializedObject(config);
                so.FindProperty("introSettings").objectReferenceValue = settings;
                so.FindProperty("introScene").stringValue = "NightRun";
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(config);
            }
            AssetDatabase.SaveAssets();
        }
    }
}
