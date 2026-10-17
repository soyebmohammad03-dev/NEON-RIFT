using System;
using System.Collections.Generic;
using NeonRift.Vehicles;
using UnityEngine;

namespace NeonRift.HarborRain
{
    /// <summary>
    /// Harbor Rain's own garage and progression. The car list is the Night Run catalog's cars (always available, as
    /// they are in Level 1) plus Level 2's unlockable cars, which exist only here: they are not in the Night Run
    /// catalog and their unlock flags use their own PlayerPrefs keys (<c>NeonRift.HarborRain.*</c>), so nothing in
    /// Level 1's vehicle availability or records changes.
    /// </summary>
    [Serializable]
    public sealed class HarborGarage
    {
        public enum Requirement { CompleteOperation, GradeAOrBetter }

        [Serializable]
        public struct Unlockable
        {
            public VehicleDefinition vehicle;
            public Requirement requirement;
        }

        public const string Prefix = "NeonRift.HarborRain.";
        [SerializeField] private Unlockable[] unlockables = Array.Empty<Unlockable>();

        public IReadOnlyList<Unlockable> Unlockables => unlockables;

        public static string UnlockKey(string id) => Prefix + "unlocked." + id;
        public const string LastCarKey = Prefix + "lastCar";
        public const string BestGradeKey = Prefix + "bestGrade";
        public const string CompletionsKey = Prefix + "completions";

        public static bool IsUnlocked(VehicleDefinition v) => v != null && (PlayerPrefs.GetInt(UnlockKey(v.Id), 0) == 1 || (ExternalUnlock != null && ExternalUnlock(v)));

        /// <summary>
        /// Campaign bootstrap: cars the campaign has unlocked for Level 2 (qualified in Night Run). Null in the standalone
        /// level, where only Harbor Rain's own requirements apply.
        /// </summary>
        public static Func<VehicleDefinition, bool> ExternalUnlock;

        public static string RequirementText(Requirement r) => r switch
        {
            Requirement.CompleteOperation => "COMPLETE OPERATION KX-7",
            _ => "EARN GRADE A OR BETTER"
        };

        /// <summary>The cars the player can pick here: catalog cars, then Level 2 cars (locked ones flagged).</summary>
        public List<(VehicleDefinition car, bool locked, string requirement)> Cars(VehicleCatalog catalog)
        {
            var list = new List<(VehicleDefinition, bool, string)>();
            if (catalog != null) foreach (var v in catalog.Vehicles) if (v != null) list.Add((v, false, null));
            foreach (var u in unlockables)
                if (u.vehicle != null) list.Add((u.vehicle, !IsUnlocked(u.vehicle), RequirementText(u.requirement)));
            return list;
        }

        /// <summary>Records a finished run; returns the cars this run unlocked.</summary>
        public List<VehicleDefinition> RecordResult(bool extracted, string grade)
        {
            var newly = new List<VehicleDefinition>();
            if (!extracted) return newly;
            PlayerPrefs.SetInt(CompletionsKey, PlayerPrefs.GetInt(CompletionsKey, 0) + 1);
            int rank = GradeRank(grade);
            if (rank > PlayerPrefs.GetInt(BestGradeKey, -1)) PlayerPrefs.SetInt(BestGradeKey, rank);
            foreach (var u in unlockables)
            {
                if (u.vehicle == null || IsUnlocked(u.vehicle)) continue;
                bool met = u.requirement == Requirement.CompleteOperation || rank >= GradeRank("A");
                if (!met) continue;
                PlayerPrefs.SetInt(UnlockKey(u.vehicle.Id), 1);
                newly.Add(u.vehicle);
            }
            PlayerPrefs.Save();
            return newly;
        }

        public static int GradeRank(string grade) => grade switch { "S" => 3, "A" => 2, "B" => 1, "C" => 0, _ => -1 };

        /// <summary>Validation / testing: clears Harbor Rain's own progression keys (never Night Run's).</summary>
        public void ResetProgress()
        {
            foreach (var u in unlockables) if (u.vehicle != null) PlayerPrefs.DeleteKey(UnlockKey(u.vehicle.Id));
            PlayerPrefs.DeleteKey(BestGradeKey);
            PlayerPrefs.DeleteKey(CompletionsKey);
            PlayerPrefs.DeleteKey(LastCarKey);
            PlayerPrefs.Save();
        }
    }
}
