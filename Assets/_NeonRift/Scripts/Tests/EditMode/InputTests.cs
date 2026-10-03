using NeonRift.Input;
using NUnit.Framework;
using UnityEngine.InputSystem;

namespace NeonRift.Tests
{
    /// <summary>
    /// Drives virtual devices through the real Driving action map and checks the values that reach
    /// <see cref="PlayerDrivingInput"/>, the source the player's VehicleController reads every step.
    /// </summary>
    public sealed class InputTests : InputTestFixture
    {
        private NeonRiftControls controls;
        private PlayerDrivingInput input;

        public override void Setup()
        {
            base.Setup();
            controls = new NeonRiftControls();
            input = new PlayerDrivingInput(controls);
            controls.Driving.Enable();
        }

        public override void TearDown()
        {
            // The generated Dispose() calls Destroy, which is not allowed in Edit Mode.
            controls.Disable();
            UnityEngine.Object.DestroyImmediate(controls.asset);
            base.TearDown();
        }

        [TestCase(Key.W)]
        [TestCase(Key.UpArrow)]
        public void KeyboardThrottle(Key key)
        {
            var keyboard = InputSystem.AddDevice<Keyboard>();
            Press(keyboard[key]);
            Assert.AreEqual(1f, input.ReadInput().Throttle);
            Release(keyboard[key]);
            Assert.AreEqual(0f, input.ReadInput().Throttle);
        }

        [TestCase(Key.S)]
        [TestCase(Key.DownArrow)]
        public void KeyboardBrake(Key key)
        {
            var keyboard = InputSystem.AddDevice<Keyboard>();
            Press(keyboard[key]);
            Assert.AreEqual(1f, input.ReadInput().Brake);
        }

        [TestCase(Key.A, -1f)]
        [TestCase(Key.LeftArrow, -1f)]
        [TestCase(Key.D, 1f)]
        [TestCase(Key.RightArrow, 1f)]
        public void KeyboardSteer(Key key, float expected)
        {
            var keyboard = InputSystem.AddDevice<Keyboard>();
            Press(keyboard[key]);
            Assert.AreEqual(expected, input.ReadInput().Steer);
        }

        [Test]
        public void KeyboardThrottleAndSteerTogether()
        {
            var keyboard = InputSystem.AddDevice<Keyboard>();
            Press(keyboard.wKey);
            Press(keyboard.dKey);
            var value = input.ReadInput();
            Assert.AreEqual(1f, value.Throttle);
            Assert.AreEqual(1f, value.Steer);
        }

        [Test]
        public void KeyboardHandbrakeAndInteract()
        {
            var keyboard = InputSystem.AddDevice<Keyboard>();
            Press(keyboard.spaceKey);
            Assert.IsTrue(input.ReadInput().Handbrake);
            Press(keyboard.eKey);
            Assert.IsTrue(controls.Driving.Interact.IsPressed());
        }

        [Test]
        public void GamepadDriving()
        {
            var gamepad = InputSystem.AddDevice<Gamepad>();
            Set(gamepad.rightTrigger, 0.6f);
            Set(gamepad.leftTrigger, 0.3f);
            Set(gamepad.leftStick, new UnityEngine.Vector2(-0.8f, 0f));
            var value = input.ReadInput();
            Assert.AreEqual(0.6f, value.Throttle, 0.01f);
            Assert.AreEqual(0.3f, value.Brake, 0.01f);
            Assert.Less(value.Steer, -0.5f);
        }

        [Test]
        public void GamepadButtons()
        {
            var gamepad = InputSystem.AddDevice<Gamepad>();
            Press(gamepad.buttonEast);
            Assert.IsTrue(input.ReadInput().Handbrake, "B / East is the handbrake");
            Press(gamepad.buttonSouth);
            Assert.IsTrue(controls.Driving.Interact.IsPressed(), "A / South is interact");
        }

        [Test]
        public void DisablingTheMenuMapLeavesDrivingEnabled()
        {
            var keyboard = InputSystem.AddDevice<Keyboard>();
            controls.Menu.Enable();
            controls.Menu.Disable();
            Press(keyboard.wKey);
            Assert.AreEqual(1f, input.ReadInput().Throttle);
        }
    }
}
