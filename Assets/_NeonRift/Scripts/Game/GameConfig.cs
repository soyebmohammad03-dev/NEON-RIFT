using System.Collections.Generic;
using NeonRift.Missions;
using NeonRift.Vehicles;
using UnityEngine;

namespace NeonRift.Game
{
    /// <summary>Top-level data the composition root needs: content catalogs and front-end scene names.</summary>
    [CreateAssetMenu(menuName = "Neon Rift/Game Config", fileName = "GameConfig")]
    public sealed class GameConfig : ScriptableObject
    {
        [Header("Scenes")]
        [SerializeField] private string frontendScene = "Frontend";
        [SerializeField] private string carSelectScene = "CarSelect";

        [Header("Content")]
        [SerializeField] private VehicleCatalog vehicleCatalog;
        [SerializeField] private List<MissionDefinition> missions = new();
        [Tooltip("Mission launched from Car Select until a mission-select screen exists.")]
        [SerializeField] private MissionDefinition defaultMission;

        [Header("Transitions")]
        [SerializeField, Min(0f)] private float fadeSeconds = 0.35f;

        public string FrontendScene => frontendScene;
        public string CarSelectScene => carSelectScene;
        public VehicleCatalog VehicleCatalog => vehicleCatalog;
        public IReadOnlyList<MissionDefinition> Missions => missions;
        public MissionDefinition DefaultMission => IsPlayable(defaultMission) ? defaultMission : null;
        public float FadeSeconds => fadeSeconds;

        public static bool IsPlayable(MissionDefinition mission)
        {
            if (mission == null) return false;
            return !mission.DevelopmentOnly || Debug.isDebugBuild;
        }

        public MissionDefinition FindMissionForScene(string sceneName)
        {
            foreach (var m in missions)
                if (m != null && m.SceneName == sceneName) return m;
            return null;
        }

        /// <summary>Every scene name the config refers to; used by the project validator.</summary>
        public IEnumerable<string> ReferencedScenes()
        {
            yield return frontendScene;
            yield return carSelectScene;
            foreach (var m in missions)
                if (m != null) yield return m.SceneName;
        }
    }
}
