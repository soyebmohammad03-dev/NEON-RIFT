using NeonRift.Audio;
using NeonRift.Game;
using NeonRift.Input;
using NeonRift.Vehicles;
using UnityEngine;
using UnityEngine.InputSystem;

namespace NeonRift.Gameplay
{
    /// <summary>
    /// Entry point for any scene that hosts a mission: spawns the selected vehicle,
    /// gives it the player's controls, points the camera at it and, if the scene has one,
    /// starts the <see cref="MissionDirector"/> with the session's mission.
    /// </summary>
    public sealed class MissionSceneEntry : MonoBehaviour, ISceneEntryPoint
    {
        [SerializeField] private VehicleSpawnPoint spawnPoint;
        [SerializeField] private VehicleChaseCamera chaseCamera;
        [Tooltip("Runs the mission's objectives. Optional: dev scenes without one are free driving.")]
        [SerializeField] private MissionDirector director;
        [Tooltip("Camera the HUD projects waypoints with.")]
        [SerializeField] private Camera viewCamera;
        [Tooltip("Rival crews driving the catalog cars the player did not pick. Optional.")]
        [SerializeField] private RivalDirector rivals;
        [Tooltip("Optional development overlay.")]
        [SerializeField] private VehicleDebugHud debugHud;
        [Tooltip("A vehicle that falls below this height has left the world and is returned to the spawn point, m.")]
        [SerializeField] private float outOfBoundsHeight = -30f;

        private GameContext context;
        private PlayerDrivingInput playerInput;

        public VehicleController PlayerVehicle { get; private set; }
        public VehicleChaseCamera ChaseCamera => chaseCamera;
        public MissionDirector Director => director;
        public RivalDirector Rivals => rivals;
        public VehicleSpawnPoint SpawnPoint => spawnPoint;

        public void Enter(GameContext gameContext)
        {
            context = gameContext;
            playerInput = new PlayerDrivingInput(context.Controls);

            var vehicle = context.Session.SelectedVehicle;
            if (vehicle == null)
                Debug.LogWarning("[Mission] No vehicle selected (the Vehicle Catalog is empty); nothing to spawn.", this);
            else
                PlayerVehicle = spawnPoint.Spawn(vehicle, detailedLights: true);

            if (PlayerVehicle != null)
            {
                foreach (var receiver in PlayerVehicle.GetComponentsInChildren<IVehicleInputReceiver>())
                    receiver.SetInputSource(playerInput);
                if (chaseCamera != null) chaseCamera.SetTarget(PlayerVehicle);
                if (PlayerVehicle.TryGetComponent(out VehicleAudio audio)) audio.SetPlayerView(true);
                if (debugHud != null) debugHud.SetTarget(PlayerVehicle);
            }

            context.Controls.Driving.Pause.performed += OnPause;
            context.Controls.Driving.ResetVehicle.performed += OnResetVehicle;
            context.Controls.Driving.Enable();
            string devices = PlayerDrivingInput.DescribeDevices(out bool canDrive);
            if (canDrive) Debug.Log($"[Input] Driving controls enabled. Devices: {devices}");
            else Debug.LogWarning($"[Input] Driving controls enabled but no keyboard or gamepad is connected (devices: {devices}). " +
                                  "In the editor this means the Input System backend lost its devices; restart the editor.");

            var mission = context.Session.SelectedMission;
            if (rivals != null && mission != null && PlayerVehicle != null) rivals.Spawn(context.Session.Rivals, mission.MaxRivals);
            if (director != null && mission != null && PlayerVehicle != null)
                director.Begin(context, mission, PlayerVehicle, viewCamera != null ? viewCamera : Camera.main);
        }

        public void Exit()
        {
            if (context == null) return;
            if (Paused) { pause.Close(); Time.timeScale = 1f; AudioListener.pause = false; }
            if (director != null) director.End();
            context.Controls.Driving.Pause.performed -= OnPause;
            context.Controls.Driving.ResetVehicle.performed -= OnResetVehicle;
            context.Controls.Driving.Disable();
            context = null;
        }

        private void FixedUpdate()
        {
            if (PlayerVehicle == null || PlayerVehicle.Body == null || PlayerVehicle.Body.position.y > outOfBoundsHeight) return;
            Debug.Log("[Mission] Vehicle left the world; returning it to the spawn point.", this);
            PlayerVehicle.Teleport(spawnPoint.Position, spawnPoint.Rotation);
            if (chaseCamera != null) chaseCamera.Snap();
        }

        private void OnResetVehicle(InputAction.CallbackContext _)
        {
            if (PlayerVehicle == null) return;
            PlayerVehicle.Recover();
            if (chaseCamera != null) chaseCamera.Snap();
        }

        // ---------------- Pause menu ----------------

        private OptionsMenu pause;
        private float pausedTimeScale = 1f;
        private NeonRift.Audio.MixerState pausedMix;

        public bool Paused => pause != null && pause.IsOpen;

        private void OnPause(InputAction.CallbackContext _)
        {
            if (director != null && director.TryHandlePause()) return;
            if (context == null || context.Flow.IsTransitioning || Paused) return;
            OpenPause();
        }

        /// <summary>Freezes the mission and opens the pause menu (also used by validation tools).</summary>
        public void OpenPause()
        {
            if (Paused || director == null || director.Hud == null) return;
            if (pause == null)
            {
                pause = new OptionsMenu(director.Hud.Root, context);
                pause.BackFromRoot += ResumeFromPause;
            }
            pausedTimeScale = Time.timeScale;
            Time.timeScale = 0f;
            AudioListener.pause = true;   // the music track ignores the listener pause and keeps playing, ducked
            pausedMix = context.Audio.State;
            context.Audio.TransitionTo(NeonRift.Audio.MixerState.Ducked, 0.3f);
            context.Controls.Driving.Disable();
            director.Paused = true;
            pause.Open("PAUSED", new System.Collections.Generic.List<OptionsMenu.Row>
            {
                pause.Button("RESUME", ResumeFromPause),
                pause.Button("RESTART MISSION", () => LeavePause(() => director.RestartMission())),
                pause.Button("CONTROLS", pause.PushControls),
                pause.Button("SETTINGS", pause.PushSettings),
                pause.Button("REPLAY INTRO", () => LeavePause(() => context.Flow.PlayIntro())),
                pause.Button("QUIT TO GARAGE", () => LeavePause(() => context.Flow.GoToCarSelect())),
                pause.Button("QUIT TO TITLE", () => LeavePause(() => context.Flow.GoToFrontend())),
            });
            Debug.Log("[Mission] paused");
        }

        public void ResumeFromPause()
        {
            if (!Paused) return;
            pause.Close();
            Time.timeScale = pausedTimeScale;
            AudioListener.pause = false;
            context.Audio.TransitionTo(pausedMix, 0.3f);
            context.Controls.Driving.Enable();
            director.Paused = false;
            Debug.Log("[Mission] resumed");
        }

        private void LeavePause(System.Action leave)
        {
            pause.Close();
            Time.timeScale = 1f;
            AudioListener.pause = false;
            director.Paused = false;
            leave();
        }
    }
}
