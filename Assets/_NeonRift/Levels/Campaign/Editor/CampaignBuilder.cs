using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NeonRift.Game;
using NeonRift.Missions;
using NeonRift.Vehicles;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Random = System.Random;

namespace NeonRift.Campaign.EditorTools
{
    /// <summary>
    /// Builds the connected campaign's data (all under Levels/Campaign; nothing of the standalone game is changed):
    /// the two five-car selections, the Harbor Rain mission entry, the campaign intro settings, the campaign config
    /// (a copy of the game config with the campaign attached), the spoken lines (macOS speech synthesiser, the same
    /// method as Harbor Rain's radio) with their subtitles, the portal materials and sounds, the teaser frames, and the
    /// campaign bootstrap scene (a copy of Bootstrap whose GameRoot uses the campaign config). The campaign scenes are
    /// appended to the editor scene list after the existing ones, so Bootstrap stays first.
    /// </summary>
    public static class CampaignBuilder
    {
        public const string Root = "Assets/_NeonRift/Levels/Campaign";
        public const string DataDir = Root + "/Data";
        public const string AudioDir = Root + "/Generated/Audio";
        public const string TeaserDir = Root + "/Generated/Teaser";
        public const string MaterialDir = Root + "/Materials";
        public const string ScenePath = Root + "/Scenes/CampaignBootstrap.unity";
        public const string DefinitionPath = DataDir + "/CampaignDefinition.asset";
        public const string ConfigPath = DataDir + "/GameConfig_Campaign.asset";
        public const string HarborScenePath = "Assets/_NeonRift/Levels/HarborRain/Scenes/HarborRain.unity";
        private const string BootstrapPath = "Assets/_NeonRift/Scenes/Bootstrap.unity";
        private const string GameConfigPath = "Assets/_NeonRift/Data/Config/GameConfig.asset";
        private const string NightRunMissionPath = "Assets/_NeonRift/Data/Missions/Mission_NightRun.asset";
        private const string F40Path = "Assets/_NeonRift/Levels/HarborRain/Generated/Vehicles/PF_HR_FerrariF40LB/Vehicle_PF_HR_FerrariF40LB.asset";
        private const string F296Path = "Assets/_NeonRift/Levels/HarborRain/Generated/Vehicles/PF_HR_Ferrari296SpecialeA/Vehicle_PF_HR_Ferrari296SpecialeA.asset";
        private const int Rate = 44100;

        /// <summary>The campaign's spoken lines: one record per line, the audio is generated from exactly this text.</summary>
        public static readonly (string id, string speaker, string text, bool radio)[] Lines =
        {
            ("teaser", "INTERCEPTED · KOWLOON BAY", "Kowloon Bay control. The storm is closing the port tonight, but the shipment still moves. Whoever wins the city run gets the call.", true),
            ("portal", "THE CONTACT", "Good driving. You were noticed. There is a door open ahead of you. Drive through it.", false),
            ("arrival", "CONTROL", "Control to Driver. Signal locked. You came through clean. Welcome to Kowloon Bay.", true),
            ("unlock", "THE CONTACT", "You performed well in the city, so I have another operation for you. A storm, a secured port, and a container nobody is supposed to touch. Prepare your crew.", false),
            ("trailer_port", "THE CONTACT", "Kowloon Bay. The busiest container port on the coast. Tonight, the storm is on our side.", false),
            ("trailer_target", "THE CONTACT", "Somewhere in these stacks is container KX-7, hidden behind a false manifest.", false),
            ("trailer_crew", "THE CONTACT", "You drive. Control runs the job. Overwatch flies the drone.", false),
            ("trailer_security", "THE CONTACT", "Cameras, badge gates and patrols. Stay out of sight until the box is ours.", false),
            ("trailer_heist", "THE CONTACT", "Crack the manifest, bypass the gate, and the gantry crane lifts the container onto our truck.", false),
            ("trailer_alarm", "THE CONTACT", "The moment it moves, the alarm goes live, the port locks down, and the police come for you.", false),
            ("trailer_escape", "THE CONTACT", "Get to the east gate, override the power, and cross the coast road before the waves close it.", false),
            ("trailer_briefing", "THE CONTACT", "Bring the cargo out. Reach extraction. Don't get caught.", false),
            ("complete", "CONTROL", "Cargo's through, Driver. That's both jobs done, the city and the harbour. Control out.", true),
        };

