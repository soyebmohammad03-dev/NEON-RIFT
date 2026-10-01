using UnityEngine;

namespace NeonRift.EditorTools.Vehicles
{
    /// <summary>
    /// Editor-only recipe that turns a third-party car model into a Neon Rift vehicle prefab.
    /// Keywords are matched case-insensitively against each renderer's hierarchy path and material names.
    /// </summary>
    [CreateAssetMenu(menuName = "Neon Rift/Editor/Vehicle Model Setup", fileName = "Setup_Vehicle")]
    public sealed class VehicleModelSetup : ScriptableObject
    {
        public GameObject sourceModel;
        public string prefabName = "PF_Vehicle_";

        [Tooltip("Euler correction so the model faces +Z with +Y up.")]
        public Vector3 bodyRotation;
        [Tooltip("Scale the model so the tyre outer diameter matches this (metres). 0 keeps the source scale.")]
        [Min(0f)] public float targetWheelDiameter;

        [Header("Wheels")]
        [Tooltip("Parts that belong to the wheels (rims, tyres, discs, calipers). Split per corner.")]
        public string[] wheelKeywords = { "rim", "tyre", "tire", "brake" };
        [Tooltip("Wheel parts that steer but do not spin (calipers).")]
        public string[] staticWheelKeywords = { "caliper" };
        [Tooltip("Motion-blurred rim variants; hidden at rest.")]
        public string[] motionBlurWheelKeywords = { "blur" };

        [Header("Body")]
        [Tooltip("Parts to hide (damage overlays, helper meshes, detached animation parts).")]
        public string[] hiddenKeywords = { "damage" };
        [Tooltip("Lamp parts; split into head/tail by their position along Z.")]
        public string[] lightKeywords = { "light" };
    }
}
