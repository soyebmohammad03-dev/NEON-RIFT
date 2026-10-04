using System;
using System.IO;
using UnityEngine;

namespace NeonRift.Game
{
    /// <summary>
    /// Command line for the player-build performance run: <c>-perfroute [output folder]</c> boots straight into the
    /// default mission with the first car and lets <c>PerfRouteRunner</c> drive the route and write its report.
    /// </summary>
    public static class PerfRouteArgs
    {
        public const string Flag = "-perfroute";

        public static bool TryGet(out string outputFolder)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (!string.Equals(args[i], Flag, StringComparison.OrdinalIgnoreCase)) continue;
                outputFolder = i + 1 < args.Length && !args[i + 1].StartsWith("-")
                    ? args[i + 1]
                    : Path.Combine(Application.persistentDataPath, "PerfRoute");
                return true;
            }
            outputFolder = null;
            return false;
        }
    }
}