        public static readonly string[] TeaserCaptions = { "KOWLOON BAY", "02:47  ·  CARGO MOVING TONIGHT", "TERMINAL ROAD", "SECURITY CHECKPOINT", "SEARCHLIGHTS SWEEPING", "STORM SURGE  ·  SEAWALL" };
        public static readonly string[] TeaserShots = { "t1_cranes", "t2_stacks", "t3_truck", "t4_gate", "t5_searchlight", "t6_surge" };

        // Night Run: the ENTRY ROAD heading out of the city, a 283 m clear straight (road graph + clearance casts).
        private static readonly Vector3 PortalStage = new(-3f, 0f, -347f);
        private const float PortalYaw = 180f, PortalDistance = 64f;

        [MenuItem("Neon Rift/Campaign/Build Campaign (assets, voice, bootstrap scene)")]
        public static string Build() => Build(regenerateVoice: true);

        public static string Build(bool regenerateVoice)
        {
            var report = new StringBuilder();
            foreach (var d in new[] { DataDir, AudioDir, TeaserDir, MaterialDir, Root + "/Scenes" }) Directory.CreateDirectory(d);
            AssetDatabase.Refresh();

            var nightRun = AssetDatabase.LoadAssetAtPath<MissionDefinition>(NightRunMissionPath);
            var baseConfig = AssetDatabase.LoadAssetAtPath<GameConfig>(GameConfigPath);
            var f40 = AssetDatabase.LoadAssetAtPath<VehicleDefinition>(F40Path);
            var f296 = AssetDatabase.LoadAssetAtPath<VehicleDefinition>(F296Path);
            if (nightRun == null || baseConfig == null) throw new InvalidOperationException("missing Night Run mission or game config");
            // The two Harbor Rain cars are local-only third-party models (not in the public repository, see
            // Docs/ThirdParty.md). Without them the campaign is built with the three Night Run cars.
            var level2Cars = new[] { f40, f296 }.Where(v => v != null).ToArray();
            if (level2Cars.Length < 2) report.AppendLine("NOTE: Harbor Rain cars missing locally (licensing: kept out of the public repository) — the selections show the Night Run cars only.");

            // Cars: the three Night Run cars always, then the two Harbor Rain cars.
            var nrCars = baseConfig.VehicleCatalog.Vehicles.Where(v => v != null).ToList();
            var all = new List<VehicleDefinition>(nrCars);
            all.AddRange(level2Cars);
            var level1 = Catalog("VehicleCatalog_CampaignLevel1", all);
            var level2 = Catalog("VehicleCatalog_CampaignLevel2", all);
            report.AppendLine($"selections: {all.Count} cars ({string.Join(", ", all.Select(v => v.Id))}); Harbor Rain cars: {(level2Cars.Length > 0 ? string.Join(", ", level2Cars.Select(v => v.Id)) : "none present")}");
            // The Harbor Rain cars' Car Select figures come from the same physics bench as the Night Run cars'; the
            // handling rating is authored (as it is for every car): the F40 is light and twitchy, the 296 planted.
            if (level2Cars.Length > 0) report.Append(NeonRift.EditorTools.Vehicles.VehiclePerformanceProbe.UpdateDisplayStats(level2Cars));
            foreach (var (car, handling) in new[] { (f40, 6.5f), (f296, 7.5f) })
            {
                if (car == null) continue;
                var cso = new SerializedObject(car);
                cso.FindProperty("displayStats").FindPropertyRelative("handlingRating").floatValue = handling;
                cso.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(car);
            }

            var harbor = HarborMission();
            var intro = Asset<IntroSettings>(DataDir + "/IntroSettings_Campaign.asset");
            intro.EditorConfigure(IntroSettings.Policy.FirstLaunchOnly, true);
            var so = new SerializedObject(intro);
            so.FindProperty("seenKey").stringValue = "NeonRift.Campaign.IntroSeen";
            so.ApplyModifiedPropertiesWithoutUndo();

            var def = Asset<CampaignDefinition>(DefinitionPath);
            def.EditorConfigure(nightRun, harbor, level1, level2, level2Cars);
            def.EditorConfigureLines(VoiceLines(regenerateVoice, report));
            var (ring, core, sparks) = PortalMaterials();
            def.EditorConfigurePortal(ring, core, sparks, Clip("Campaign_PortalOpen", 2.6f, PortalOpen), LoopClip("Campaign_PortalHum", 4f, PortalHum),
                                      Clip("Campaign_PortalEnter", 2.2f, PortalEnter), Clip("Campaign_PortalArrive", 2.4f, PortalArrive),
                                      PortalStage, PortalYaw, PortalDistance);
            var frames = TeaserFrames(report);
            def.EditorConfigureTeaser(frames, TeaserCaptions.Take(Math.Max(frames.Length, 0)).ToArray(), Clip("Campaign_TeaserStatic", 0.9f, Static), LoopClip("Campaign_TeaserDrone", 6f, Drone));
            EditorUtility.SetDirty(def);

            // The campaign config: the game config with the campaign intro, both missions and the campaign attached.
            if (AssetDatabase.LoadAssetAtPath<GameConfig>(ConfigPath) == null) AssetDatabase.CopyAsset(GameConfigPath, ConfigPath);
            var config = AssetDatabase.LoadAssetAtPath<GameConfig>(ConfigPath);
            var missions = baseConfig.Missions.Where(m => m != null).ToList();
            if (!missions.Contains(harbor)) missions.Add(harbor);
            config.EditorConfigureCampaign(intro, def, missions);
            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();

            BuildScene(config, report);
            AddToBuildList(report);
            AssetDatabase.SaveAssets();
            string text = report.ToString();
            Directory.CreateDirectory("Logs/Campaign");
            File.WriteAllText("Logs/Campaign/build_campaign.txt", text);
            Debug.Log("[Campaign] built:\n" + text);
            return text;
        }

