using System;
using UnityEngine;

namespace NeonRift.Vehicles
{
    /// <summary>
    /// Gear state and automatic shift logic. Gear −1 = reverse, 0 = neutral, 1..n = forward.
    /// A shift takes <see cref="TransmissionSettings.shiftTime"/> with the clutch open; the new gear is reported immediately.
    /// </summary>
    public sealed class Gearbox
    {
        /// <summary>Minimum time between automatic shifts, s. Prevents hunting between two gears.</summary>
        private const float MinShiftInterval = 0.45f;
        /// <summary>A downshift is only taken if the lower gear lands below this fraction of the shift-up rpm.</summary>
        private const float DownshiftHeadroom = 0.92f;

        private TransmissionSettings settings;
        private float shiftTimer;
        private float sinceShift;

        public int Gear { get; private set; } = 1;
        public bool IsShifting => shiftTimer > 0f;
        /// <summary>Remaining shift time, s.</summary>
        public float ShiftTimeRemaining => shiftTimer;
        public int ForwardGearCount => settings.ForwardGearCount;

        /// <summary>(previous gear, new gear). Raised once per change; useful for audio and HUD.</summary>
        public event Action<int, int> GearChanged;

        public void Configure(in TransmissionSettings transmission)
        {
            settings = transmission;
            Reset();
        }

        public void Reset()
        {
            Gear = 1;
            shiftTimer = 0f;
            sinceShift = MinShiftInterval;
        }

        /// <summary>Signed gearbox ratio for a gear (reverse is negative, neutral 0). Excludes the final drive.</summary>
        public float RatioFor(int gear)
        {
            if (gear < 0) return -settings.reverseRatio;
            if (gear == 0 || gear > settings.ForwardGearCount) return 0f;
            return settings.gearRatios[gear - 1];
        }

        public float Ratio => RatioFor(Gear);

        /// <summary>Starts a timed shift to <paramref name="gear"/>. Ignored while already shifting.</summary>
        public bool Shift(int gear)
        {
            gear = Mathf.Clamp(gear, -1, settings.ForwardGearCount);
            if (gear == Gear || IsShifting) return false;
            int previous = Gear;
            Gear = gear;
            shiftTimer = settings.shiftTime;
            sinceShift = 0f;
            GearChanged?.Invoke(previous, gear);
            return true;
        }

        /// <summary>Selects reverse or first at standstill without a timed shift.</summary>
        public void SelectDirection(bool reverse)
        {
            int target = reverse ? -1 : 1;
            if (target == Gear) return;
            int previous = Gear;
            Gear = target;
            shiftTimer = 0f;
            sinceShift = 0f;
            GearChanged?.Invoke(previous, target);
        }

        public void Tick(float dt)
        {
            sinceShift += dt;
            if (shiftTimer > 0f) shiftTimer = Mathf.Max(0f, shiftTimer - dt);
        }

        /// <summary>Automatic shift decision from engine speed and throttle. Call after <see cref="Tick"/>.</summary>
        public void UpdateAutomatic(float engineRpm, float throttle)
        {
            if (IsShifting || Gear < 1 || settings.ForwardGearCount < 2 || sinceShift < MinShiftInterval) return;

            if (Gear < settings.ForwardGearCount && engineRpm > settings.shiftUpRpm && throttle > 0.05f)
            {
                Shift(Gear + 1);
                return;
            }
            if (Gear > 1)
            {
                // Hold lower gears longer under throttle (kick-down), drop early when cruising or braking.
                float downRpm = settings.shiftDownRpm + throttle * 0.25f * (settings.shiftUpRpm - settings.shiftDownRpm);
                float lowerGearRpm = engineRpm * RatioFor(Gear - 1) / RatioFor(Gear);
                if (engineRpm < downRpm && lowerGearRpm < settings.shiftUpRpm * DownshiftHeadroom)
                    Shift(Gear - 1);
            }
        }
    }
}
