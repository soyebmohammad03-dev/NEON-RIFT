using System.Collections;
using UnityEngine;

namespace NeonRift.HarborRain
{
    /// <summary>
    /// Development aid: runs one full wave cycle on a seawall section with the car held short of it and saves Play Mode
    /// screenshots (HUD included) from the chase camera, a side view along the wall and a wide high angle at each stage —
    /// offshore swell, steepening face, curling lip, plunge, impact, overtopping, flood, drain and calm — to
    /// <c>Logs/HarborWaveShots/</c>. Not part of the mission.
    /// </summary>
    public sealed class HarborWaveShots : MonoBehaviour
    {
        private static bool restoreBackground;
        private static int sectionIndex = 1;
        private static bool driveThrough, probe;
        private Camera shotCam;

        /// <summary>Diagnostic: at the overtopping moment, capture the chase view with each water layer hidden in turn.</summary>
        public static void Probe(int section = 1) { Run(section, true); probe = true; }

        public static void Run(int section = 1, bool drive = false)
        {
            probe = false;
            sectionIndex = section;
            driveThrough = drive;
            restoreBackground = Application.runInBackground;
            Application.runInBackground = true;
            System.IO.Directory.CreateDirectory("Logs/HarborWaveShots");
            new GameObject("HarborWaveShots").AddComponent<HarborWaveShots>();
        }

        private IEnumerator Capture(string name, Vector3? from = null, Vector3? at = null)
        {
            if (from.HasValue)
            {
                shotCam.transform.SetPositionAndRotation(from.Value, Quaternion.LookRotation(at.Value - from.Value));
                shotCam.enabled = true;
            }
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot($"Logs/HarborWaveShots/{name}.png");
            yield return null;
            shotCam.enabled = false;
            yield return null;
        }

        private IEnumerator Views(string name, HarborWaves.Section s)
        {
            float c = s.road.center.x;
            yield return Capture(name + "_a_chase");
            yield return Capture(name + "_b_side", new Vector3(c - 78f, 4.5f, 289.5f), new Vector3(c - 5f, 2.5f, 304f));
            yield return Capture(name + "_c_wide", new Vector3(c - 70f, 20f, 236f), new Vector3(c + 5f, 1f, 318f));
            yield return Capture(name + "_d_sea", new Vector3(c - 115f, 24f, 410f), new Vector3(c, 0f, 300f));
        }

        private IEnumerator Start()
        {
            while (HarborRainEntry.Active == null || HarborRainEntry.Active.Mission == null) yield return null;
            var m = HarborRainEntry.Active.Mission;
            var main = Camera.main;
            // A copy of the main camera (same URP data and post-processing) without the brain, listener or tag.
            var copy = Instantiate(main.gameObject);
            copy.name = "ShotCamera";
            copy.tag = "Untagged";
            foreach (var b in copy.GetComponents<Behaviour>())
                if (b is not Camera && b.GetType().Name != "UniversalAdditionalCameraData") Destroy(b);
            for (int i = copy.transform.childCount - 1; i >= 0; i--) Destroy(copy.transform.GetChild(i).gameObject);
            shotCam = copy.GetComponent<Camera>();
            shotCam.depth = main.depth + 10;
            shotCam.fieldOfView = 55f;
            shotCam.enabled = false;

            yield return new WaitForSeconds(1f);
            if (m.Current == HarborRainMission.Phase.Garage) m.ChooseCar(0);
            yield return new WaitForSeconds(2f);
            if (m.Cinematics.Playing) m.Cinematics.Skip();
            var storm = FindAnyObjectByType<StormController>();
            if (storm != null) storm.Escalate(1f, 1f);
            var s = m.Waves.Sections[sectionIndex];
            var car = HarborRainEntry.Active.Driving.PlayerVehicle;
            var cam = HarborRainEntry.Active.Driving.ChaseCamera;
            car.Teleport(new Vector3(s.road.xMin - 12f, 0.3f, 276f), Quaternion.Euler(0f, 90f, 0f));   // at the HOLD SHORT marker
            car.Body.linearVelocity = Vector3.zero;
            if (cam != null) cam.Snap();
            yield return new WaitForSeconds(3f);
            yield return Views("00_calm", s);

            m.Waves.Trigger(sectionIndex);
            while (s.state != HarborWaves.State.Building) yield return null;
            yield return new WaitForSeconds(3.5f);
            yield return Views("01_offshore_swell", s);
            while (s.state != HarborWaves.State.Approach) yield return null;
            yield return new WaitForSeconds(3.5f);
            yield return Views("02_steepening", s);
            while (s.distance > 24f) yield return null;
            yield return Views("03_curling", s);
            while (s.distance > 8f) yield return null;
            yield return Views("04_plunge", s);
            if (driveThrough)
            {
                car.Teleport(new Vector3(s.road.center.x - 6f, 0.3f, 276f), Quaternion.Euler(0f, 90f, 0f));
                car.Body.linearVelocity = car.transform.forward * 12f;
            }
            while (s.state != HarborWaves.State.Impact) yield return null;
            yield return Views("05_impact", s);
            while (s.state != HarborWaves.State.Washing) yield return null;
            yield return new WaitForSeconds(0.4f);
            if (probe)
            {
                Time.timeScale = 0f;
                yield return Capture("P0_all");
                var layers = new (string name, Renderer r)[]
                {
                    ("P1_no_wallspray", s.plume.GetComponent<Renderer>()), ("P2_no_overtop", s.overtop), ("P3_no_breaker", s.breaker.GetComponent<Renderer>()),
                    ("P4_no_mist", s.mist.GetComponent<Renderer>()), ("P5_no_plunge", s.breaker.transform.parent.Find("PlungeSplash").GetComponent<Renderer>()),
                    ("P6_no_crest", s.breaker.transform.parent.Find("CrestSpray").GetComponent<Renderer>()), ("P7_no_droplets", s.droplets.GetComponent<Renderer>()),
                    ("P8_no_flood", s.flood)
                };
                foreach (var (name, r) in layers)
                {
                    bool was = r.enabled;
                    r.forceRenderingOff = true;
                    yield return Capture(name);
                    r.forceRenderingOff = false;
                }
                if (s.flash != null) { s.flash.enabled = false; yield return Capture("P9_no_flash"); }
                Time.timeScale = 1f;
            }
            yield return Views("06_overtopping", s);
            yield return new WaitForSeconds(0.9f);
            yield return Views("07_flood_crossing", s);
            yield return new WaitForSeconds(1.6f);
            yield return Views("08_flood_slowing", s);
            while (s.state != HarborWaves.State.Receding) yield return null;
            yield return new WaitForSeconds(2f);
            yield return Views("09_draining", s);
            while (s.state != HarborWaves.State.Calm) yield return null;
            yield return new WaitForSeconds(2f);
            yield return Views("10_after", s);
            System.IO.File.WriteAllText("Logs/HarborWaveShots/done.txt", $"done {Time.time:0.0} peak push {m.Waves.PeakPush:0.00}");
            Application.runInBackground = restoreBackground;
            m.Garage.ResetProgress();
            Destroy(shotCam.gameObject);
            Destroy(gameObject);
        }
    }
}