        // ---------------- Data ----------------

        private static T Asset<T>(string path) where T : ScriptableObject
        {
            var a = AssetDatabase.LoadAssetAtPath<T>(path);
            if (a != null) return a;
            a = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(a, path);
            return a;
        }

        private static VehicleCatalog Catalog(string name, List<VehicleDefinition> cars)
        {
            var c = Asset<VehicleCatalog>($"{DataDir}/{name}.asset");
            var so = new SerializedObject(c);
            var list = so.FindProperty("vehicles");
            list.arraySize = cars.Count;
            for (int i = 0; i < cars.Count; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = cars[i];
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(c);
            return c;
        }

        /// <summary>Harbor Rain as a mission the flow can load (its rules live in the level; no Night Run objectives).</summary>
        private static MissionDefinition HarborMission()
        {
            var m = Asset<MissionDefinition>(DataDir + "/Mission_HarborRain.asset");
            var so = new SerializedObject(m);
            so.FindProperty("id").stringValue = "harbor_rain";
            so.FindProperty("displayName").stringValue = "Harbor Rain";
            so.FindProperty("tagline").stringValue = "Operation KX-7 · Kowloon Bay container port";
            so.FindProperty("briefing").stringValue = "Intercept container KX-7 in a secured port during a storm, evade port security and the police, cross the storm-surge coast road and reach extraction.";
            so.FindProperty("sceneName").stringValue = "HarborRain";
            so.FindProperty("developmentOnly").boolValue = false;
            so.FindProperty("maxRivals").intValue = 0;
            so.FindProperty("qualifyingPlaces").intValue = 0;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(m);
            return m;
        }

        // ---------------- Voice ----------------

        private static string VoiceFor(string speaker) => speaker switch
        {
            "THE CONTACT" => "Tessa",
            "CONTROL" => "Daniel",
            _ => "Reed (English (UK))"
        };

        /// <summary>The words as the synthesiser should say them (the subtitle keeps the written form).</summary>
        private static string Spoken(string text) => text.Replace("KX-7", "K X 7").Replace("\"", "");

        private static VoiceLine[] VoiceLines(bool regenerate, StringBuilder report)
        {
            var result = new List<VoiceLine>();
            int voiced = 0;
            foreach (var (id, speaker, text, radio) in Lines)
            {
                string path = $"{AudioDir}/Campaign_VO_{id}.wav";
                AudioClip clip = regenerate || !File.Exists(path) ? Speak(id, speaker, text, radio, path) : null;
                if (clip == null) clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                if (clip != null) voiced++;
                result.Add(new VoiceLine { id = id, speaker = speaker, text = text, clip = clip });
            }
            report.AppendLine($"voice: {voiced}/{Lines.Length} lines spoken (Tessa = THE CONTACT, Daniel = CONTROL, Reed = intercepted); every line has a subtitle");
            return result.ToArray();
        }

        private static AudioClip Speak(string id, string speaker, string text, bool radio, string path)
        {
            string raw = Path.Combine(Path.GetTempPath(), $"nr_campaign_vo_{id}.wav");
            try
            {
                if (File.Exists(raw)) File.Delete(raw);
                var psi = new System.Diagnostics.ProcessStartInfo("/usr/bin/say",
                    $"-v \"{VoiceFor(speaker)}\" -r {(radio ? 182 : 168)} -o \"{raw}\" --file-format=WAVE --data-format=LEI16@22050 \"{Spoken(text)}\"")
                { UseShellExecute = false, CreateNoWindow = true };
                using (var p = System.Diagnostics.Process.Start(psi)) p.WaitForExit(30000);
                if (!File.Exists(raw)) { Debug.LogWarning($"[Campaign] voice {id}: say produced no file"); return null; }
                var (samples, rate) = ReadWav(raw);
                File.Delete(raw);
                var rng = new Random(id.GetHashCode());
                int lead = rate / 10;
                var x = new float[samples.Length + lead * 3];
                double hp = 0, hpPrev = 0, lp = 0, lp2 = 0;
                double aHp = Math.Exp(-2 * Math.PI * (radio ? 320 : 90) / rate), aLp = 1 - Math.Exp(-2 * Math.PI * (radio ? 3200 : 9000) / rate);
                for (int i = 0; i < x.Length; i++)
                {
                    double s = i >= lead && i - lead < samples.Length ? samples[i - lead] : 0;
                    hp = aHp * (hp + s - hpPrev); hpPrev = s;
                    lp += (hp - lp) * aLp; lp2 += (lp - lp2) * aLp;
                    double v = radio ? Math.Tanh(lp2 * 2.2) * 0.8 + (rng.NextDouble() * 2 - 1) * 0.012 : lp2;
                    x[i] = (float)v;
                }
                if (!radio)
                {
                    // A little room: two soft early reflections, so the contact sounds present but not dry.
                    int d1 = rate * 37 / 1000, d2 = rate * 83 / 1000;
                    for (int i = x.Length - 1; i >= d2; i--) x[i] += x[i - d1] * 0.16f + x[i - d2] * 0.09f;
                }
                return Write(path, x, rate, false, 0.9f);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Campaign] voice {id} failed: {e.Message}");
                return null;
            }
        }

