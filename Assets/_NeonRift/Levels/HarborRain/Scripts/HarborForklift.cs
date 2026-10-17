using System.Collections.Generic;
using UnityEngine;

namespace NeonRift.HarborRain
{
    /// <summary>
    /// A counterbalance forklift restacking two palletised loads between two stacks (Harbor Rain 2.2). Every trip: drive
    /// in on low forks until the pallet sits centred on them, lift it clear, tilt the mast back, reverse out on a rear-
    /// steered arc, carry it to the other stack, raise to the stacking height, tilt level, set it down exactly on the slot,
    /// lower the forks out of the pockets and back away — then fetch the next one. When the source stack is empty the
    /// direction swaps. Driving follows smooth pose-to-pose curves (rear wheels steer from the path curvature, wheels turn
    /// with distance); the two-stage mast extends above the free lift; the beacon flashes while moving and the reversing
    /// alarm sounds only when backing. Kinematic: it waits if the player's car is in its way and never pushes it.
    /// </summary>
    public sealed class HarborForklift : MonoBehaviour
    {
        [SerializeField] private Transform carriage;
        [SerializeField] private Transform mast;
        [SerializeField] private Transform innerMast;
        [SerializeField] private Transform[] wheels = new Transform[0];
        [SerializeField] private Transform[] steerWheels = new Transform[0];
        [SerializeField] private Renderer beacon;
        [SerializeField] private Vector3 pointA, pointB;
        [SerializeField] private Transform[] loads = new Transform[0];
        [SerializeField] private float speed = 2.6f;
        [SerializeField] private AudioSource reverseBeep;

        /// <summary>Fork centre ahead of the forklift origin, unit height (pallet + goods), fork height when travelling empty.</summary>
        public const float ForkReach = 1.9f, UnitHeight = 1.06f, Travel = 0.18f, FreeLift = 1.15f, WheelBase = 1.65f;

        private enum Step { Approach, Engage, Lift, Withdraw, Carry, Place, Release, Back }
        private Step step;
        private float t, lift = Travel, tilt, spin, steer, v, pickH, placeH;
        private bool aToB = true;
        private Transform held;
        private readonly int[] counts = new int[2];
        private readonly List<Transform>[] stacks = { new(), new() };
        private MaterialPropertyBlock block;
        private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");

        // Current leg: a cubic Hermite from p0 (heading h0) to p1 (heading h1), driven forwards or in reverse.
        private Vector3 p0, p1, m0, m1;
        private bool reverse;
        private float legLen, legS;
        private readonly float[] arc = new float[33];

        public Vector3 PointA => pointA;
        public Vector3 PointB => pointB;
        public int Trips { get; private set; }
        public float Lift => lift;
        public bool Carrying => held != null;
        public float Steer => steer;
        public string State => step.ToString();

        private Vector3 Dir => Flat(pointB - pointA);
        private Vector3 Slot(int s) => s == 0 ? pointA : pointB;
        private int Source => aToB ? 0 : 1;
        private int Target => aToB ? 1 : 0;
        /// <summary>Facing direction when working stack <paramref name="s"/> (the forks point at it).</summary>
        private Vector3 Facing(int s) => s == 0 ? -Dir : Dir;

        private void OnEnable()
        {
            stacks[0].Clear(); stacks[1].Clear();
            for (int i = 0; i < loads.Length; i++)
            {
                if (loads[i] == null) continue;
                loads[i].SetParent(transform.parent, true);
                stacks[0].Add(loads[i]);
                loads[i].SetPositionAndRotation(pointA + Vector3.up * (UnitHeight * i), Quaternion.LookRotation(Dir));
            }
            counts[0] = stacks[0].Count; counts[1] = 0;
            aToB = true;
            held = null; lift = Travel; tilt = 0f;
            // Start a little way back from stack A, facing it, ready to approach.
            var face = Facing(0);
            transform.SetPositionAndRotation(pointA - face * (ForkReach + 6f), Quaternion.LookRotation(face));
            BeginApproach();
        }

        private static Vector3 Flat(Vector3 d) { d.y = 0f; return d.sqrMagnitude > 1e-6f ? d.normalized : Vector3.forward; }

        private float PickHeight(int s) => (stacks[s].Count - 1) * UnitHeight + 0.02f;
        private float PlaceHeight(int s) => stacks[s].Count * UnitHeight + 0.02f;

