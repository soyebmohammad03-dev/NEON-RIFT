using System.Collections;
using UnityEngine;

namespace NeonRift.HarborRain
{
    /// <summary>
    /// Development aid: drives the running level through its key moments and saves Play Mode screenshots (HUD
    /// included) to <c>Logs/HarborRainTour/</c> for visual review — the chase view at the berth, terminal, east gate,
    /// coast road and extraction, each puzzle, a roadblock and a full wave impact. Not part of the mission.
    /// </summary>
    public sealed class HarborRainTour : MonoBehaviour
    {
        private static bool restoreBackground;

        public static void Run()
        {
            restoreBackground = Application.runInBackground;
            Application.runInBackground = true;
            System.IO.Directory.CreateDirectory("Logs/HarborRainTour");
            new GameObject("HarborRainTour").AddComponent<HarborRainTour>();
        }

        private IEnumerator Shot(string name, float settle = 1.2f)
        {
            yield return new WaitForSeconds(settle);
            ScreenCapture.CaptureScreenshot($"Logs/HarborRainTour/{name}.png");
            yield return null;
            yield return null;
        }

        private IEnumerator Place(HarborRainMission m, Vector3 p, float yaw, float settle = 1.5f)
        {
            var car = HarborRainEntry.Active.Driving.PlayerVehicle;
            car.Teleport(p + Vector3.up * 0.3f, Quaternion.Euler(0f, yaw, 0f));
            car.Body.linearVelocity = Vector3.zero;
            var cam = HarborRainEntry.Active.Driving.ChaseCamera;
            if (cam != null) cam.Snap();
            yield return new WaitForSeconds(settle);
        }