        private static (float[] samples, int rate) ReadWav(string path)
        {
            var bytes = File.ReadAllBytes(path);
            int rate = BitConverter.ToInt32(bytes, 24);
            int pos = 12;
            while (pos + 8 < bytes.Length)
            {
                string chunk = Encoding.ASCII.GetString(bytes, pos, 4);
                int size = BitConverter.ToInt32(bytes, pos + 4);
                if (chunk == "data")
                {
                    var s = new float[size / 2];
                    for (int i = 0; i < s.Length; i++) s[i] = BitConverter.ToInt16(bytes, pos + 8 + i * 2) / 32768f;
                    return (s, rate);
                }
                pos += 8 + size;
            }
            return (new float[0], rate);
        }

        // ---------------- Synthesised sounds ----------------

        private static AudioClip Clip(string name, float seconds, Action<float[]> fill)
        {
            var x = new float[(int)(seconds * Rate)];
            fill(x);
            return Write($"{AudioDir}/{name}.wav", x, Rate, false, 0.85f);
        }

        private static AudioClip LoopClip(string name, float seconds, Action<float[]> fill)
        {
            var x = new float[(int)(seconds * Rate)];
            fill(x);
            // Crossfade the ends so the loop is seamless.
            int f = Rate / 5;
            for (int i = 0; i < f; i++)
            {
                float k = i / (float)f;
                x[i] = x[i] * k + x[x.Length - f + i] * (1f - k);
            }
            Array.Resize(ref x, x.Length - f);
            return Write($"{AudioDir}/{name}.wav", x, Rate, true, 0.7f);
        }

        private static double Noise(Random r) => r.NextDouble() * 2 - 1;

        private static void PortalHum(float[] x)
        {
            var r = new Random(11);
            double lp = 0;
            for (int i = 0; i < x.Length; i++)
            {
                double t = i / (double)Rate;
                double trem = 0.75 + 0.25 * Math.Sin(2 * Math.PI * 1.5 * t);
                lp += (Noise(r) - lp) * 0.02;
                x[i] = (float)((Math.Sin(2 * Math.PI * 55 * t) * 0.5 + Math.Sin(2 * Math.PI * 110.5 * t) * 0.25 + Math.Sin(2 * Math.PI * 221 * t + Math.Sin(2 * Math.PI * 0.5 * t)) * 0.12) * trem + lp * 1.6);
            }
        }

