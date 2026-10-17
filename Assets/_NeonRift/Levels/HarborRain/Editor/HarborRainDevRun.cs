using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace NeonRift.HarborRain.EditorTools
{
    /// <summary>
    /// Development aid (Harbor Rain 2.2): one call opens the Harbor Rain scene, enters Play Mode and — once the level is
    /// running (after the domain reload) — starts a <see cref="HarborReviewShots"/> section or the validator. The pending
    /// request survives the reload in SessionState. Results land in Logs/HarborReview (done.txt) / Logs/HarborRainValidation.txt.
    /// </summary>
    [InitializeOnLoad]
    public static class HarborRainDevRun
    {
        private const string Key = "NeonRift.HarborRain.DevRun";

        static HarborRainDevRun() => EditorApplication.update += Poll;

        [MenuItem("Neon Rift/Harbor Rain/Validate (Play Mode)")] private static void MenuValidate() => Debug.Log(Start("validate"));

        /// <summary><paramref name="what"/>: a review section ("truck", "heist", "perf", …) or "validate".</summary>
        public static string Start(string what)
        {
            if (EditorApplication.isPlaying) return "already playing";
            if (EditorSceneManager.GetActiveScene().path != HarborRainBuilder.ScenePath) EditorSceneManager.OpenScene(HarborRainBuilder.ScenePath);
            SessionState.SetString(Key, what);
            EditorApplication.EnterPlaymode();
            return "entering play for " + what;
        }

        private static void Poll()
        {
            string what = SessionState.GetString(Key, "");
            if (what.Length == 0 || !EditorApplication.isPlaying || HarborRainEntry.Active == null || HarborRainEntry.Active.Mission == null) return;
            SessionState.EraseString(Key);
            Application.runInBackground = true;
            if (what == "validate") HarborRainValidator.Run();
            else HarborReviewShots.Run(what);
            Debug.Log("[HarborRainDevRun] started " + what);
        }
    }
}
