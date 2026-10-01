using System.Collections.Generic;
using System.Linq;
using NeonRift.Game;
using UnityEditor;
using UnityEngine;

namespace NeonRift.EditorTools
{
    /// <summary>Checks the wiring that would otherwise only fail at runtime: config, catalogs and build scenes.</summary>
    public static class ProjectValidator
    {
        [MenuItem("Neon Rift/Validate Project")]
        public static void ValidateFromMenu()
        {
            var problems = Validate();
            if (problems.Count == 0) Debug.Log("[Validator] Project OK.");
            else foreach (var p in problems) Debug.LogError("[Validator] " + p);
        }

        public static List<string> Validate()
        {
            var problems = new List<string>();
            var buildScenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToList();

            if (buildScenes.Count == 0 || buildScenes[0] != BootstrapLoader.BootstrapScenePath)
                problems.Add($"'{BootstrapLoader.BootstrapScenePath}' must be the first enabled build scene.");

            var configs = AssetDatabase.FindAssets("t:GameConfig");
            if (configs.Length != 1)
            {
                problems.Add($"Expected exactly one GameConfig asset, found {configs.Length}.");
                return problems;
            }

            var config = AssetDatabase.LoadAssetAtPath<GameConfig>(AssetDatabase.GUIDToAssetPath(configs[0]));
            var buildSceneNames = new HashSet<string>(buildScenes.Select(System.IO.Path.GetFileNameWithoutExtension));
            foreach (var scene in config.ReferencedScenes())
                if (string.IsNullOrWhiteSpace(scene) || !buildSceneNames.Contains(scene))
                    problems.Add($"Scene '{scene}' referenced by GameConfig is not an enabled build scene.");

            if (config.VehicleCatalog == null) problems.Add("GameConfig has no VehicleCatalog.");
            else problems.AddRange(config.VehicleCatalog.Validate().Select(p => "VehicleCatalog: " + p));

            var missionIds = new HashSet<string>();
            foreach (var mission in config.Missions)
            {
                if (mission == null) { problems.Add("GameConfig has an empty mission slot."); continue; }
                if (string.IsNullOrWhiteSpace(mission.Id)) problems.Add($"Mission '{mission.name}' has no id.");
                else if (!missionIds.Add(mission.Id)) problems.Add($"Duplicate mission id '{mission.Id}'.");
            }
            if (config.DefaultMission == null) problems.Add("GameConfig has no playable default mission.");

            return problems;
        }
    }
}