        private void Leg(Vector3 to, Vector3 headingTo, bool back)
        {
            p0 = transform.position; p1 = to; p1.y = p0.y;
            var h0 = transform.forward; var h1 = headingTo;
            float d = Vector3.Distance(p0, p1);
            float k = Mathf.Clamp(d * 1.1f, 0.5f, 12f);
            reverse = back;
            m0 = (back ? -h0 : h0) * k; m1 = (back ? -h1 : h1) * k;
            float acc = 0f; var prev = p0; arc[0] = 0f;
            for (int i = 1; i < arc.Length; i++) { var q = Hermite(i / (float)(arc.Length - 1)); acc += Vector3.Distance(prev, q); arc[i] = acc; prev = q; }
            legLen = Mathf.Max(acc, 0.01f); legS = 0f;
        }

        private Vector3 Hermite(float u)
        {
            float u2 = u * u, u3 = u2 * u;
            return (2 * u3 - 3 * u2 + 1) * p0 + (u3 - 2 * u2 + u) * m0 + (-2 * u3 + 3 * u2) * p1 + (u3 - u2) * m1;
        }

        private float U(float s)
        {
            for (int i = 1; i < arc.Length; i++)
                if (arc[i] >= s) { float f = Mathf.InverseLerp(arc[i - 1], arc[i], s); return (i - 1 + f) / (arc.Length - 1); }
            return 1f;
        }

        /// <summary>Advances along the leg; returns true on arrival.</summary>
        private bool Drive(float dt, float vmax)
        {
            float remaining = legLen - legS;
            float want = Mathf.Min(vmax, Mathf.Sqrt(2f * 0.7f * Mathf.Max(remaining, 0f)) + 0.05f);
            if (!reverse && Blocked()) want = 0f;
            v = Mathf.MoveTowards(v, want, dt * (want < v ? 1.6f : 0.9f));
            float ds = Mathf.Min(v * dt, remaining);
            legS += ds;
            float u = U(legS), ua = Mathf.Min(1f, U(legS + 0.25f));
            var pos = Hermite(u);
            var tan = Hermite(Mathf.Max(ua, u + 0.001f)) - pos;
            if (ua <= u + 0.0005f) tan = (reverse ? -m1 : m1);
            var heading = Flat(reverse ? -tan : tan);
            // Rear-wheel steering from the change of heading per metre (bicycle model, wheelbase 1.65 m).
            float yawRate = Vector3.SignedAngle(transform.forward, heading, Vector3.up) * Mathf.Deg2Rad / Mathf.Max(ds, 1e-4f);
            float want_steer = ds > 1e-5f ? Mathf.Clamp(-Mathf.Atan(yawRate * WheelBase) * Mathf.Rad2Deg * (reverse ? -1f : 1f), -70f, 70f) : steer;
            steer = Mathf.Lerp(steer, want_steer, 1f - Mathf.Exp(-6f * dt));
            transform.SetPositionAndRotation(pos, Quaternion.LookRotation(heading));
            spin += (reverse ? -ds : ds) / 0.33f * Mathf.Rad2Deg;
            return legS >= legLen - 0.01f;
        }

        private bool Blocked()
        {
            var car = HarborRainEntry.Active != null && HarborRainEntry.Active.Driving != null ? HarborRainEntry.Active.Driving.PlayerVehicle : null;
            return car != null && Vector3.Distance(car.transform.position, transform.position + transform.forward * 3f) < 4.5f;
        }