        private static void PortalOpen(float[] x)
        {
            var r = new Random(12);
            double ph = 0, lp = 0, bp = 0;
            for (int i = 0; i < x.Length; i++)
            {
                double t = i / (double)Rate, u = t / (x.Length / (double)Rate);
                double f = 70 + 900 * u * u;
                ph += 2 * Math.PI * f / Rate;
                double n = Noise(r);
                double a = 0.02 + 0.6 * u * u;
                lp += (n - lp) * (0.02 + 0.3 * u);
                bp = lp - bp * 0.2;
                double crack = u > 0.9 ? Math.Exp(-(t - 0.9 * x.Length / (double)Rate) * 18) * n * 0.9 : 0;
                x[i] = (float)((Math.Sin(ph) * 0.35 + bp * 0.8) * a + crack);
            }
        }

        private static void PortalEnter(float[] x)
        {
            var r = new Random(13);
            double lp = 0;
            for (int i = 0; i < x.Length; i++)
            {
                double t = i / (double)Rate;
                double boom = Math.Sin(2 * Math.PI * (48 - 18 * t) * t) * Math.Exp(-t * 2.4) * 0.9;
                lp += (Noise(r) - lp) * (0.25 * Math.Exp(-t * 1.5) + 0.01);
                double whoosh = lp * Math.Exp(-t * 1.2) * 1.4;
                x[i] = (float)(boom + whoosh);
            }
        }

        private static void PortalArrive(float[] x)
        {
            var r = new Random(14);
            double lp = 0;
            double hit = 0.75;
            for (int i = 0; i < x.Length; i++)
            {
                double t = i / (double)Rate;
                double env = t < hit ? Math.Pow(t / hit, 3) : Math.Exp(-(t - hit) * 3.5);
                lp += (Noise(r) - lp) * (0.04 + 0.2 * env);
                double boom = t > hit ? Math.Sin(2 * Math.PI * 52 * (t - hit)) * Math.Exp(-(t - hit) * 3) * 0.8 : 0;
                x[i] = (float)(lp * env * 1.6 + boom);
            }
        }

        private static void Static(float[] x)
        {
            var r = new Random(15);
            for (int i = 0; i < x.Length; i++)
            {
                double t = i / (double)Rate;
                double env = Math.Exp(-t * 3.2);
                double crackle = r.NextDouble() < 0.002 ? Noise(r) * 2 : 0;
                x[i] = (float)((Noise(r) * 0.55 + crackle) * env);
            }
        }

        private static void Drone(float[] x)
        {
            var r = new Random(16);
            double lp = 0, lp2 = 0;
            for (int i = 0; i < x.Length; i++)
            {
                double t = i / (double)Rate;
                lp += (Noise(r) - lp) * 0.004;
                lp2 += (Noise(r) - lp2) * 0.05;
                double pad = Math.Sin(2 * Math.PI * 49 * t) * 0.35 + Math.Sin(2 * Math.PI * 73.4 * t + 0.6 * Math.Sin(2 * Math.PI * 0.2 * t)) * 0.22 + Math.Sin(2 * Math.PI * 98.2 * t) * 0.1;
                x[i] = (float)(pad + lp * 3.5 + lp2 * 0.25 * (0.5 + 0.5 * Math.Sin(2 * Math.PI * 0.15 * t)));
            }
        }

        private static AudioClip Write(string path, float[] x, int rate, bool loop, float peak)
        {
            float max = 1e-6f;
            foreach (var v in x) max = Mathf.Max(max, Mathf.Abs(v));
            float g = peak / max;
            using (var w = new BinaryWriter(File.Create(path)))
            {
                int bytes = x.Length * 2;
                w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + bytes); w.Write(Encoding.ASCII.GetBytes("WAVE"));
                w.Write(Encoding.ASCII.GetBytes("fmt ")); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
                w.Write(Encoding.ASCII.GetBytes("data")); w.Write(bytes);
                foreach (var v in x) w.Write((short)Mathf.Clamp(Mathf.RoundToInt(v * g * 32767f), -32768, 32767));
            }
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var importer = (AudioImporter)AssetImporter.GetAtPath(path);
            var s = importer.defaultSampleSettings;
            s.compressionFormat = AudioCompressionFormat.Vorbis;
            s.quality = 0.6f;
            s.loadType = x.Length > rate * 6 ? AudioClipLoadType.CompressedInMemory : AudioClipLoadType.DecompressOnLoad;
            importer.defaultSampleSettings = s;
            importer.forceToMono = true;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        }

        // ---------------- Portal and teaser ----------------

