using System.Collections.Generic;
using System.Linq;
using NeonRift.EditorTools.Audio;
using NeonRift.EditorTools.District;
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
    public static class IntroBuilder
    {
        public const string DataFolder = "Assets/_NeonRift/Data/Intro";
        public const string TimelinePath = DataFolder + "/Intro_NightRun.playable";
        public const string SettingsPath = "Assets/_NeonRift/Data/Config/IntroSettings.asset";

        /// <summary>Lineup on W Avenue, south of the spawn: three cars abreast, facing north.</summary>
        private static readonly Vector3 LineupCentre = new(0f, 0f, -285f);
        private const float LineupSpacing = 3.6f;

        public const float TitleTime = 52.5f, HoldTime = 56f, Duration = 62f;

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

            // ---- Lineup slots and stage lights
            var slots = new VehicleSpawnPoint[3];
            for (int i = 0; i < 3; i++)
            {
                var go = new GameObject($"LineupSlot_{i}");
                go.transform.SetParent(root.transform, false);
                go.transform.SetPositionAndRotation(LineupCentre + Vector3.right * (i - 1) * LineupSpacing, Quaternion.identity);
                slots[i] = go.AddComponent<VehicleSpawnPoint>();
            }
            var key = StageLight(root.transform, "KeyLight", LineupCentre + new Vector3(-7f, 4.5f, 6f), new Color(1f, 0.82f, 0.62f), 70f, 18f);
            var rim = StageLight(root.transform, "RimLight", LineupCentre + new Vector3(6f, 3.5f, -8f), new Color(0.45f, 0.75f, 1f), 90f, 16f);

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
            var shots = Shots(core, gatePos, signal);
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
            entry.EditorConfigure(playable, overlay, slots, shotRoot, push, new[] { key, rim }, cctv, ambienceSource, droneSource, effects,
                                  new[] { pulseSource, riserSource, hitSource, scanSource }, clips.Ignition, navigation, profiles,
                                  staging != null ? staging.transform : null, hud != null ? hud.GetComponent<UIDocument>() : null, TitleTime, HoldTime);
            RegisterInConfig(settings);
            log.AppendLine($"  {shots.Count} shots, timeline {Duration:0.0}s, title at {TitleTime:0.0}s, hold at {HoldTime:0.0}s; core {core:F0}, gate {gatePos:F0}, signal {signal:F1}");
            return log.ToString();
        }

        // ---------------- Shot table ----------------

        private static List<Shot> Shots(Vector3 core, Vector3 gate, Vector3 signal)
        {
            Vector3 L = LineupCentre;
            float right = LineupSpacing, left = -LineupSpacing;
            var list = new List<Shot>
            {
                // 1. Out of black: the city from far away.
                new("01_City", 0f, 7.5f, 0f, 40f, new Vector3(-700f, 320f, -1050f), new Vector3(-600f, 285f, -925f), new Vector3(200f, 60f, 150f), new Vector3(180f, 70f, 150f)),
                // 2. High over Sector 7: scale, skyline, roads, lights.
                new("02_Sector7", 7.5f, 14.5f, 2.5f, 45f, new Vector3(-300f, 200f, -470f), new Vector3(-185f, 172f, -345f), new Vector3(160f, 30f, 170f), new Vector3(150f, 40f, 170f)),
                // 3. Down into the streets, towards the industrial and commercial districts.
                new("03_Streets", 14.5f, 21f, 2f, 55f, new Vector3(0f, 18f, -250f), new Vector3(0f, 9.5f, -95f), new Vector3(0f, 6f, -100f), new Vector3(60f, 5f, 100f)),
                // 4. The lineup: headlight and wheel, a slide along the hero, the three cars from the front.
                new("04a_Headlight", 21f, 24f, 0f, 35f, L + new Vector3(right + 2f, 0.45f, 4.6f), L + new Vector3(right + 1.4f, 0.5f, 3.8f), L + new Vector3(right + 0.65f, 0.6f, 2.2f), L + new Vector3(right + 0.5f, 0.55f, 1.7f)),
                new("04b_Slide", 24f, 27f, 0f, 40f, L + new Vector3(1.8f, 0.85f, -4.5f), L + new Vector3(1.8f, 0.95f, 2f), L + new Vector3(0.6f, 0.7f, -1.5f), L + new Vector3(0.5f, 0.75f, 2.5f)),
                new("04c_Lineup", 27f, 30.5f, 0.8f, 42f, L + new Vector3(-6.5f, 1f, 9f), L + new Vector3(-4.6f, 1.15f, 7.4f), L + new Vector3(0f, 0.75f, 0f), L + new Vector3(0.2f, 0.8f, 0.5f)),
                // 5. The Data Core compound: gates, cameras, infrastructure.
                new("05_Facility", 30.5f, 37f, 0f, 50f, gate + new Vector3(50f, 30f, 51f), gate + new Vector3(30f, 22f, 31f), core + new Vector3(3f, 10f, 25f), core + new Vector3(1f, 8f, 10f)),
                // 7. The Data Core itself.
                new("07_DataCore", 37f, 42f, 1.5f, 45f, core + new Vector3(16f, 4f, 22f), core + new Vector3(11f, 6f, -10f), core + new Vector3(0f, 3f, 0f), core + new Vector3(0f, 4f, 0f)),
                // 8. A security camera watching the lineup.
                new("08_Surveillance", 42f, 46.5f, 0f, 52f, L + new Vector3(12.5f, 8.5f, 17f), L + new Vector3(12.5f, 8.5f, 17f), L + new Vector3(1.5f, 0.5f, 1f), L + new Vector3(-1.5f, 0.5f, 0f)),
                // 9. Quick cuts.
                new("09a_Ignition", 46.5f, 47.25f, 0f, 38f, L + new Vector3(0.4f, 0.5f, -5.6f), L + new Vector3(0.3f, 0.55f, -5.9f), L + new Vector3(0f, 0.55f, -2.4f), L + new Vector3(0f, 0.55f, -2.4f)),
                new("09b_Headlights", 47.25f, 48f, 0f, 34f, L + new Vector3(0.9f, 0.7f, 4.4f), L + new Vector3(0.85f, 0.72f, 4.1f), L + new Vector3(0.65f, 0.62f, 2.3f), L + new Vector3(0.65f, 0.62f, 2.3f)),
                new("09c_Wheels", 48f, 48.75f, 0f, 36f, L + new Vector3(left - 2.3f, 0.38f, 1f), L + new Vector3(left - 2.3f, 0.38f, 0.4f), L + new Vector3(left - 0.8f, 0.35f, 1.1f), L + new Vector3(left - 0.8f, 0.35f, 1.4f)),
                new("09d_Street", 48.75f, 49.5f, 0f, 60f, new Vector3(3f, 1.2f, -160f), new Vector3(3f, 1.3f, -125f), new Vector3(3f, 1f, -60f), new Vector3(3f, 1f, -30f)),
                new("09e_Gate", 49.5f, 50.25f, 0f, 42f, gate + new Vector3(16f, 1.6f, 13f), gate + new Vector3(13f, 1.8f, 11f), gate + new Vector3(-2f, 3.4f, 0f), gate + new Vector3(-3f, 3.4f, 0f)),
                new("09f_Signal", 50.25f, 51f, 0f, 32f, signal + new Vector3(-3.2f, -1.4f, -7.5f), signal + new Vector3(-2.8f, -1.3f, -6.6f), signal, signal),
                new("09g_Core", 51f, 51.75f, 0f, 38f, core + new Vector3(15f, 2.5f, 19f), core + new Vector3(13f, 3f, 16f), core + new Vector3(0f, 4f, 0f), core + new Vector3(0f, 4.5f, 0f)),
                new("09h_Avenue", 51.75f, 52.5f, 0f, 48f, L + new Vector3(5.5f, 0.7f, 27f), L + new Vector3(5.2f, 0.75f, 26f), L + new Vector3(0f, 0.9f, 0f), L + new Vector3(0f, 0.9f, 0f)),
                // 10. Title: low behind the hero car, looking up W Avenue into the city.
                new("10_Title", TitleTime, Duration, 0f, 50f, L + new Vector3(1.6f, 1.35f, -14.5f), L + new Vector3(1.2f, 1.55f, -11.5f), L + new Vector3(0f, 1f, 5f), L + new Vector3(0f, 2.4f, 45f)),
            };
            return list;
        }

        private static List<Cue> Cues() => new()
        {
            new(IntroCueKind.Letterbox, 0f, Duration),
            new(IntroCueKind.FadeIn, 0f, 5f),
            new(IntroCueKind.SkipHint, 1.5f, TitleTime),
            new(IntroCueKind.Dip, 20.6f, 21.4f),
            new(IntroCueKind.Headlights, 22f, 22.2f),
            new(IntroCueKind.Dip, 30.1f, 30.9f),
            new(IntroCueKind.Card, 31.2f, 36.5f, "IN NEON RIFT, INFORMATION IS POWER.", "WHOEVER RUNS THE GRID RUNS THE CITY."),
            new(IntroCueKind.Card, 37.4f, 41.7f, "TONIGHT, A CREW GOES FOR THE DATA CORE.", "ONE RUN. NO SECOND CHANCE."),
            new(IntroCueKind.Surveillance, 42f, 46.5f, "UNUSUAL ACTIVITY  ·  W AVENUE"),
            new(IntroCueKind.Ignition, 46.5f, 46.7f),
            new(IntroCueKind.Departure, 48f, 48.2f),
            new(IntroCueKind.Flash, 46.5f, 46.62f), new(IntroCueKind.Flash, 47.25f, 47.37f), new(IntroCueKind.Flash, 48f, 48.12f),
            new(IntroCueKind.Flash, 48.75f, 48.87f), new(IntroCueKind.Flash, 49.5f, 49.62f), new(IntroCueKind.Flash, 50.25f, 50.37f),
            new(IntroCueKind.Flash, 51f, 51.12f), new(IntroCueKind.Flash, 51.75f, 51.87f),
            new(IntroCueKind.Flash, TitleTime, TitleTime + 0.35f),
            new(IntroCueKind.Title, 53f, Duration),
        };

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
            Audio("Drone (placeholder music)", droneSource, (clips.Drone, 0.5f, TitleTime + 1f, true));
            Audio("Pulse (placeholder music)", pulseSource, (clips.Pulse, 46.5f, Duration, true));
            Audio("Riser", riserSource, (clips.Riser, TitleTime - 6f, TitleTime, false));
            var hits = new List<(AudioClip, float, float, bool)>
            {
                (clips.Impact, 21f, 24f, false), (clips.Impact, 30.9f, 33.9f, false)
            };
            for (float t = 46.5f; t < TitleTime - 0.1f; t += 0.75f) hits.Add((clips.Whoosh, t, t + 0.7f, false));
            hits.Add((clips.Impact, TitleTime, TitleTime + 3f, false));
            Audio("Hits", hitSource, hits.ToArray());
            Audio("Scan", scanSource, (clips.Scan, 42.2f, 46.2f, false));

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
