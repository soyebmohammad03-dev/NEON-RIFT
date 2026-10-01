using System;
using UnityEngine;

namespace NeonRift.Core
{
    /// <summary>Provenance of a third-party asset. Kept with the data that uses it so release checks can filter.</summary>
    [Serializable]
    public struct AssetLicense
    {
        [Tooltip("SPDX-style id, e.g. CC-BY-4.0, CC-BY-NC-4.0, Sketchfab-Free-Standard.")]
        public string LicenseId;
        public string Author;
        public string SourceUrl;
        [Tooltip("False for NonCommercial licences or unverified terms. Such assets must not ship commercially.")]
        public bool CommercialUseAllowed;

        public string AttributionLine =>
            string.IsNullOrWhiteSpace(Author) ? LicenseId : $"{Author} — {LicenseId} — {SourceUrl}";
    }
}