        private void BeginApproach()
        {
            int s = held == null ? Source : Target;
            var face = Facing(s);
            Leg(Slot(s) - face * (ForkReach + 2.4f), face, false);
            step = held == null ? Step.Approach : Step.Carry;
            t = 0f;
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            t += dt;
            switch (step)
            {
                case Step.Approach:
                    // Empty: forks at the pick height while closing on the source stack.
                    lift = Mathf.MoveTowards(lift, legS > legLen * 0.4f ? PickHeight(Source) : Travel, dt * 0.55f);
                    tilt = Mathf.MoveTowards(tilt, 0f, dt * 3f);
                    if (Drive(dt, speed) && Mathf.Abs(lift - PickHeight(Source)) < 0.01f)
                    { var face = Facing(Source); pickH = PickHeight(Source); Leg(Slot(Source) - face * ForkReach, face, false); step = Step.Engage; t = 0f; }
                    break;
                case Step.Engage:
                    if (Drive(dt, 0.7f)) { step = Step.Lift; t = 0f; }
                    break;
                case Step.Lift:
                    if (held == null && stacks[Source].Count > 0)
                    {
                        held = stacks[Source][stacks[Source].Count - 1];
                        stacks[Source].RemoveAt(stacks[Source].Count - 1);
                        held.SetParent(carriage, true);
                    }
                    lift = Mathf.MoveTowards(lift, pickH + 0.14f, dt * 0.35f);
                    tilt = Mathf.MoveTowards(tilt, -5f, dt * 2.5f);
                    if (t > 2.2f)
                    {
                        // Reverse out on an arc: three metres back while swinging the counterweight round 90°.
                        var face = Facing(Source);
                        var side = Vector3.Cross(Vector3.up, face);
                        var to = transform.position - face * 3.4f + side * 1.6f;
                        Leg(to, -side, true);
                        step = Step.Withdraw; t = 0f;
                    }
                    break;
                case Step.Withdraw:
                    lift = Mathf.MoveTowards(lift, Mathf.Max(Travel + 0.2f, Mathf.Min(lift, 0.45f)), dt * 0.4f);
                    if (Drive(dt, 1.3f)) BeginApproach();
                    break;
                case Step.Carry:
                    tilt = Mathf.MoveTowards(tilt, -5f, dt * 2.5f);
                    lift = Mathf.MoveTowards(lift, legS > legLen * 0.55f ? PlaceHeight(Target) + 0.12f : Travel + 0.2f, dt * 0.45f);
                    if (Drive(dt, speed * 0.85f) && Mathf.Abs(lift - (PlaceHeight(Target) + 0.12f)) < 0.01f)
                    { var face = Facing(Target); placeH = PlaceHeight(Target); Leg(Slot(Target) - face * ForkReach, face, false); step = Step.Place; t = 0f; }
                    break;
                case Step.Place:
                    tilt = Mathf.MoveTowards(tilt, 0f, dt * 2.5f);
                    if (Drive(dt, 0.6f) && Mathf.Abs(tilt) < 0.05f) { step = Step.Release; t = 0f; }
                    break;
                case Step.Release:
                    // Down out of the pockets, never into the ground.
                    lift = Mathf.MoveTowards(lift, Mathf.Max(placeH - 0.09f, 0.004f), dt * 0.3f);
                    if (held != null && lift <= placeH + 0.001f)
                    {
                        // Seated: exactly on the slot, square to the stack.
                        held.SetParent(transform.parent, true);
                        held.SetPositionAndRotation(Slot(Target) + Vector3.up * (stacks[Target].Count * UnitHeight), Quaternion.LookRotation(Dir));
                        stacks[Target].Add(held);
                        held = null;
                        Trips++;
                    }
                    if (held == null && lift <= Mathf.Max(placeH - 0.09f, 0.004f) + 0.001f)
                    {
                        var face = Facing(Target);
                        Leg(transform.position - face * 2.6f, face, true);
                        step = Step.Back; t = 0f;
                    }
                    break;
                case Step.Back:
                    if (Drive(dt, 1.0f))
                    {
                        // Swing round toward the source stack: reverse arc, then the approach leg.
                        var face = Facing(Target);
                        var side = Vector3.Cross(Vector3.up, face);
                        if (stacks[Source].Count == 0) aToB = !aToB;
                        Leg(transform.position - face * 2.8f - side * 1.8f, Quaternion.AngleAxis(90f, Vector3.up) * face, true);
                        step = Step.Withdraw; t = 0f;
                    }
                    break;
            }

            if (carriage != null) { var lp = carriage.localPosition; carriage.localPosition = new Vector3(lp.x, lift, lp.z); }
            if (innerMast != null) { var lp = innerMast.localPosition; innerMast.localPosition = new Vector3(lp.x, Mathf.Max(0f, lift - FreeLift), lp.z); }
            if (mast != null) mast.localRotation = Quaternion.Euler(tilt, 0f, 0f);
            foreach (var w in wheels) if (w != null) w.localRotation = Quaternion.Euler(spin, 0f, 0f);
            foreach (var w in steerWheels) if (w != null) w.localRotation = Quaternion.Euler(0f, steer, 0f);
            bool backing = reverse && v > 0.05f;
            if (reverseBeep != null)
            {
                if (backing && !reverseBeep.isPlaying) reverseBeep.Play();
                else if (!backing && reverseBeep.isPlaying) reverseBeep.Stop();
            }
            if (beacon != null)
            {
                block ??= new MaterialPropertyBlock();
                bool on = v > 0.05f && Mathf.Repeat(Time.time * 1.8f, 1f) < 0.45f;
                beacon.GetPropertyBlock(block);
                block.SetColor(EmissionColor, on ? new Color(4f, 1.8f, 0.1f) : new Color(0.15f, 0.06f, 0f));
                beacon.SetPropertyBlock(block);
            }
        }

#if UNITY_EDITOR
        public void EditorConfigure(Transform forks, Transform mastPivot, Transform inner, Transform[] wheelList, Transform[] steered, Renderer amber, Vector3 a, Vector3 b, Transform[] palletLoads, AudioSource beep)
        {
            carriage = forks; mast = mastPivot; innerMast = inner; wheels = wheelList; steerWheels = steered; beacon = amber; pointA = a; pointB = b; loads = palletLoads; reverseBeep = beep;
        }
#endif
    }
}
