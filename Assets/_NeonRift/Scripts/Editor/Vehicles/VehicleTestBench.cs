using System;
using NeonRift.Vehicles;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace NeonRift.EditorTools.Vehicles
{
    /// <summary>
    /// Isolated, deterministic physics scene for one vehicle: a preview scene with its own physics world, stepped
    /// manually at the game's 100 Hz. Used by automated tests and by the performance measurement tool.
    /// </summary>
    public sealed class VehicleTestBench : IDisposable
    {
        public const float Dt = 0.01f;

        private Scene scene;
        private PhysicsScene physics;

        public VehicleController Vehicle { get; private set; }
        public ScriptedDrivingInput Input { get; } = new ScriptedDrivingInput();
        public VehicleDefinition Definition { get; private set; }
        public float Time { get; private set; }
        public Rigidbody Body => Vehicle.Body;
        public Vector3 LocalVelocity => Quaternion.Inverse(Body.rotation) * Body.linearVelocity;
        public float ForwardSpeed => LocalVelocity.z;
        public float SpeedKph => Body.linearVelocity.magnitude * VehicleUnits.MsToKph;
        /// <summary>Body sideslip angle, degrees (0 = travelling where the nose points).</summary>
        public float Sideslip => Body.linearVelocity.sqrMagnitude > 1f ? Mathf.Atan2(LocalVelocity.x, Mathf.Abs(LocalVelocity.z)) * Mathf.Rad2Deg : 0f;
        public bool HasNaN => float.IsNaN(Body.position.x) || float.IsNaN(Body.linearVelocity.x) || float.IsNaN(Body.angularVelocity.x);

        /// <param name="groundGrip">Optional surface grip for the ground (adds a DrivingSurface).</param>
        public static VehicleTestBench Create(VehicleDefinition definition, float groundGrip = 1f)
        {
            var bench = new VehicleTestBench();
            bench.Build(definition, groundGrip);
            return bench;
        }

        private void Build(VehicleDefinition definition, float groundGrip)
        {
            Definition = definition;
            scene = EditorSceneManager.NewPreviewScene();
            physics = scene.GetPhysicsScene();

            var ground = AddBox("Ground", new Vector3(0f, -0.5f, 0f), new Vector3(6000f, 1f, 6000f), Quaternion.identity, "Drivable");
            if (!Mathf.Approximately(groundGrip, 1f)) ground.AddComponent<DrivingSurface>().EditorConfigure(groundGrip, 1f);

            var instance = Object.Instantiate(definition.GameplayPrefab);
            instance.name = "BenchVehicle_" + definition.Id;
            SceneManager.MoveGameObjectToScene(instance, scene);
            Vehicle = instance.GetComponent<VehicleController>();
            Vehicle.ExternalStepping = true;
            Vehicle.Configure(definition.PhysicsProfile);
            Vehicle.SetInputSource(Input);
            Vehicle.Teleport(new Vector3(0f, 0.05f, 0f), Quaternion.identity);
        }

        public GameObject AddBox(string name, Vector3 position, Vector3 size, Quaternion rotation, string layer)
        {
            var go = new GameObject(name);
            SceneManager.MoveGameObjectToScene(go, scene);
            go.layer = LayerMask.NameToLayer(layer);
            go.transform.SetPositionAndRotation(position, rotation);
            go.AddComponent<BoxCollider>().size = size;
            return go;
        }

        /// <summary>
        /// Copies a root object (by name) from a scene asset into the bench, e.g. the test track's validation route.
        /// Returns the copy, or null if the scene or object is missing.
        /// </summary>
        public GameObject AddFromScene(string scenePath, string rootName)
        {
            var source = EditorSceneManager.GetSceneByPath(scenePath);
            bool opened = false;
            if (!source.isLoaded)
            {
                source = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
                opened = true;
            }
            try
            {
                foreach (var root in source.GetRootGameObjects())
                {
                    if (root.name != rootName) continue;
                    var copy = Object.Instantiate(root);
                    copy.name = rootName;
                    SceneManager.MoveGameObjectToScene(copy, scene);
                    return copy;
                }
                return null;
            }
            finally
            {
                if (opened) EditorSceneManager.CloseScene(source, true);
            }
        }

        /// <summary>A transverse speed bump: a cylinder of <paramref name="radius"/> whose top stands <paramref name="height"/> above the road.</summary>
        public GameObject AddBump(float z, float height, float radius, float width = 14f, float xOffset = 0f)
        {
            var go = new GameObject("Bump");
            SceneManager.MoveGameObjectToScene(go, scene);
            go.layer = LayerMask.NameToLayer("Drivable");
            go.transform.SetPositionAndRotation(new Vector3(xOffset, height - radius, z), Quaternion.Euler(0f, 0f, 90f));
            var capsule = go.AddComponent<CapsuleCollider>();
            capsule.radius = radius;
            capsule.height = width;
            capsule.direction = 1;
            return go;
        }

        public void Step()
        {
            Vehicle.Step(Dt);
            physics.Simulate(Dt);
            Time += Dt;
        }

        /// <summary>Steps for <paramref name="seconds"/> or until <paramref name="until"/> returns true. Returns elapsed time.</summary>
        public float Run(float seconds, Func<bool> until = null, Action perStep = null)
        {
            float start = Time;
            int steps = Mathf.RoundToInt(seconds / Dt);
            for (int i = 0; i < steps; i++)
            {
                perStep?.Invoke();
                Step();
                if (HasNaN) throw new InvalidOperationException($"{Definition.Id}: NaN in vehicle state at t={Time:0.00}s");
                if (until != null && until()) break;
            }
            return Time - start;
        }

        /// <summary>
        /// Moves the car back toward the origin along the ground plane, keeping its velocity. Only valid on the plain
        /// bench floor (long top-speed runs).
        /// </summary>
        public void Recentre()
        {
            Vector3 p = Body.position;
            Body.position = new Vector3(0f, p.y, 0f);
            Physics.SyncTransforms();
        }

        public void SetInput(float throttle = 0f, float brake = 0f, float steer = 0f, bool handbrake = false)
            => Input.Current = new DrivingInput { Throttle = throttle, Brake = brake, Steer = steer, Handbrake = handbrake };

        /// <summary>Simple throttle/brake controller toward a forward speed, keeping the current steer.</summary>
        public void HoldSpeed(float targetMs)
        {
            float error = targetMs - ForwardSpeed;
            var input = Input.Current;
            input.Throttle = Mathf.Clamp01(error * 0.6f + 0.15f);
            input.Brake = error < -1f ? Mathf.Clamp01(-error * 0.2f) : 0f;
            Input.Current = input;
        }

        /// <summary>Accelerates in a straight line to <paramref name="kph"/>. Returns false if it was not reached in time.</summary>
        public bool AccelerateTo(float kph, float timeout = 40f)
        {
            SetInput(throttle: 1f);
            Run(timeout, () => SpeedKph >= kph);
            return SpeedKph >= kph;
        }

        public void Dispose()
        {
            if (scene.IsValid()) EditorSceneManager.ClosePreviewScene(scene);
        }
    }
}
