using System;
using UnityEngine;

namespace NeonRift.HarborRain
{
    /// <summary>
    /// The restricted bay's rail-mounted gantry crane, the machine that does the heist. One gantry runs along its rails
    /// (z), a trolley runs along the girders (x), and the spreader hangs from the trolley on four wire ropes that are
    /// stretched between the sheaves every frame. Every axis accelerates and brakes smoothly; the spreader swings as a
    /// pendulum of the current rope length when the trolley or gantry starts or stops, and is held still once seated.
    ///
    /// Sequence (driven by the mission, the puzzle stays the authority on success):
    /// <see cref="Prepare"/> — the gantry travels over the target box with the spreader high, work lights on the box;
    /// <see cref="Connect"/> — the trolley creeps the last metres to square up, the corner flippers drop, the spreader
    /// lowers onto the corner castings and seats with a small bounce; <see cref="Lock"/> — the four twistlocks turn in one
    /// after another (each lamp goes green, each makes its clack); <see cref="SetLift"/> / <see cref="Hoist"/> — the ropes
    /// take the weight (the trolley sags, the ropes groan) and the box comes up slowly, water streaming off it;
    /// <see cref="Deliver"/> — after the theft the box is carried, one axis at a time, onto the crew's transfer trailer,
    /// lowered, unlocked, and the spreader goes back up. The box never passes below 8 m over the lane while carried.
    /// </summary>
    public sealed class HarborBayCrane : MonoBehaviour
    {
        public enum Step { Parked, Positioning, Ready, Aligning, Descending, Seated, Locking, Locked, Lifting, Hoisting, Carrying, Lowering, Unlocking, Delivered, Returning }

        [Header("Structure")]
        [SerializeField] private Transform gantry;
        [SerializeField] private Transform trolley;
        [SerializeField] private Transform spreader;
        [SerializeField] private Transform[] ropes = Array.Empty<Transform>();
        [SerializeField] private Transform[] ropeTops = Array.Empty<Transform>();
        [SerializeField] private Transform[] ropeBottoms = Array.Empty<Transform>();
        [SerializeField] private Transform[] locks = Array.Empty<Transform>();
        [SerializeField] private Transform[] flippers = Array.Empty<Transform>();
        [SerializeField] private Renderer[] cornerLamps = Array.Empty<Renderer>();
        [SerializeField] private Renderer statusLamp;
        [SerializeField] private Renderer[] beacons = Array.Empty<Renderer>();
        [SerializeField] private Light[] floods = Array.Empty<Light>();
        [SerializeField] private Light targetLight;
        [Header("Cargo")]
        [SerializeField] private Transform[] boxes = Array.Empty<Transform>();
        [Tooltip("Where a delivered box stands (its origin = bottom centre), on the transfer trailer.")]
        [SerializeField] private Vector3 pad = new(196f, 1.45f, 160f);
        [Header("Motion")]
        [SerializeField] private float parkZ = 160f, parkX = 225f;
        [Tooltip("Spreader bottom while travelling (clear of 4-high stacks).")]
        [SerializeField] private float travelY = 14f;
        [SerializeField] private float carryClearance = 9f;
        [SerializeField] private float gantrySpeed = 4f, trolleySpeed = 3f, hoistSpeed = 2.6f;
        [SerializeField] private float alignOffset = 3.4f;
        [Header("Audio")]
        [SerializeField] private AudioSource motor;
        [SerializeField] private AudioSource sfx;
        [SerializeField] private AudioClip clack, impact, strain, hydraulic;

        public const float BoxHeight = 2.6f;
        public Step Current { get; private set; }
        public int Target { get; private set; }
        public int LocksEngaged { get; private set; }
        public bool Carrying { get; private set; }
        /// <summary>Height of the target box's bottom above the ground, m.</summary>
        public float BoxLift => Target < boxes.Length && boxes[Target] != null ? boxes[Target].position.y - rest[Target].y : 0f;
        public float SpreaderBottom => sy;
        public float SwayDegrees => Mathf.Max(Mathf.Abs(swayX), Mathf.Abs(swayZ)) * Mathf.Rad2Deg;
        public float LowestCarriedOverLane { get; private set; } = float.MaxValue;
        public int Deliveries { get; private set; }
        public bool Moving { get; private set; }
        public Transform TargetBox => Target < boxes.Length ? boxes[Target] : null;

