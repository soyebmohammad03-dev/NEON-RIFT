using System.Collections;
using System.Collections.Generic;
using NeonRift.Game;
using NeonRift.Vehicles;
using UnityEngine;
using UnityEngine.UIElements;

namespace NeonRift.Gameplay
{
    /// <summary>
    /// Campaign only, after a qualified Night Run: the bridge to Harbor Rain. Under a short black card (the operation's
    /// result), the car is staged on a clear straight, a rift opens across the road ahead, the car drives into it under
    /// its own physics (three camera set-ups: behind the car, alongside, at the portal), and a white surge carries the
    /// cut to the Harbor Rain arrival. The car is only ever repositioned while the screen is black.
    /// </summary>
    public sealed class NightRunPortalSequence : MonoBehaviour
    {
        public static NightRunPortalSequence Active { get; private set; }

        private GameContext context;
        private CampaignSession campaign;
        private VehicleController car;
        private VisualElement hudRoot;
        private PortalRift portal;
        private CinematicCamera cam;
        private readonly ScriptedDrivingInput drive = new();
        private bool skip;

        /// <summary>Current shot name (validation / review).</summary>
        public string Shot { get; private set; } = "";
        public bool CarEntered { get; private set; }
        /// <summary>The car physically crossed the portal plane (false when a skip or the safety timeout ended the shot).</summary>
        public bool CarCrossed { get; private set; }
        private float nextClear;
        public PortalRift Portal => portal;

        public static NightRunPortalSequence Begin(GameContext context, VehicleController car, VisualElement hudRoot, string resultLine)
        {
            if (Active != null) return Active;
            var go = new GameObject("NightRunPortalSequence");
            var s = go.AddComponent<NightRunPortalSequence>();
            Active = s;
            s.context = context;
            s.campaign = context.Campaign;
            s.car = car;
            s.hudRoot = hudRoot;
            s.campaign.Step = CampaignStep.Portal;
            s.StartCoroutine(s.Run(resultLine));
            return s;
        }

        private void OnDestroy()
        {
            if (Active == this) Active = null;
            if (context != null) context.Controls.Menu.Skip.performed -= OnSkip;
            if (cam != null) Destroy(cam.gameObject);
            if (portal != null) Destroy(portal.gameObject);
        }

        private void OnSkip(UnityEngine.InputSystem.InputAction.CallbackContext _) => skip = true;

        private IEnumerator Wait(float seconds)
        {
            for (float t = 0f; t < seconds && !skip; t += Time.unscaledDeltaTime) yield return null;
        }

        private IEnumerator Run(string resultLine)
        {
            var overlay = campaign.Overlay;
            var def = campaign.Definition;
            Time.timeScale = 1f;
            context.Controls.Menu.Skip.performed += OnSkip;
            context.Controls.Menu.Enable();

            // 1. Black, with the result and the incoming signal.
            Shot = "card";
            for (float t = 0f; t < 0.6f; t += Time.unscaledDeltaTime) { overlay.SetFade(t / 0.6f); yield return null; }
            overlay.SetFade(1f);
            if (hudRoot != null) hudRoot.style.display = DisplayStyle.None;
            overlay.ShowCard("OPERATION NIGHT RUN", "COMPLETE", new List<string> { resultLine, "▸ AN UNKNOWN CHANNEL IS TRYING TO REACH YOU…" }, null, 0.7f);

            // Stage the car (screen is black): a clear straight, the rift ahead.
            Vector3 stage = def.NightRunPortalStage;
            Quaternion heading = Quaternion.Euler(0f, def.NightRunPortalYaw, 0f);
            if (stage == Vector3.zero) { stage = car.transform.position; heading = Quaternion.Euler(0f, car.transform.eulerAngles.y, 0f); }
            car.Teleport(stage + Vector3.up * 0.15f, heading);
            drive.Current = new DrivingInput { Brake = 1f };
            foreach (var r in car.GetComponentsInChildren<IVehicleInputReceiver>()) r.SetInputSource(drive);
            Vector3 fwd = heading * Vector3.forward, right = heading * Vector3.right;
            Vector3 portalPos = stage + fwd * def.NightRunPortalDistance;
            portal = PortalRift.Create(def, portalPos, Quaternion.LookRotation(-fwd, Vector3.up), new AudioMixerConfigRef(context.Config.AudioMixer != null ? context.Config.AudioMixer.Sfx : null));
            ClearTraffic(stage, portalPos);
            cam = CinematicCamera.Create("PortalCamera", 34f);
            Vector3 shotA = stage - fwd * 7.5f - right * 2.2f + Vector3.up * 1.1f;
            cam.Pose(shotA, portal.Centre - Vector3.up * 1.2f);
            yield return Wait(2.6f);
            overlay.HideCard();

            // 2. Behind the car: the rift tears open across the road.
            Shot = "ignite";
            for (float t = 0f; t < 0.7f; t += Time.unscaledDeltaTime) { overlay.SetFade(1f - t / 0.7f); yield return null; }
            overlay.SetFade(0f);
            overlay.SetLetterbox(true);
            overlay.SetHint("SKIP  ·  SPACE / START");
            portal.PlayOpen();
            float speak = campaign.Speak("portal");
            for (float t = 0f; t < 3.2f && !skip; t += Time.unscaledDeltaTime)
            {
                portal.SetOpen(Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.2f, 2.4f, t)));
                cam.Pose(shotA + fwd * t * 0.35f, portal.Centre - Vector3.up * 1.6f, Mathf.Lerp(34f, 30f, t / 3.2f));
                if (t > 0.5f && t < 1.4f) cam.Shake(0.4f);
                yield return null;
            }
            portal.SetOpen(1f);

