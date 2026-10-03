using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using NeonRift.Audio;
using NeonRift.EditorTools.Vehicles;
using NeonRift.Game;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;

namespace NeonRift.EditorTools.Audio
{
    /// <summary>
    /// Builds the game's AudioMixer: group tree, state snapshots and exposed user-volume parameters, then writes the
    /// <see cref="AudioMixerConfig"/> and hooks it into <see cref="GameConfig"/>. Unity has no public API for authoring
    /// mixers, so this editor-only tool drives the editor's mixer controller through reflection. If the mixer already
    /// exists it is reused and only the config is refreshed.
    ///
    /// Master [MasterVolume]
    ///   Gameplay [EffectsVolume] → Engine, Tires, SFX, Ambience   (snapshots act here)
    ///   Music [MusicVolume]      → Score                           (snapshots act here)
    ///   UI [UIVolume]
    /// </summary>
    public static class AudioMixerBuilder
    {
        public const string MixerPath = "Assets/_NeonRift/Audio/Mixer/NeonRiftMixer.mixer";
        public const string ConfigPath = "Assets/_NeonRift/Data/Audio/AudioMixerConfig.asset";

        private static readonly (string group, string parent, float db)[] Groups =
        {
            ("Gameplay", "Master", 0f),
            ("Engine", "Gameplay", 0f),
            ("Tires", "Gameplay", -3f),
            ("SFX", "Gameplay", 0f),
            ("Ambience", "Gameplay", -4f),
            ("Music", "Master", 0f),
            ("Score", "Music", 0f),
            ("UI", "Master", 0f),
        };

        /// <summary>Snapshot offsets in dB relative to Gameplay, per ducked group.</summary>
        private static readonly (MixerState state, string name, (string group, float db)[] offsets)[] Snapshots =
        {
            (MixerState.Gameplay, "Gameplay", new (string, float)[0]),
            (MixerState.Menu, "Menu", new[] { ("Engine", -12f), ("Tires", -20f), ("SFX", -6f), ("Ambience", -15f) }),
            (MixerState.Results, "Results", new[] { ("Engine", -18f), ("Tires", -25f), ("SFX", -8f), ("Ambience", -12f) }),
            (MixerState.Lockdown, "Lockdown", new[] { ("Engine", -2f), ("Ambience", -4f) }),
            (MixerState.Ducked, "Ducked", new[] { ("Engine", -8f), ("Tires", -8f), ("Ambience", -8f), ("Score", -10f) }),
        };

        private static readonly (AudioChannel channel, string group, string parameter)[] Exposed =
        {
            (AudioChannel.Master, "Master", "MasterVolume"),
            (AudioChannel.Effects, "Gameplay", "EffectsVolume"),
            (AudioChannel.Music, "Music", "MusicVolume"),
            (AudioChannel.UI, "UI", "UIVolume"),
        };

        private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        [MenuItem("Neon Rift/Audio/Build Audio Mixer")]
        public static void BuildFromMenu() => Debug.Log(Build());