        private Vector3[] rest = Array.Empty<Vector3>();
        private float gz, tx, sy, gzV, txV, syV;          // axis positions and velocities
        private float wantZ, wantX, wantY;
        private float swayX, swayZ, swayXV, swayZV;       // pendulum (radians)
        private float flip, lockTimer, sag, bounce, bounceV, stepTime;
        private bool fast, seatedPlayed, preLower;
        private float ringT = -1f;            // rope ring after the load is taken
        private float[] lockFlash = new float[4];
        private int deliverPhase;
        private MaterialPropertyBlock block;
        private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
        private const float SheaveDrop = 2.8f;            // trolley origin to the sheave line

        private void Awake()
        {
            rest = new Vector3[boxes.Length];
            for (int i = 0; i < boxes.Length; i++) if (boxes[i] != null) rest[i] = boxes[i].position;
            ResetCrane();
        }

        // ---------------- Commands ----------------

        public void ResetCrane()
        {
            for (int i = 0; i < boxes.Length && i < rest.Length; i++)
                if (boxes[i] != null) boxes[i].SetPositionAndRotation(rest[i], Quaternion.identity);
            gz = wantZ = parkZ; tx = wantX = parkX; sy = wantY = travelY;
            gzV = txV = syV = swayX = swayZ = swayXV = swayZV = 0f;
            flip = sag = bounce = bounceV = 0f;
            LocksEngaged = 0;
            Carrying = fast = seatedPlayed = false;
            deliverPhase = 0;
            LowestCarriedOverLane = float.MaxValue;
            Enter(Step.Parked);
            foreach (var l in floods) if (l != null) l.enabled = false;
            if (targetLight != null) targetLight.enabled = false;
            Pose(0f);
        }

        /// <summary>Stage A: bring the gantry over the target with the spreader high and light the box.</summary>
        public void Prepare(int target)
        {
            if (Current is not (Step.Parked or Step.Ready or Step.Positioning) || target >= boxes.Length) return;
            Target = target;
            var b = rest[target];
            wantZ = b.z;
            wantX = b.x + (b.x < parkX ? alignOffset : -alignOffset);
            // Pre-lower to 4 m over the box once over it (the column is clear: bays keep a gap to the stacks).
            wantY = travelY;
            preLower = true;
            foreach (var l in floods) if (l != null) l.enabled = true;
            if (targetLight != null) targetLight.enabled = true;
            Enter(Step.Positioning);
        }

        /// <summary>Stage B: square up over the corner castings and lower onto the box (call when the lock puzzle opens).</summary>
        public void Connect()
        {
            if (Current is Step.Parked) Prepare(Target);
            if (Current is Step.Positioning or Step.Ready) { wantX = rest[Target].x; Enter(Step.Aligning); Play(hydraulic, 0.5f); }
        }

        /// <summary>Turn the twistlocks (sequentially; returns immediately).</summary>
        public void Lock()
        {
            if (Current == Step.Seated) { Enter(Step.Locking); lockTimer = 0.15f; }
        }

        /// <summary>The puzzle's lift phase: box bottom <paramref name="metres"/> above its bay (0..~1.2).</summary>
        public void SetLift(float metres)
        {
            if (Current is not (Step.Locked or Step.Lifting)) return;
            if (Current == Step.Locked && metres > 0.02f) { Enter(Step.Lifting); Carrying = true; Play(strain, 0.8f); TakeWeight(); }
            if (Current == Step.Lifting) wantY = rest[Target].y + BoxHeight + Mathf.Max(0f, metres);
        }

        /// <summary>The lift beat: hoist the box to <paramref name="height"/> m (box bottom above its bay). Finishes any seating first.</summary>
        public void Hoist(float height)
        {
            if (Current is Step.Locked or Step.Lifting) { Carrying = true; if (Current == Step.Locked) { Play(strain, 0.8f); TakeWeight(); } Enter(Step.Hoisting); }
            if (Current == Step.Hoisting) wantY = rest[Target].y + BoxHeight + height;
            else fast = true;   // still aligning / descending / locking: hurry the remaining steps
        }

        /// <summary>After the theft: carry the box to the transfer trailer, set it down, release it and lift clear.</summary>
        public void Deliver()
        {
            if (!Carrying) return;
            deliverPhase = 0;
            Enter(Step.Carrying);
        }

