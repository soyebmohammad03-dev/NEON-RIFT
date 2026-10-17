using System;
using UnityEngine;

namespace NeonRift.Game
{
    /// <summary>
    /// The campaign save: one versioned JSON record in PlayerPrefs (the project's existing persistence), separate from
    /// the standalone Night Run and Harbor Rain keys, so playing the campaign never changes either standalone level.
    /// Missing, corrupt or future-version data falls back to a fresh campaign (logged) instead of throwing.
    /// </summary>
    public sealed class CampaignProgress
    {
        public const string Key = "NeonRift.Campaign.Save";
        public const int Version = 1;

        [Serializable]
        public sealed class Data
        {
            public int version = Version;
            public bool nightRunCompleted;
            public bool qualifiedForHarbor;
            public bool portalCompleted;
            public bool harborVehiclesUnlocked;
            public bool harborCompleted;
            public string level1Vehicle = "";
            public string level2Vehicle = "";
            public string nightRunGrade = "";
            public float nightRunTime;
            public int nightRunPosition;
            public int nightRunCrews;
            public string harborGrade = "";
            public float harborTime;
            public int nightRunClears;
            public int harborClears;
        }

        private readonly Func<string> read;
        private readonly Action<string> write;

        public Data State { get; private set; } = new();
        /// <summary>Why the last load fell back to a fresh save (null when it loaded cleanly or nothing was saved).</summary>
        public string LoadProblem { get; private set; }

        /// <summary>PlayerPrefs-backed progress.</summary>
        public CampaignProgress() : this(() => PlayerPrefs.GetString(Key, ""), s => { PlayerPrefs.SetString(Key, s); PlayerPrefs.Save(); }) { }

        /// <summary>Custom storage (tests).</summary>
        public CampaignProgress(Func<string> reader, Action<string> writer)
        {
            read = reader;
            write = writer;
        }

        public bool NightRunCompleted => State.nightRunCompleted;
        public bool QualifiedForHarbor => State.qualifiedForHarbor;
        public bool PortalCompleted => State.portalCompleted;
        public bool HarborVehiclesUnlocked => State.harborVehiclesUnlocked;
        public bool HarborCompleted => State.harborCompleted;
        /// <summary>Harbor Rain can be entered from the title (the player earned it once).</summary>
        public bool HarborAvailable => State.qualifiedForHarbor;

        public void Load()
        {
            LoadProblem = null;
            string json = read?.Invoke() ?? "";
            if (string.IsNullOrEmpty(json)) { State = new Data(); return; }
            try
            {
                var d = JsonUtility.FromJson<Data>(json);
                if (d == null) throw new FormatException("empty record");
                if (d.version > Version) throw new FormatException($"save version {d.version} is newer than {Version}");
                d.version = Version;
                d.level1Vehicle ??= "";
                d.level2Vehicle ??= "";
                d.nightRunGrade ??= "";
                d.harborGrade ??= "";
                // Flags imply their prerequisites (a hand-edited or partial save stays consistent).
                if (d.harborCompleted) d.harborVehiclesUnlocked = true;
                if (d.harborVehiclesUnlocked || d.portalCompleted) d.qualifiedForHarbor = true;
                if (d.qualifiedForHarbor) d.nightRunCompleted = true;
                State = d;
            }
            catch (Exception e)
            {
                LoadProblem = e.Message;
                Debug.LogWarning($"[Campaign] save data was invalid ({e.Message}); starting a fresh campaign.");
                State = new Data();
            }
        }

        public void Save() => write?.Invoke(JsonUtility.ToJson(State));

        public void Reset()
        {
            State = new Data();
            Save();
        }

        /// <summary>Night Run finished successfully (qualified or not).</summary>
        public void RecordNightRun(bool qualified, string vehicleId, string grade, float time, int position, int crews)
        {
            State.nightRunCompleted = true;
            State.nightRunClears++;
            if (qualified) State.qualifiedForHarbor = true;
            State.level1Vehicle = vehicleId ?? "";
            State.nightRunGrade = grade ?? "";
            State.nightRunTime = time;
            State.nightRunPosition = position;
            State.nightRunCrews = crews;
            Save();
        }

        public void MarkPortalCompleted()
        {
            State.portalCompleted = true;
            Save();
        }

        public void UnlockHarborVehicles()
        {
            if (!State.qualifiedForHarbor) return;
            State.harborVehiclesUnlocked = true;
            Save();
        }

        public void SetLevel2Vehicle(string vehicleId)
        {
            State.level2Vehicle = vehicleId ?? "";
            Save();
        }

        public void RecordHarbor(string grade, float time)
        {
            State.harborCompleted = true;
            State.harborClears++;
            State.harborGrade = grade ?? "";
            State.harborTime = time;
            Save();
        }

        /// <summary>Development: everything up to (not including) Harbor Rain, as if Night Run had been qualified.</summary>
        public void DevUnlockHarbor()
        {
            State.nightRunCompleted = State.qualifiedForHarbor = State.portalCompleted = State.harborVehiclesUnlocked = true;
            if (string.IsNullOrEmpty(State.nightRunGrade)) State.nightRunGrade = "DEV";
            Save();
        }
    }
}
