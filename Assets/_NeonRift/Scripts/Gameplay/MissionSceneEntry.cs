using NeonRift.Game;
using NeonRift.Input;
using NeonRift.Vehicles;
using Unity.Cinemachine;
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
        [SerializeField] private CinemachineCamera followCamera;

        private GameContext context;
        private PlayerDrivingInput playerInput;

        public GameObject PlayerVehicle { get; private set; }

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

                if (followCamera != null)
                {
                    followCamera.Follow = PlayerVehicle.transform;
                    followCamera.LookAt = PlayerVehicle.transform;
                }
            }

            context.Controls.Driving.Pause.performed += OnPause;
            context.Controls.Driving.Enable();
        }

        public void Exit()
        {
            if (context == null) return;
            context.Controls.Driving.Pause.performed -= OnPause;
            context.Controls.Driving.Disable();
            context = null;
        }

        // Returns to the title until the pause menu exists (UI phase).
        private void OnPause(InputAction.CallbackContext _)
        {
            if (context != null && !context.Flow.IsTransitioning)
                context.Flow.GoToFrontend();
        }
    }
}
