using UnityEngine;

namespace NeonRift.Missions
{
    /// <summary>Personal bests per mission (completed runs only), kept in PlayerPrefs.</summary>
    public static class MissionRecords
    {
        private const string Prefix = "NeonRift.Best.";

        public static string TimeKey(string missionId) => Prefix + missionId + ".time";
        public static string GradeKey(string missionId) => Prefix + missionId + ".grade";

        /// <summary>The best completed time, s, or 0 when there is none.</summary>
        public static float BestTime(string missionId) => PlayerPrefs.GetFloat(TimeKey(missionId), 0f);
        public static int BestScore(string missionId) => PlayerPrefs.GetInt(GradeKey(missionId), -1);

        /// <summary>
        /// Records a completed run. Returns whether it set a new best time; <paramref name="previous"/> is the best
        /// before this run (0 = first completion). The best score is kept separately (a slow, clean run can still
        /// improve it).
        /// </summary>
        public static bool Submit(string missionId, float time, int score, out float previous)
        {
            previous = BestTime(missionId);
            bool newBest = previous <= 0f || time < previous;
            if (newBest) PlayerPrefs.SetFloat(TimeKey(missionId), time);
            if (score > BestScore(missionId)) PlayerPrefs.SetInt(GradeKey(missionId), score);
            PlayerPrefs.Save();
            return newBest;
        }

        public static void Clear(string missionId)
        {
            PlayerPrefs.DeleteKey(TimeKey(missionId));
            PlayerPrefs.DeleteKey(GradeKey(missionId));
        }

        /// <summary>m:ss.cc</summary>
        public static string FormatTime(float seconds)
        {
            // Round to centiseconds first so 59.996 s reads 1:00.00, not 0:60.00.
            int cs = Mathf.RoundToInt(Mathf.Max(0f, seconds) * 100f);
            return $"{cs / 6000}:{cs % 6000 / 100:00}.{cs % 100:00}";
        }
    }
}