        /// <summary>Puzzle cancelled before the lift: unlock and lift clear, ready to try again.</summary>
        public void Abort()
        {
            if (Carrying || Current is Step.Parked or Step.Positioning or Step.Ready) return;
            for (int i = 0; i < locks.Length; i++) SetLock(i, false);
            LocksEngaged = 0;
            wantY = travelY;
            wantX = rest[Target].x + (rest[Target].x < parkX ? alignOffset : -alignOffset);
            seatedPlayed = false;
            Enter(Step.Returning);
        }

        public bool Seated => Current is Step.Seated or Step.Locking or Step.Locked;

        /// <summary>The ropes take the load: the frame dips, the box starts a small damped swing, the ropes ring.</summary>
        private void TakeWeight()
        {
            bounceV -= 0.12f;
            swayXV += 0.035f;
            swayZV -= 0.025f;
            ringT = 0f;
        }

        /// <summary>
        /// Safety net for a very slow frame rate: finish squaring up, seating and locking at once (the frame is already over
        /// the box, so the remaining move is at most a few metres of the empty spreader; the box itself does not move).
        /// </summary>
        public void CompleteSeating()
        {
            if (Carrying || Target >= rest.Length) return;
            float boxTop = rest[Target].y + BoxHeight;
            gz = wantZ = rest[Target].z; tx = wantX = rest[Target].x; sy = wantY = boxTop;
            gzV = txV = syV = swayX = swayZ = swayXV = swayZV = 0f;
            flip = 1f;
            for (int i = 0; i < locks.Length; i++) SetLock(i, true);
            LocksEngaged = locks.Length;
            seatedPlayed = true;
            Enter(Step.Locked);
        }
        public bool Locked => Current is Step.Locked or Step.Lifting or Step.Hoisting || Carrying;

        // ---------------- Simulation ----------------

        /// <summary>Every step the crane enters, in order (several can pass inside one frame's sub-steps).</summary>
        public event System.Action<Step> StepEntered;

        private void Enter(Step s) { Current = s; stepTime = 0f; StepEntered?.Invoke(s); }

        /// <summary>Largest simulation step, s. The axes and the rope swing are integrated in fixed sub-steps no longer than
        /// this, so the crane behaves the same at any frame rate: a long frame (a hitch, a throttled editor, a slow
        /// device) no longer leaves the trolley oscillating round its target, which used to stall alignment.</summary>
        private const float MaxStep = 1f / 60f;

        private void Update()
        {
            float frame = Mathf.Min(Time.deltaTime, 0.25f);
            int steps = Mathf.Max(1, Mathf.CeilToInt(frame / MaxStep - 1e-4f));
            float h = frame / steps;
            for (int i = 0; i < steps; i++) Simulate(h);
            Pose(frame);
            Feedback();
        }

