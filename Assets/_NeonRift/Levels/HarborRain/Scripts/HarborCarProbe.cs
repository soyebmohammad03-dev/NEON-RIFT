using System.Collections;
using System.Text;
using UnityEngine;

namespace NeonRift.HarborRain
{
    /// <summary>
    /// Development aid: measures what the Level 2 cars cost from the chase camera. For each unlockable car it hides the
    /// player's car, places a static copy of the car's gameplay prefab in its spot and records the triangles and draw
    /// calls the frame gains, with mesh LODs automatic and forced to the full-detail level, and saves a screenshot of
    /// each so the two can be compared by eye. Editor Play Mode only (UnityStats); writes Logs/HarborCarProbe.txt.
    /// </summary>
    public sealed class HarborCarProbe : MonoBehaviour
    {
        public static void Run() => new GameObject("HarborCarProbe").AddComponent<HarborCarProbe>();

#if UNITY_EDITOR
        private static (int tris, int calls) Stats() => (UnityEditor.UnityStats.triangles, UnityEditor.UnityStats.drawCalls);

        private IEnumerator Sample(System.Action<int, int> done)
        {
            long t = 0, c = 0;
            for (int i = 0; i < 10; i++) { yield return new WaitForEndOfFrame(); var s = Stats(); t += s.tris; c += s.calls; }
            done((int)(t / 10), (int)(c / 10));
        }

        private IEnumerator Start()
        {
            var restore = Application.runInBackground;
            Application.runInBackground = true;
            while (HarborRainEntry.Active == null || HarborRainEntry.Active.Mission == null) yield return null;
            var m = HarborRainEntry.Active.Mission;
            yield return new WaitForSeconds(1f);
            if (m.Current == HarborRainMission.Phase.Garage) m.ChooseCar(0);
            yield return new WaitForSeconds(2f);
            if (m.Cinematics.Playing) m.Cinematics.Skip();
            yield return new WaitForSeconds(1.5f);
            var player = HarborRainEntry.Active.Driving.PlayerVehicle;
            player.Body.linearVelocity = Vector3.zero;
            var report = new StringBuilder("Harbor Rain car cost from the chase camera (editor Play Mode, UnityStats; includes shadow passes)\n");
            var playerRenderers = player.GetComponentsInChildren<Renderer>();
            foreach (var r in playerRenderers) r.forceRenderingOff = true;
            yield return Sample((t, c) => report.AppendLine($"  empty frame: {t:N0} tris, {c} draw calls"));
            int baseTris = 0, baseCalls = 0;
            yield return Sample((t, c) => { baseTris = t; baseCalls = c; });
            foreach (var u in m.Garage.Unlockables)
            {
                if (u.vehicle == null || u.vehicle.GameplayPrefab == null) continue;
                var copy = Instantiate(u.vehicle.GameplayPrefab, player.transform.position, player.transform.rotation);
                foreach (var b in copy.GetComponentsInChildren<MonoBehaviour>()) b.enabled = false;
                foreach (var a in copy.GetComponentsInChildren<AudioSource>()) a.enabled = false;
                foreach (var rb in copy.GetComponentsInChildren<Rigidbody>()) rb.isKinematic = true;
                foreach (var col in copy.GetComponentsInChildren<Collider>()) col.enabled = false;
                var renderers = copy.GetComponentsInChildren<MeshRenderer>();
                int lodMeshes = 0;
                foreach (var r in renderers) if (r.TryGetComponent(out MeshFilter f) && f.sharedMesh != null && f.sharedMesh.lodCount > 1) lodMeshes++;
                foreach (var r in renderers) r.forceMeshLod = 0;
                yield return new WaitForSeconds(0.5f);
                int fullTris = 0, fullCalls = 0;
                yield return Sample((t, c) => { fullTris = t - baseTris; fullCalls = c - baseCalls; });
                ScreenCapture.CaptureScreenshot($"Logs/HarborCarProbe_{u.vehicle.Id}_full.png");
                yield return null;
                foreach (var r in renderers) r.forceMeshLod = -1;
                yield return new WaitForSeconds(0.5f);
                int autoTris = 0, autoCalls = 0;
                yield return Sample((t, c) => { autoTris = t - baseTris; autoCalls = c - baseCalls; });
                ScreenCapture.CaptureScreenshot($"Logs/HarborCarProbe_{u.vehicle.Id}_auto.png");
                yield return null;
                report.AppendLine($"  {u.vehicle.DisplayName}: {renderers.Length} renderers, {lodMeshes} with mesh LODs — full detail {fullTris:N0} tris / {fullCalls} calls; automatic LOD {autoTris:N0} tris / {autoCalls} calls");
                Destroy(copy);
                yield return null;
            }
            foreach (var r in playerRenderers) r.forceRenderingOff = false;
            System.IO.File.WriteAllText("Logs/HarborCarProbe.txt", report.ToString());
            Application.runInBackground = restore;
            Destroy(gameObject);
        }
#endif
    }
}
