using System.IO;
using NeonRift.EditorTools.District;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NeonRift.HarborRain.EditorTools
{
    /// <summary>
    /// Development aid (Harbor Rain 2.2): places the port truck in a temporary additive scene over the Harbor Rain
    /// level (its lighting and weather) and saves edit-mode views to Logs/HarborTruck/, then removes the scene so
    /// Harbor Rain stays untouched. For checking geometry only — Play Mode screenshots are the real proof.
    /// </summary>
    public static class HarborRainTruckPreview
    {
        private const string Dir = "Logs/HarborTruck";

        public static string Run(Vector3 at, float yaw, string tag)
        {
            Directory.CreateDirectory(Dir);
            if (SceneManager.GetActiveScene().path != HarborRainBuilder.ScenePath) EditorSceneManager.OpenScene(HarborRainBuilder.ScenePath);
            var temp = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            var log = new System.Text.StringBuilder();
            try
            {
                var go = HarborRainBuilder.PreviewTruck(temp, at, yaw, log);
                var rot = Quaternion.Euler(0f, yaw, 0f);
                Vector3 W(Vector3 l) => at + rot * l;
                void Shot(string name, Vector3 from, Vector3 look, float fov = 45f)
                {
                    var p = W(from);
                    var e = Quaternion.LookRotation(W(look) - p).eulerAngles;
                    log.AppendLine(ViewCapture.Capture(p, e, $"{Dir}/{tag}_{name}.png", 1600, 900, fov));
                }
                Shot("front34", new Vector3(7f, 2.4f, 16f), new Vector3(0f, 1.6f, 3f));
                Shot("side", new Vector3(16f, 1.8f, 0.5f), new Vector3(0f, 1.6f, 0.5f), 50f);
                Shot("rear34", new Vector3(-6f, 2.0f, -14f), new Vector3(0f, 1.2f, -4f));
                Shot("under_rear", new Vector3(3.5f, 0.7f, -9.5f), new Vector3(0.6f, 0.8f, -3.5f), 55f);
                Shot("legs", new Vector3(4.5f, 1.0f, 5.2f), new Vector3(0.6f, 0.9f, 2.8f), 50f);
                Object.DestroyImmediate(go);
            }
            catch (System.Exception e) { log.AppendLine("FAILED " + e); }
            finally { EditorSceneManager.CloseScene(temp, true); }
            File.WriteAllText($"{Dir}/{tag}_log.txt", log.ToString());
            return log.ToString();
        }
    }
}