        private void Simulate(float dt)
        {
            stepTime += dt;
            float boxTop = Target < rest.Length ? rest[Target].y + BoxHeight : 2.6f;
            switch (Current)
            {
                case Step.Positioning:
                    if (preLower && Near(gz, wantZ, 0.3f) && Near(tx, wantX, 0.3f)) { wantY = boxTop + 4f; preLower = false; }
                    if (!preLower && Near(gz, wantZ, 0.05f) && Near(tx, wantX, 0.05f) && Near(sy, wantY, 0.05f)) Enter(Step.Ready);
                    break;
                case Step.Aligning:
                    flip = Mathf.MoveTowards(flip, 1f, dt / 0.9f);
                    if (Near(tx, wantX, 0.03f) && Mathf.Abs(txV) < 0.05f && SwayDegrees < (fast ? 2f : 1f) && flip >= 1f)
                    {
                        wantY = boxTop;
                        Enter(Step.Descending);
                    }
                    break;
                case Step.Descending:
                    if (Near(sy, boxTop, 0.01f))
                    {
                        sy = boxTop; syV = 0f;
                        bounceV = -0.35f;   // the frame lands on the castings and settles
                        if (!seatedPlayed) { Play(impact, 0.55f); seatedPlayed = true; }
                        Enter(Step.Seated);
                    }
                    break;
                case Step.Seated:
                    if (fast) Lock();
                    break;
                case Step.Locking:
                    lockTimer -= dt * (fast ? 2.5f : 1f);
                    if (lockTimer <= 0f && LocksEngaged < locks.Length)
                    {
                        SetLock(LocksEngaged, true);
                        if (LocksEngaged < lockFlash.Length) lockFlash[LocksEngaged] = 0.3f;
                        LocksEngaged++;
                        bounceV -= 0.06f;   // each pin turning home jolts the frame a little
                        Play(clack, 0.85f);
                        lockTimer = 0.24f;
                    }
                    if (LocksEngaged >= locks.Length && lockTimer <= 0f) { Enter(Step.Locked); flip = 1f; }
                    break;
                case Step.Lifting:
                case Step.Hoisting:
                    // The ropes take the weight: the trolley sags a few centimetres and the girders ring; the flippers stow.
                    sag = Mathf.MoveTowards(sag, 0.06f, dt * 0.2f);
                    if (flip >= 1f && stepTime > 0.6f) Play(hydraulic, 0.35f);
                    if (stepTime > 0.6f) flip = Mathf.MoveTowards(flip, 0f, dt / 1.5f);
                    break;
                case Step.Carrying:
                    // One axis at a time, as a crane driver would: up to carry height, along the rails, across, then down.
                    float carryY = Mathf.Max(pad.y + BoxHeight + 0.5f, carryClearance) + BoxHeight;
                    if (deliverPhase == 0) { wantY = Mathf.Max(wantY, carryY); if (Near(sy, wantY, 0.05f)) deliverPhase = 1; }
                    if (deliverPhase == 1) { wantZ = pad.z; if (Near(gz, wantZ, 0.05f) && Mathf.Abs(gzV) < 0.05f) deliverPhase = 2; }
                    if (deliverPhase == 2) { wantX = pad.x; if (Near(tx, wantX, 0.05f) && Mathf.Abs(txV) < 0.05f && SwayDegrees < 1f) deliverPhase = 3; }
                    if (deliverPhase == 3) { wantY = pad.y + BoxHeight; if (Near(sy, wantY, 0.01f)) { bounceV = -0.25f; Play(impact, 0.45f); Enter(Step.Unlocking); lockTimer = 0.6f; } }
                    if (Carrying && TargetBox != null && Mathf.Abs(TargetBox.position.x - 225f) < 13f) LowestCarriedOverLane = Mathf.Min(LowestCarriedOverLane, BoxLift);
                    break;
                case Step.Unlocking:
                    lockTimer -= dt;
                    if (lockTimer <= 0f && LocksEngaged > 0)
                    {
                        LocksEngaged--;
                        SetLock(LocksEngaged, false);
                        Play(clack, 0.7f);
                        lockTimer = 0.22f;
                        if (LocksEngaged == 0) { Carrying = false; Deliveries++; sag = 0f; wantY = travelY; Enter(Step.Delivered); }
                    }
                    break;
                case Step.Delivered:
                    if (Near(sy, wantY, 0.05f)) { wantZ = parkZ; wantX = parkX; Enter(Step.Returning); }
                    break;
                case Step.Returning:
                    if (Near(gz, wantZ, 0.05f) && Near(tx, wantX, 0.05f) && Near(sy, wantY, 0.05f))
                        Enter(Deliveries > 0 ? Step.Parked : Step.Ready);
                    break;
            }
            if (Current is not (Step.Lifting or Step.Hoisting or Step.Carrying or Step.Unlocking)) sag = Mathf.MoveTowards(sag, 0f, dt * 0.2f);
            if (Current is Step.Carrying or Step.Unlocking or Step.Delivered or Step.Returning or Step.Parked or Step.Positioning or Step.Ready) flip = Mathf.MoveTowards(flip, 0f, dt / 1.5f);

            // Axes: critically damped approach with a speed cap (smooth starts and stops, never a jump).
            float load = Carrying ? 0.7f : 1f;
            float accelGantry = Axis(ref gz, ref gzV, wantZ, gantrySpeed * load * (fast ? 1.6f : 1f), dt, 1.2f);
            float accelTrolley = Axis(ref tx, ref txV, wantX, trolleySpeed * load * (fast ? 1.6f : 1f), dt, 1.1f);
            // Hoist slows for the last metre to the castings.
            float hoistCap = hoistSpeed * (fast ? 2f : 1f) * (Current == Step.Descending && sy - boxTop < 1.0f ? 0.4f : 1f) * (Carrying ? 0.6f : 1f);
            Axis(ref sy, ref syV, wantY, hoistCap, dt, 1.2f);
            Moving = Mathf.Abs(gzV) > 0.05f || Mathf.Abs(txV) > 0.05f || Mathf.Abs(syV) > 0.05f;

            // Pendulum: rope length L, driven by the trolley / gantry acceleration; held when seated or resting on the box.
            bool held = Seated || Current is Step.Descending && sy - boxTop < 0.3f || Current is Step.Lowering or Step.Unlocking;
            float L = Mathf.Max(1.5f, gantryTopSheave - sy);
            // Electronic anti-sway (as fitted to yard cranes) damps the swing hard once the trolley is aligning or stopped.
            float w2 = 9.81f / L, damp = Current is Step.Aligning or Step.Ready ? 2.2f : Carrying ? 0.9f : 0.6f;
            // (+θ hangs the frame toward −x / −z, so a forward acceleration of the pivot swings it back: + a / L.)
            swayXV += (-w2 * swayX + accelTrolley / L - damp * swayXV) * dt;
            swayZV += (-w2 * swayZ + accelGantry / L - damp * swayZV) * dt;
            swayX += swayXV * dt; swayZ += swayZV * dt;
            if (held) { swayX = Mathf.MoveTowards(swayX, 0f, dt); swayZ = Mathf.MoveTowards(swayZ, 0f, dt); swayXV = swayZV = 0f; }
            swayX = Mathf.Clamp(swayX, -0.06f, 0.06f); swayZ = Mathf.Clamp(swayZ, -0.06f, 0.06f);
            if (ringT >= 0f) { ringT += dt; if (ringT > 2.5f) ringT = -1f; }
            for (int i = 0; i < lockFlash.Length; i++) lockFlash[i] = Mathf.Max(0f, lockFlash[i] - dt);
            bounceV += (-140f * bounce - 9f * bounceV) * dt;
            bounce += bounceV * dt;
        }