        private IEnumerator Start()
        {
            while (HarborRainEntry.Active == null || HarborRainEntry.Active.Mission == null) yield return null;
            var m = HarborRainEntry.Active.Mission;
            yield return new WaitForSeconds(1f);
            if (m.Current == HarborRainMission.Phase.Garage) { yield return Shot("00_garage", 0.3f); m.ChooseCar(0); }
            yield return new WaitForSeconds(3.5f);
            yield return Shot("01_arrival_reveal", 0f);
            if (m.Cinematics.Playing) m.Cinematics.Skip();
            yield return new WaitForSeconds(1.5f);
            yield return Shot("02_spawn_checkpoint");

            yield return Place(m, new Vector3(-150f, 0f, 230f), 270f);
            yield return Shot("03_quay_road_ship_cranes");
            yield return Place(m, new Vector3(-360f, 0f, 255f), 300f);
            yield return Shot("04_berth_apron_ship");
            yield return Place(m, new Vector3(90f, 0f, 20f), 0f);
            yield return Shot("05_terminal_lane");
            yield return Place(m, new Vector3(-400f, 0f, -180f), 90f);
            yield return Shot("06_industrial");

            // Puzzles, each opened at its station.
            var car = HarborRainEntry.Active.Driving.PlayerVehicle;
            yield return Place(m, m.DebugPoint("manifestPoint").position, 90f);
            m.OpenPuzzle("Manifest");
            yield return Shot("07_puzzle_manifest", 0.6f);
            var mp = (ManifestPuzzle)m.ActivePuzzle;
            mp.Choose(mp.Answer);
            yield return new WaitForSeconds(1.5f);
            yield return Place(m, m.DebugPoint("securityGatePoint").position, 0f);
            m.OpenPuzzle("Breach");
            yield return Shot("08_puzzle_badge_reels", 0.8f);
            var reels = (ReelPuzzle)m.ActivePuzzle;
            while (reels.Status == HarborPuzzle.State.Running) { if (reels.Showing == reels.Target[reels.Active]) reels.Lock(); yield return null; }
            yield return Shot("09_security_gate_open", 1.5f);
            yield return Place(m, m.DebugPoint("shipmentPoint").position, 0f);
            m.OpenPuzzle("Acquire");
            yield return Shot("10_puzzle_twistlocks", 0.8f);
            m.Hud.ShowPuzzle(null);   // look past the console at the spreader coming down (puzzle still running)
            yield return Shot("10a_spreader_descending", 2.5f);
            m.Hud.ShowPuzzle(m.ActivePuzzle);
            var lp = (LockPuzzle)m.ActivePuzzle;
            bool signalShot = false, liftShot = false;
            float engageT = Time.time;
            while (lp.Status == HarborPuzzle.State.Running)
            {
                if (lp.Stage == 0 && lp.RingInWindow) lp.LockRing();
                if (lp.Stage == 1) { lp.TouchSteer = Mathf.Clamp(-lp.Needle * 4f, -1f, 1f); if (!signalShot && lp.SignalProgress > 0.4f) { signalShot = true; ScreenCapture.CaptureScreenshot("Logs/HarborRainTour/11_puzzle_seal_signal.png"); } }
                m.ForceInteract = lp.Stage == 2;
                if (lp.Stage == 2 && !liftShot && lp.LiftProgress > 0.6f) { liftShot = true; m.Cinematics.enabled = true; }
                yield return null;
            }
            m.ForceInteract = false;
            // The lift beat (puzzle closed: the HUD is clear of the console).
            yield return Shot("12a_lift_beat_start", 0.4f);
            yield return Shot("12b_lift_beat_high", 1.4f);
            yield return Shot("12_theft_alarm", 1.6f);

            // Roadblock (whichever was chosen) seen from 60 m.
            yield return new WaitForSeconds(2f);
            car.Teleport(new Vector3(500f, 0.3f, 230f), Quaternion.Euler(0f, 90f, 0f));
            yield return new WaitForSeconds(28f);   // the roadblock reveals itself 25 s after the theft at the latest
            yield return Place(m, new Vector3(540f, 0f, m.ActiveBlockade == 0 ? 230f : m.ActiveBlockade == 1 ? 80f : -50f), 90f);
            yield return Shot("13_roadblock_reroute", 1.5f);

            yield return Place(m, m.DebugPoint("overridePoint").position, 90f);
            m.OpenPuzzle("Override");
            yield return Shot("14_puzzle_power_override", 0.8f);
            ((PowerPuzzle)m.ActivePuzzle).DebugSolve();
            yield return Shot("15_east_gate_open", 1.5f);

            // Coast road and a wave impact, from the car held short of section 1.
            var sec = m.Waves.Sections[1];
            yield return Place(m, new Vector3(sec.road.xMin - 12f, 0f, 276f), 90f);   // at the HOLD SHORT marker
            yield return Shot("16_coast_road");
            m.Waves.Trigger(1);
            while (sec.state != HarborWaves.State.Approach) yield return null;
            yield return Shot("17_wave_approach", 2.5f);
            while (sec.state != HarborWaves.State.Impact) yield return null;
            yield return Shot("18_wave_impact", 0.25f);
            yield return Shot("19_wave_plume", 0.7f);
            while (sec.state != HarborWaves.State.Washing) yield return null;
            yield return Shot("20_wave_washing_road", 1.2f);
            yield return Place(m, new Vector3(sec.road.center.x + 6f, 0f, 276f), 90f, 0.4f);
            yield return Shot("21_inside_wash", 0.2f);
            while (sec.state != HarborWaves.State.Calm) yield return null;
            yield return Place(m, new Vector3(1000f, 0f, 156f), 90f);
            yield return Shot("22_service_road");
            yield return Place(m, new Vector3(2190f, 0f, 552f), 90f, 0.2f);
            yield return Shot("23_extraction_approach", 1f);
            yield return Place(m, m.DebugPoint("extractionPoint").position, 90f, 0.5f);
            yield return Shot("24_extraction_results", 3f);
            Application.runInBackground = restoreBackground;   // in the editor this setting persists to Player Settings
            m.Garage.ResetProgress();
            Destroy(gameObject);
        }
    }
}