        public static string Build()
        {
            var log = new StringBuilder("[AudioMixer]\n");
            VehiclePrefabBuilder.EnsureFolder(System.IO.Path.GetDirectoryName(MixerPath).Replace('\\', '/'));
            VehiclePrefabBuilder.EnsureFolder(System.IO.Path.GetDirectoryName(ConfigPath).Replace('\\', '/'));

            var mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(MixerPath);
            if (mixer == null)
            {
                mixer = CreateMixer(log);
                AssetDatabase.SaveAssets();
                AssetDatabase.ImportAsset(MixerPath);
                mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(MixerPath);
            }
            else log.AppendLine("  reusing existing mixer");

            AudioMixerGroup G(string n) => mixer.FindMatchingGroups(n).FirstOrDefault(g => g.name == n);
            var config = AssetDatabase.LoadAssetAtPath<AudioMixerConfig>(ConfigPath);
            if (config == null) { config = ScriptableObject.CreateInstance<AudioMixerConfig>(); AssetDatabase.CreateAsset(config, ConfigPath); }
            config.EditorConfigure(mixer, G("Engine"), G("Tires"), G("SFX"), G("Ambience"), G("Score"), G("UI"),
                Snapshots.Select(s => new AudioMixerConfig.StateSnapshot { state = s.state, snapshot = s.name }).ToArray(),
                Exposed.Select(e => new AudioMixerConfig.ChannelParameter { channel = e.channel, parameter = e.parameter }).ToArray());
            EditorUtility.SetDirty(config);

            var gameConfig = AssetDatabase.LoadAssetAtPath<GameConfig>("Assets/_NeonRift/Data/Config/GameConfig.asset");
            if (gameConfig != null)
            {
                var so = new SerializedObject(gameConfig);
                so.FindProperty("audioMixer").objectReferenceValue = config;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            AssetDatabase.SaveAssets();
            log.AppendLine($"  groups: {string.Join(", ", mixer.FindMatchingGroups(string.Empty).Select(g => g.name))}");
            log.AppendLine($"  snapshots: {string.Join(", ", Snapshots.Select(s => mixer.FindSnapshot(s.name) != null ? s.name : s.name + "(MISSING)"))}");
            foreach (var e in Exposed) log.AppendLine($"  exposed {e.parameter}: {(mixer.GetFloat(e.parameter, out float v) ? v.ToString("0.0") + " dB" : "MISSING")}");
            return log.ToString();
        }

        private static AudioMixer CreateMixer(StringBuilder log)
        {
            var controllerType = FindType("UnityEditor.Audio.AudioMixerController");
            var create = controllerType.GetMethod("CreateMixerControllerAtPath", Any);
            var controller = create.Invoke(null, new object[] { MixerPath });
            var groupType = FindType("UnityEditor.Audio.AudioMixerGroupController");
            var snapshotType = FindType("UnityEditor.Audio.AudioMixerSnapshotController");

            object master = controllerType.GetProperty("masterGroup", Any).GetValue(controller);

            // A controller created this way has no group view yet; give it one containing the master group.
            var viewsProp = controllerType.GetProperty("views", Any);
            var views = (Array)viewsProp.GetValue(controller);
            if (views == null || views.Length == 0)
            {
                var viewType = viewsProp.PropertyType.GetElementType();
                var view = Activator.CreateInstance(viewType);
                viewType.GetField("name", Any).SetValue(view, "View");
                var guidType = viewType.GetField("guids", Any).FieldType.GetElementType();
                var guids = Array.CreateInstance(guidType, 1);
                guids.SetValue(groupType.GetProperty("groupID", Any).GetValue(master), 0);
                viewType.GetField("guids", Any).SetValue(view, guids);
                var newViews = Array.CreateInstance(viewType, 1);
                newViews.SetValue(view, 0);
                viewsProp.SetValue(controller, newViews);
                controllerType.GetProperty("currentViewIndex", Any).SetValue(controller, 0);
            }
            var groups = new Dictionary<string, object> { ["Master"] = master };
            foreach (var (name, parent, _) in Groups)
            {
                var g = controllerType.GetMethod("CreateNewGroup", Any).Invoke(controller, new object[] { name, false });
                controllerType.GetMethod("AddChildToParent", Any).Invoke(controller, new[] { g, groups[parent] });
                controllerType.GetMethod("AddGroupToCurrentView", Any).Invoke(controller, new[] { g });
                groups[name] = g;
            }

            var setVolume = groupType.GetMethod("SetValueForVolume", Any);
            var targetProp = controllerType.GetProperty("TargetSnapshot", Any);
            var baseSnapshot = targetProp.GetValue(controller);
            ((UnityEngine.Object)baseSnapshot).name = Snapshots[0].name;
            foreach (var (name, _, db) in Groups) setVolume.Invoke(groups[name], new[] { controller, baseSnapshot, (object)db });

            for (int i = 1; i < Snapshots.Length; i++)
            {
                targetProp.SetValue(controller, baseSnapshot);
                controllerType.GetMethod("CloneNewSnapshotFromTarget", Any).Invoke(controller, new object[] { false });
                var snap = targetProp.GetValue(controller);
                ((UnityEngine.Object)snap).name = Snapshots[i].name;
                foreach (var (group, offset) in Snapshots[i].offsets)
                {
                    float baseDb = Groups.First(g => g.group == group).db;
                    setVolume.Invoke(groups[group], new[] { controller, snap, (object)(baseDb + offset) });
                }
            }
            targetProp.SetValue(controller, baseSnapshot);
            controllerType.GetProperty("startSnapshot", Any).SetValue(controller, baseSnapshot);

            // Exposed parameters for user volume settings.
            var pathType = FindType("UnityEditor.Audio.AudioGroupParameterPath");
            var getVolumeGuid = groupType.GetMethod("GetGUIDForVolume", Any);
            var addExposed = controllerType.GetMethod("AddExposedParameter", Any);
            foreach (var e in Exposed)
            {
                var guid = getVolumeGuid.Invoke(groups[e.group], null);
                var path = Activator.CreateInstance(pathType, Any, null, new[] { groups[e.group], guid }, null);
                addExposed.Invoke(controller, new[] { path });
            }
            var exposedProp = controllerType.GetProperty("exposedParameters", Any);
            var parameters = (Array)exposedProp.GetValue(controller);
            var paramType = parameters.GetType().GetElementType();
            var guidField = paramType.GetField("guid", Any);
            var nameField = paramType.GetField("name", Any);
            for (int i = 0; i < parameters.Length; i++)
            {
                var p = parameters.GetValue(i);
                var guid = guidField.GetValue(p);
                foreach (var e in Exposed)
                    if (getVolumeGuid.Invoke(groups[e.group], null).Equals(guid)) nameField.SetValue(p, e.parameter);
                parameters.SetValue(p, i);
            }
            exposedProp.SetValue(controller, parameters);

            EditorUtility.SetDirty((UnityEngine.Object)controller);
            log.AppendLine("  created mixer " + MixerPath);
            return (AudioMixer)controller;
        }

        // The mixer controller types are internal to the editor and their assembly moved between Unity versions, so
        // they are looked up by name across the loaded assemblies (editor-only, run on demand).
#pragma warning disable UAC0005
        private static Type FindType(string name) =>
            AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name)).FirstOrDefault(t => t != null)
            ?? throw new InvalidOperationException($"Editor type {name} not found (Unity version change?).");
#pragma warning restore UAC0005
    }
}