        private float gantryTopSheave => gantry != null && trolley != null ? trolley.position.y - SheaveDrop : 23f;

        /// <summary>Moves x toward target with a speed cap and smooth acceleration; returns the acceleration (m/s²).</summary>
        private static float Axis(ref float x, ref float v, float target, float maxSpeed, float dt, float accel)
        {
            float d = target - x;
            // Stopping-distance controller: the speed that still stops in the remaining distance, capped.
            float want = Mathf.Sign(d) * Mathf.Min(maxSpeed, Mathf.Sqrt(2f * accel * Mathf.Abs(d)));
            float before = v;
            v = Mathf.MoveTowards(v, want, accel * dt * 1.6f);
            if (Mathf.Abs(d) < 0.004f && Mathf.Abs(v) < 0.05f) { x = target; v = 0f; }
            else x += v * dt;
            return dt > 0f ? (v - before) / dt : 0f;
        }

        private static bool Near(float a, float b, float tol) => Mathf.Abs(a - b) <= tol;

        private void Pose(float dt)
        {
            if (gantry != null) { var p = gantry.position; gantry.position = new Vector3(p.x, p.y, gz); }
            if (trolley != null) trolley.position = new Vector3(tx, gantryTrolleyY - sag, gz);
            float L = Mathf.Max(1.5f, gantryTopSheave - sy);
            var pivot = new Vector3(tx, gantryTopSheave, gz);
            var rot = Quaternion.Euler(swayZ * Mathf.Rad2Deg, 0f, -swayX * Mathf.Rad2Deg);
            // Spreader origin = bottom centre of the frame; it hangs L below the sheave line, rotated by the swing.
            var hang = pivot + rot * (Vector3.down * L);
            if (spreader != null) spreader.SetPositionAndRotation(new Vector3(hang.x, sy + bounce, hang.z), rot);
            for (int i = 0; i < ropes.Length && i < ropeTops.Length && i < ropeBottoms.Length; i++)
            {
                if (ropes[i] == null || ropeTops[i] == null || ropeBottoms[i] == null) continue;
                Vector3 a = ropeBottoms[i].position, b = ropeTops[i].position, d = b - a;
                if (ringT >= 0f) d += (i % 2 == 0 ? Vector3.right : Vector3.forward) * (Mathf.Sin(ringT * 26f + i) * 0.05f * Mathf.Exp(-ringT * 2.2f));   // the ropes ring as they take the load
                ropes[i].SetPositionAndRotation(a, Quaternion.FromToRotation(Vector3.up, d));
                ropes[i].localScale = new Vector3(1f, d.magnitude, 1f);
            }
            // Corner flippers hinge on the frame end: stowed pointing up (180°), deployed hanging down round the corner.
            for (int i = 0; i < flippers.Length; i++) if (flippers[i] != null) flippers[i].localRotation = Quaternion.Euler(180f * (1f - flip), 0f, 0f);
            if (Carrying && TargetBox != null && spreader != null)
                TargetBox.SetPositionAndRotation(spreader.position - spreader.up * BoxHeight, spreader.rotation);
        }

