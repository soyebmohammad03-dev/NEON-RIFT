using System.Collections.Generic;
using UnityEngine;

namespace NeonRift.Vehicles
{
    /// <summary>
    /// A car's sound. Referenced from <see cref="VehicleDefinition"/> and played by the audio module from the
    /// vehicle's telemetry, so any car can use any profile and no car has its own audio code.
    /// </summary>
    [CreateAssetMenu(menuName = "Neon Rift/Vehicles/Vehicle Audio Profile", fileName = "VehicleAudio")]
    public sealed class VehicleAudioProfile : ScriptableObject
    {
        [SerializeField] private EngineAudioSettings engine;
        [SerializeField] private ChassisAudioSettings chassis;
        [Tooltip("Where the sounds came from and under which licence.")]
        [SerializeField, TextArea(2, 6)] private string sourceNotes;

        public EngineAudioSettings Engine => engine;
        public ChassisAudioSettings Chassis => chassis;
        public string SourceNotes => sourceNotes;

#if UNITY_EDITOR
        public void EditorConfigure(EngineAudioSettings engineSettings, ChassisAudioSettings chassisSettings, string notes)
        {
            engine = engineSettings;
            chassis = chassisSettings;
            sourceNotes = notes;
        }
#endif

        /// <summary>Human-readable data problems; empty when the profile is usable.</summary>
        public List<string> Validate()
        {
            var p = new List<string>();
            var layers = engine.layers;
            if (layers == null || layers.Length == 0) { p.Add("No engine layers."); return p; }
            int on = 0, off = 0;
            foreach (var l in layers)
            {
                if (l.clip == null) p.Add($"Engine layer at {l.recordedRpm:0} rpm has no clip.");
                if (l.recordedRpm <= 0f) p.Add("Engine layer rpm must be positive.");
                if (l.onLoad) on++; else off++;
            }
            if (on == 0) p.Add("No on-load engine layers.");
            if (off == 0) p.Add("No off-load engine layers.");
            if (engine.minPitch >= 1f || engine.maxPitch <= 1f) p.Add("Pitch limits must bracket 1.");
            if (chassis.skidLoop == null) p.Add("No skid loop.");
            return p;
        }
    }
}
