using UnityEngine;

namespace NeonRift.Missions
{
    /// <summary>
    /// The debrief grade of a finished run: pace against the mission's par time, how quiet the job stayed (heat),
    /// the crew finishing order and clean driving (hard contacts), as a 0–100 score and a letter. Pure logic.
    /// </summary>
    public readonly struct MissionGrade
    {
        public const float PaceWeight = 40f, HeatWeight = 25f, PositionWeight = 20f, CleanWeight = 15f;
        /// <summary>Hard contacts at which the clean-driving part reaches zero.</summary>
        public const int ContactLimit = 6;

        public readonly int Score;
        public readonly string Letter;
        public readonly float Pace, Quiet, Position, Clean;

        private MissionGrade(float pace, float quiet, float position, float clean)
        {
            Pace = pace;
            Quiet = quiet;
            Position = position;
            Clean = clean;
            Score = Mathf.RoundToInt(pace + quiet + position + clean);
            Letter = LetterFor(Score);
        }

        /// <param name="elapsed">Run time, s.</param>
        /// <param name="par">Par time, s: full pace marks at or under par, none at 1.6 × par.</param>
        /// <param name="heat">Heat at the end, 0–1.</param>
        /// <param name="position">Finishing position (1 = first); 0 when there was no race.</param>
        /// <param name="field">Cars in the race, player included.</param>
        /// <param name="contacts">Hard contacts during the run.</param>
        public static MissionGrade Evaluate(float elapsed, float par, float heat, int position, int field, int contacts)
        {
            par = Mathf.Max(1f, par);
            float pace = PaceWeight * Mathf.Clamp01((1.6f * par - elapsed) / (0.6f * par));
            float quiet = HeatWeight * (1f - Mathf.Clamp01(heat));
            // No race (or a race of one): full position marks.
            float place = field <= 1 || position <= 0 ? 1f : Mathf.Clamp01((field - position) / (float)(field - 1));
            float clean = CleanWeight * Mathf.Clamp01(1f - contacts / (float)ContactLimit);
            return new MissionGrade(pace, quiet, PositionWeight * place, clean);
        }

        public static string LetterFor(int score) => score >= 90 ? "S" : score >= 75 ? "A" : score >= 60 ? "B" : score >= 45 ? "C" : "D";
    }
}
