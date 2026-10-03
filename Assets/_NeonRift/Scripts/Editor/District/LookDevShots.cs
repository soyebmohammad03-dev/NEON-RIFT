using System.Collections.Generic;
using System.IO;
using NeonRift.Gameplay;
using UnityEditor;
using UnityEngine;

namespace NeonRift.EditorTools.District
{
    /// <summary>
    /// Captures a fixed set of Night Run viewpoints in Play Mode (lamps and the HUD are only live at runtime), so
    /// before/after look-dev shots are comparable. Street shots move the player car; aerial shots park the camera.
    /// Start it from the menu or with <c>LookDevShots.Begin("Docs/Screenshots/Environment/after")</c> while playing.
    /// Writes <c>_done.txt</c> into the folder when finished.
    /// </summary>
    public static class LookDevShots
    {
        private readonly struct Shot
        {
            public readonly string Name;
            public readonly Vector3 Position;
            public readonly Vector3 Euler;
            public readonly bool Aerial;

            public Shot(string name, Vector3 position, Vector3 euler, bool aerial)
            {
                Name = name;
                Position = position;
                Euler = euler;
                Aerial = aerial;
            }
        }

        // Street shots: player pose (x, y, z) and heading. Aerial shots: camera pose.
        private static readonly Shot[] Shots =
        {
            new("01_spawn_avenue", new Vector3(3.5f, 0.2f, -280f), new Vector3(0f, 0f, 0f), false),
            new("02_kowloon_market", new Vector3(-197.5f, 0.2f, -95f), new Vector3(0f, 0f, 0f), false),
            new("03_service_alley", new Vector3(162f, 0.3f, -85f), new Vector3(0f, 0f, 0f), false),
            new("04_spire_boulevard", new Vector3(30f, 0.2f, 476f), new Vector3(0f, 90f, 0f), false),
            new("05_harbor_dock_road", new Vector3(563.5f, 0.2f, -130f), new Vector3(0f, 180f, 0f), false),
            new("06_harbor_skyway", new Vector3(470f, 8.3f, 58f), new Vector3(0f, 90f, 0f), false),
            new("07_lowtown", new Vector3(-3.5f, 0.2f, -360f), new Vector3(0f, 180f, 0f), false),
            new("08_aerial_southwest", new Vector3(-520f, 170f, -880f), new Vector3(14f, 38f, 0f), true),
            new("09_aerial_skyline_north", new Vector3(60f, 85f, -640f), new Vector3(5f, 12f, 0f), true),
            new("10_aerial_harbor", new Vector3(900f, 120f, -420f), new Vector3(12f, -58f, 0f), true),
        };

        private const float SettleSeconds = 3f;
        private const float WriteSeconds = 1.5f;

        private static string folder;
        private static int index;
        private static double nextTime;
        private static bool captured;
        private static readonly List<string> Written = new();

        [MenuItem("Neon Rift/Night Run/Capture Look-Dev Shots (Play Mode)")]
        private static void BeginFromMenu() => Begin("Docs/Screenshots/Environment/latest");

        public static string Begin(string outputFolder)
        {
            if (!EditorApplication.isPlaying) return "enter Play Mode in the Night Run first";
            if (Object.FindAnyObjectByType<MissionSceneEntry>() == null) return "no MissionSceneEntry in the scene";
            folder = Path.GetFullPath(outputFolder);
            Directory.CreateDirectory(folder);
            File.Delete(Path.Combine(folder, "_done.txt"));
            Application.runInBackground = true;
            index = -1;
            captured = true;
            nextTime = 0;
            Written.Clear();
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            return $"capturing {Shots.Length} shots into {folder}";
        }

        private static void Tick()
        {
            if (!EditorApplication.isPlaying)
            {
                EditorApplication.update -= Tick;
                return;
            }
            if (EditorApplication.timeSinceStartup < nextTime) return;

            if (!captured)
            {
                ScreenCapture.CaptureScreenshot(Path.Combine(folder, Shots[index].Name + ".png"));
                Written.Add(Shots[index].Name);
                captured = true;
                nextTime = EditorApplication.timeSinceStartup + WriteSeconds;
                return;
            }

            index++;
            var entry = Object.FindAnyObjectByType<MissionSceneEntry>();
            if (index >= Shots.Length || entry == null)
            {
                Finish(entry);
                return;
            }
            Pose(entry, Shots[index]);
            captured = false;
            nextTime = EditorApplication.timeSinceStartup + SettleSeconds;
        }

        private static void Pose(MissionSceneEntry entry, Shot shot)
        {
            var camera = Camera.main;
            var brain = camera.GetComponent<Unity.Cinemachine.CinemachineBrain>();
            if (shot.Aerial)
            {
                if (brain != null) brain.enabled = false;
                camera.transform.SetPositionAndRotation(shot.Position, Quaternion.Euler(shot.Euler));
                return;
            }
            if (brain != null) brain.enabled = true;
            entry.PlayerVehicle.Teleport(shot.Position, Quaternion.Euler(shot.Euler));
            entry.ChaseCamera.Snap();
        }

        private static void Finish(MissionSceneEntry entry)
        {
            EditorApplication.update -= Tick;
            var camera = Camera.main;
            if (camera != null && camera.TryGetComponent(out Unity.Cinemachine.CinemachineBrain brain)) brain.enabled = true;
            if (entry != null && entry.ChaseCamera != null) entry.ChaseCamera.Snap();
            File.WriteAllText(Path.Combine(folder, "_done.txt"), string.Join("\n", Written));
            Debug.Log($"[LookDev] {Written.Count} shots written to {folder}");
        }
    }
}
