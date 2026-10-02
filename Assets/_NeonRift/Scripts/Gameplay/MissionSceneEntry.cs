using NeonRift.Game;
using NeonRift.Input;
using NeonRift.Vehicles;
using UnityEngine;
using UnityEngine.InputSystem;

namespace NeonRift.Gameplay
{
    /// <summary>
    /// Entry point for any scene that hosts a mission: spawns the selected vehicle,
    /// gives it the player's controls and points the camera at it.
    /// </summary>
    public sealed class MissionSceneEntry : MonoBehaviour, ISceneEntryPoint
    {
        [SerializeField] private VehicleSpawnPoint spawnPoint;
        [SerializeField] private VehicleChaseCamera chaseCamera;
        [Tooltip("Optional development overlay.")]
        [SerializeField] private VehicleDebugHud debugHud;
        [Tooltip("A vehicle that falls below this height has left the world and is returned to the spawn point, m.")]
        [SerializeField] private float outOfBoundsHeight = -30f;

        private GameContext context;
        private PlayerDrivingInput playerInput;

        public VehicleController PlayerVehicle { get; private set; }
        public VehicleChaseCamera ChaseCamera => chaseCamera;

        public void Enter(GameContext gameContext)
        {
            context = gameContext;
            playerInput = new PlayerDrivingInput(context.Controls);

            var vehicle = context.Session.SelectedVehicle;
            if (vehicle == null)
                Debug.LogWarning("[Mission] No vehicle selected (the Vehicle Catalog is empty); nothing to spawn.", this);
            else
                PlayerVehicle = spawnPoint.Spawn(vehicle);

            if (PlayerVehicle != null)
            {
                foreach (var receiver in PlayerVehicle.GetComponentsInChildren<IVehicleInputReceiver>())
                    receiver.SetInputSource(playerInput);
                if (chaseCamera != null) chaseCamera.SetTarget(PlayerVehicle);
                if (debugHud != null) debugHud.SetTarget(PlayerVehicle);
            }

            context.Controls.Driving.Pause.performed += OnPause;
            context.Controls.Driving.ResetVehicle.performed += OnResetVehicle;
            context.Controls.Driving.Enable();
        }

        public void Exit()
        {
            if (context == null) return;
            context.Controls.Driving.Pause.performed -= OnPause;
            context.Controls.Driving.ResetVehicle.performed -= OnResetVehicle;
            context.Controls.Driving.Disable();
            context = null;
        }

        private void FixedUpdate()
        {
            if (PlayerVehicle == null || PlayerVehicle.Body.position.y > outOfBoundsHeight) return;
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

        // Returns to the title until the pause menu exists (UI phase).
        private void OnPause(InputAction.CallbackContext _)
        {
            if (context != null && !context.Flow.IsTransitioning)
                context.Flow.GoToFrontend();
        }
    }
}
