using UnityEngine;

namespace NeonRift.Missions
{
    /// <summary>
    /// A playable mission: which scene hosts it and how it is presented.
    /// Objectives (reach / hack / collect / escape) are added when the mission runtime is built.
    /// </summary>
    [CreateAssetMenu(menuName = "Neon Rift/Missions/Mission Definition", fileName = "MissionDefinition")]
    public sealed class MissionDefinition : ScriptableObject
    {
        [Tooltip("Stable identifier used for saves and results. Never change after release.")]
        [SerializeField] private string id;
        [SerializeField] private string displayName;
        [SerializeField, TextArea(2, 6)] private string briefing;
        [Tooltip("Scene name (must be in Build Profiles scene list) that hosts this mission.")]
        [SerializeField] private string sceneName;
        [Tooltip("Development missions (test tracks) are hidden from players in release builds.")]
        [SerializeField] private bool developmentOnly;

        public string Id => id;
        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;
        public string Briefing => briefing;
        public string SceneName => sceneName;
        public bool DevelopmentOnly => developmentOnly;
    }
}