            // 3. Alongside: the car launches at the portal under its own power.
            Shot = "approach";
            float start = Time.unscaledTime;
            while (!skip && portal.Front(car.Body.position) > 16f && Time.unscaledTime - start < 10f)
            {
                Steer();
                KeepClear(stage, portalPos);
                Vector3 p = car.Body.position;
                Vector3 f = car.transform.forward;
                cam.Pose(p + right * 4.6f - f * 3.2f + Vector3.up * 1.0f, p + f * 7f + Vector3.up * 0.6f, 52f);
                yield return null;
            }

            // 4. At the rift: the car crosses the plane and is gone in a white surge.
            Shot = "enter";
            // Low behind the car, looking through the rift: the car runs into the opening and is gone.
            start = Time.unscaledTime;
            while (!skip && !CarEntered && Time.unscaledTime - start < 6f)
            {
                Steer();
                KeepClear(stage, portalPos);
                Vector3 cp = car.Body.position;
                cam.Pose(cp - fwd * 9f + right * 1.2f + Vector3.up * 2.0f, portal.Centre - Vector3.up * 1.4f, 46f);
                if (portal.Front(car.Body.position) < 0.4f) CarEntered = CarCrossed = true;
                yield return null;
            }
            CarEntered = true;
            portal.Surge();
            portal.PlayEnter();
            cam.Shake(1f);
            car.gameObject.SetActive(false);
            overlay.SetHint(null);
            for (float t = 0f; t < 0.45f; t += Time.unscaledDeltaTime) { overlay.SetFlash(t / 0.45f); yield return null; }
            overlay.SetFlash(1f);
            // Let the line finish (it is short) unless skipped.
            for (float t = 0f; !skip && t < 0.9f; t += Time.unscaledDeltaTime) yield return null;
            Shot = "leaving";

            context.Controls.Menu.Skip.performed -= OnSkip;
            campaign.Step = CampaignStep.Arrival;
            context.Session.SelectMission(def.Level2);
            Debug.Log($"[Campaign] portal: {(skip ? "skipped" : CarCrossed ? "car drove through" : "safety timeout")}, leaving for Harbor Rain (spoke {speak:0.0}s)");
            context.Flow.StartMission();
        }

        private void Steer()
        {
            Vector3 to = portal.transform.position - car.Body.position;
            to.y = 0f;
            float angle = Vector3.SignedAngle(Vector3.ProjectOnPlane(car.transform.forward, Vector3.up), to, Vector3.up);
            float kph = car.Body.linearVelocity.magnitude * 3.6f;
            drive.Current = new DrivingInput { Throttle = kph < 78f ? 0.85f : 0.35f, Steer = Mathf.Clamp(angle / 25f, -1f, 1f) };
        }

        /// <summary>Ambient traffic keeps cycling: clear the run-up again twice a second while the car is driving it.</summary>
        private void KeepClear(Vector3 a, Vector3 b)
        {
            if (Time.unscaledTime < nextClear) return;
            nextClear = Time.unscaledTime + 0.5f;
            ClearTraffic(a, b);
        }

        /// <summary>Removes ambient traffic from the run-up so nothing crosses the shot (traffic is gone with the scene anyway).</summary>
        private static void ClearTraffic(Vector3 a, Vector3 b)
        {
            foreach (var t in FindObjectsByType<TrafficCar>(FindObjectsSortMode.None))
            {
                Vector3 p = t.transform.position;
                Vector3 ab = b - a;
                float k = Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude);
                if (Vector3.Distance(p, a + ab * k) < 40f) t.gameObject.SetActive(false);
            }
        }
    }
}
