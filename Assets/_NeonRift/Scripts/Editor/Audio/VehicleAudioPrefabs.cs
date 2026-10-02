using System.Text;
using NeonRift.Audio;
using NeonRift.EditorTools.Vehicles;
using UnityEditor;
using UnityEngine;

namespace NeonRift.EditorTools.Audio
{
    /// <summary>Adds and routes the <see cref="VehicleAudio"/> component on vehicle prefabs.</summary>
    public static class VehicleAudioPrefabs
    {
        /// <summary>Ensures a VehicleAudio on <paramref name="root"/>, routed to the mixer groups in the audio config.</summary>
        public static void AddAudio(GameObject root)
        {
            var config = AssetDatabase.LoadAssetAtPath<AudioMixerConfig>(AudioMixerBuilder.ConfigPath);
            if (!root.TryGetComponent(out VehicleAudio audio)) audio = root.AddComponent<VehicleAudio>();
            if (config == null) return;
            var so = new SerializedObject(audio);
            so.FindProperty("engineGroup").objectReferenceValue = config.Engine;
            so.FindProperty("tiresGroup").objectReferenceValue = config.Tires;
            so.FindProperty("sfxGroup").objectReferenceValue = config.Sfx;
            so.FindProperty("ambienceGroup").objectReferenceValue = config.Ambience;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        [MenuItem("Neon Rift/Audio/Add Vehicle Audio To Prefabs")]
        public static void UpgradePrefabsFromMenu() => Debug.Log(UpgradePrefabs());

        public static string UpgradePrefabs()
        {
            var log = new StringBuilder("[VehicleAudioPrefabs]\n");
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { VehiclePrefabBuilder.PrefabFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    if (root.GetComponent<NeonRift.Vehicles.VehicleController>() == null) continue;
                    AddAudio(root);
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    log.AppendLine("  " + path);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }
            return log.ToString();
        }
    }
}