        [SerializeField, HideInInspector] private float gantryTrolleyY = 26.4f;

        private void SetLock(int i, bool on)
        {
            if (i < locks.Length && locks[i] != null) locks[i].localRotation = Quaternion.Euler(0f, on ? 90f : 0f, 0f);
        }

        private void Feedback()
        {
            block ??= new MaterialPropertyBlock();
            bool flash = Mathf.Repeat(Time.time * 2.4f, 1f) < 0.5f;
            for (int i = 0; i < cornerLamps.Length; i++)
            {
                var r = cornerLamps[i];
                if (r == null) continue;
                bool on = i < LocksEngaged;
                Color c = i < lockFlash.Length && lockFlash[i] > 0f ? new Color(6f, 6f, 5.5f) : on ? new Color(0.3f, 4f, 0.7f) : Seated || Current == Step.Unlocking ? new Color(4f, 1.8f, 0.1f) : new Color(0.5f, 0.22f, 0.02f);
                r.GetPropertyBlock(block); block.SetColor(EmissionColor, c); r.SetPropertyBlock(block);
            }
            if (statusLamp != null)
            {
                Color c = Carrying ? (flash ? new Color(5f, 0.3f, 0.2f) : new Color(0.5f, 0.02f, 0.01f)) : LocksEngaged == locks.Length && locks.Length > 0 ? new Color(0.3f, 4.5f, 0.8f)
                        : Moving ? (flash ? new Color(4.5f, 2.2f, 0.15f) : new Color(0.4f, 0.18f, 0.02f)) : new Color(1.2f, 0.55f, 0.05f);
                statusLamp.GetPropertyBlock(block); block.SetColor(EmissionColor, c); statusLamp.SetPropertyBlock(block);
            }
            bool travel = Mathf.Abs(gzV) > 0.05f || Mathf.Abs(txV) > 0.05f;
            foreach (var b in beacons)
            {
                if (b == null) continue;
                b.GetPropertyBlock(block); block.SetColor(EmissionColor, travel && flash ? new Color(4f, 1.8f, 0.1f) : new Color(0.25f, 0.1f, 0.01f)); b.SetPropertyBlock(block);
            }
            if (motor != null)
            {
                float target = Moving ? Mathf.Clamp01(Mathf.Max(Mathf.Abs(gzV) / gantrySpeed, Mathf.Abs(txV) / trolleySpeed, Mathf.Abs(syV) / hoistSpeed)) : 0f;
                motor.volume = Mathf.MoveTowards(motor.volume, target * 0.55f, Time.deltaTime * 1.5f);
                motor.pitch = 0.75f + target * 0.35f + (Carrying ? -0.08f : 0f);
                if (motor.volume > 0.01f && !motor.isPlaying) motor.Play();
                else if (motor.volume <= 0.01f && motor.isPlaying) motor.Stop();
            }
            if (targetLight != null && targetLight.enabled && TargetBox != null)
                targetLight.transform.rotation = Quaternion.LookRotation(TargetBox.position + Vector3.up * 1.3f - targetLight.transform.position);
        }

        private void Play(AudioClip clip, float volume)
        {
            if (sfx != null && clip != null) sfx.PlayOneShot(clip, volume);
        }

#if UNITY_EDITOR
        public void EditorConfigure(Transform g, Transform t, Transform s, Transform[] ropeList, Transform[] tops, Transform[] bottoms, Transform[] lockList, Transform[] flipperList,
                                    Renderer[] corners, Renderer status, Renderer[] beaconList, Light[] floodList, Light target, Transform[] boxList, Vector3 padAt,
                                    float trolleyY, AudioSource motorSource, AudioSource sfxSource, AudioClip clackClip, AudioClip impactClip, AudioClip strainClip, AudioClip hydraulicClip)
        {
            gantry = g; trolley = t; spreader = s; ropes = ropeList; ropeTops = tops; ropeBottoms = bottoms; locks = lockList; flippers = flipperList; cornerLamps = corners;
            statusLamp = status; beacons = beaconList; floods = floodList; targetLight = target; boxes = boxList; pad = padAt; gantryTrolleyY = trolleyY;
            motor = motorSource; sfx = sfxSource; clack = clackClip; impact = impactClip; strain = strainClip; hydraulic = hydraulicClip;
        }
#endif
    }
}