        private static (Material ring, Material core, Material sparks) PortalMaterials()
        {
            var shader = Shader.Find("NeonRift/Portal");
            if (shader == null) throw new InvalidOperationException("NeonRift/Portal shader not found");
            Material Mat(string name, float kind, Color a, Color b, float src, float dst, float speed)
            {
                string path = $"{MaterialDir}/{name}.mat";
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (m == null) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
                m.shader = shader;
                m.SetFloat("_Kind", kind);
                m.SetColor("_ColorA", a);
                m.SetColor("_ColorB", b);
                m.SetFloat("_SrcBlend", src);
                m.SetFloat("_DstBlend", dst);
                m.SetFloat("_Speed", speed);
                m.SetFloat("_Intensity", 1f);
                m.SetFloat("_Open", 1f);
                m.renderQueue = 3010;
                EditorUtility.SetDirty(m);
                return m;
            }
            var ring = Mat("M_PortalRing", 1f, new Color(0.25f, 2.8f, 4.2f), new Color(4.0f, 0.6f, 3.4f), 1f, 1f, 1f);
            var core = Mat("M_PortalCore", 0f, new Color(0.15f, 1.6f, 2.8f), new Color(2.4f, 0.35f, 2.2f), 1f, 10f, 1f);   // One, OneMinusSrcAlpha
            var sparks = Mat("M_PortalSparks", 2f, Color.white, Color.white, 1f, 1f, 1f);
            return (ring, core, sparks);
        }

        /// <summary>Teaser frames: Play Mode captures of the Harbor Rain level (HarborReviewShots "teaser") copied in as textures.</summary>
        private static Texture2D[] TeaserFrames(StringBuilder report)
        {
            var list = new List<Texture2D>();
            for (int i = 0; i < TeaserShots.Length; i++)
            {
                string src = $"Logs/HarborReview/teaser_{TeaserShots[i]}.png";
                string dst = $"{TeaserDir}/Teaser_{i + 1:00}_{TeaserShots[i]}.png";
                if (File.Exists(src)) File.Copy(src, dst, true);
                if (!File.Exists(dst)) continue;
                AssetDatabase.ImportAsset(dst, ImportAssetOptions.ForceUpdate);
                var ti = (TextureImporter)AssetImporter.GetAtPath(dst);
                ti.textureType = TextureImporterType.Default;
                ti.mipmapEnabled = false;
                ti.sRGBTexture = true;
                ti.wrapMode = TextureWrapMode.Clamp;
                ti.maxTextureSize = 2048;
                ti.textureCompression = TextureImporterCompression.Compressed;
                ti.npotScale = TextureImporterNPOTScale.None;
                ti.SaveAndReimport();
                list.Add(AssetDatabase.LoadAssetAtPath<Texture2D>(dst));
            }
            report.AppendLine($"teaser: {list.Count}/{TeaserShots.Length} frames captured from Harbor Rain");
            return list.ToArray();
        }

        // ---------------- Scene and build list ----------------

        private static void BuildScene(GameConfig config, StringBuilder report)
        {
            if (!File.Exists(ScenePath)) AssetDatabase.CopyAsset(BootstrapPath, ScenePath);
            var open = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(ScenePath);
            bool wasOpen = open.IsValid() && open.isLoaded;
            var scene = wasOpen ? open : EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                GameRoot root = null;
                foreach (var go in scene.GetRootGameObjects()) root ??= go.GetComponentInChildren<GameRoot>(true);
                if (root == null) throw new InvalidOperationException("CampaignBootstrap has no GameRoot");
                var so = new SerializedObject(root);
                so.FindProperty("config").objectReferenceValue = config;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                report.AppendLine($"scene: {ScenePath} (copy of Bootstrap; GameRoot → {ConfigPath})");
            }
            finally
            {
                if (!wasOpen) EditorSceneManager.CloseScene(scene, true);
            }
        }

        /// <summary>Appends the campaign scenes after the existing list (Bootstrap stays first for the standalone game).</summary>
        private static void AddToBuildList(StringBuilder report)
        {
            var scenes = EditorBuildSettings.scenes.ToList();
            bool changed = false;
            foreach (var path in new[] { ScenePath, HarborScenePath })
            {
                if (scenes.Any(s => s.path == path)) continue;
                scenes.Add(new EditorBuildSettingsScene(path, true));
                changed = true;
            }
            if (changed) EditorBuildSettings.scenes = scenes.ToArray();
            report.AppendLine("editor scene list: " + string.Join(", ", EditorBuildSettings.scenes.Select(s => Path.GetFileNameWithoutExtension(s.path))));
        }
    }
}
