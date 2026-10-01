using UnityEngine;
using UnityEngine.SceneManagement;

namespace NeonRift.Game
{
    /// <summary>
    /// Guarantees the Bootstrap scene is present no matter which scene Play Mode starts from,
    /// so every scene is always entered through the game flow.
    /// </summary>
    public static class BootstrapLoader
    {
        public const string BootstrapSceneName = "Bootstrap";
        public const string BootstrapScenePath = "Assets/_NeonRift/Scenes/Bootstrap.unity";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void EnsureBootstrapLoaded()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).name == BootstrapSceneName)
                    return;

            if (SceneUtility.GetBuildIndexByScenePath(BootstrapScenePath) < 0)
            {
                Debug.LogWarning($"[Bootstrap] '{BootstrapScenePath}' is not in the build scene list; game flow is disabled.");
                return;
            }

            SceneManager.LoadScene(BootstrapSceneName, LoadSceneMode.Additive);
        }
    }
}
