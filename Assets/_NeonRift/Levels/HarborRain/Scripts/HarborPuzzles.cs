using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace NeonRift.HarborRain
{
    /// <summary>Edge-triggered puzzle input, filled from the driving controls (keyboard / gamepad) or touch.</summary>
    public struct PuzzleInput
    {
        public bool Left, Right, Up, Down, Confirm, Cancel, ConfirmHeld;
        public float Steer;
    }

    /// <summary>
    /// A short, skill-based security challenge shown while the car is parked. Four small types, shared by the
    /// mission's obstacles: <see cref="ManifestPuzzle"/> (deduce the container from clues), <see cref="ReelPuzzle"/>
    /// (lock rotating badge reels on the right symbol), <see cref="LockPuzzle"/> (time twistlock rings, then hold a
    /// drifting seal signal in band, then lift) and <see cref="PowerPuzzle"/> (rotate junction tiles to route power).
    /// Every type has a success path, a failure path that never soft-locks (retry or a time/suspicion cost), and is
    /// drawn with Painter2D in <see cref="HarborPuzzleView"/>. Mouse / touch clicks are supported on every element.
    /// </summary>
    public abstract class HarborPuzzle
    {
        public enum State { Running, Solved, Failed }

        public State Status { get; protected set; }
        public int Mistakes { get; protected set; }
        public float Elapsed { get; protected set; }
        public float TimeLimit { get; protected set; }
        public abstract string Title { get; }
        public abstract string Instructions { get; }
        public virtual string Body => "";
        /// <summary>"tick", "confirm", "error", "success", "spark" — the mission plays the matching cue.</summary>
        public event Action<string> Cue;
        protected void Play(string cue) => Cue?.Invoke(cue);

        public float TimeLeft => TimeLimit > 0f ? Mathf.Max(0f, TimeLimit - Elapsed) : -1f;

        public void Tick(float dt, PuzzleInput input)
        {
            if (Status != State.Running) return;
            Elapsed += dt;
            Update(dt, input);
            if (Status == State.Running && TimeLimit > 0f && Elapsed >= TimeLimit) Fail();
        }

        protected abstract void Update(float dt, PuzzleInput input);
        public abstract void Draw(Painter2D p, Rect area);
        public virtual void Click(Vector2 local, Rect area) { }

        protected void Solve() { Status = State.Solved; Play("success"); }
        protected void Fail() { Status = State.Failed; Play("spark"); }
        protected void Mistake() { Mistakes++; Play("error"); }

        // Shared drawing helpers.
        protected static readonly Color Cyan = new(0.33f, 0.91f, 1f), Amber = new(1f, 0.7f, 0.2f), Red = new(1f, 0.3f, 0.25f),
                                         Green = new(0.35f, 1f, 0.55f), Dim = new(0.3f, 0.36f, 0.42f), Panel = new(0.03f, 0.05f, 0.08f, 0.95f);

        protected static void Box(Painter2D p, Rect r, Color fill, Color? line = null, float width = 2f)
        {
            p.fillColor = fill;
            p.BeginPath();
            p.MoveTo(new Vector2(r.xMin, r.yMin)); p.LineTo(new Vector2(r.xMax, r.yMin)); p.LineTo(new Vector2(r.xMax, r.yMax)); p.LineTo(new Vector2(r.xMin, r.yMax));
            p.ClosePath();
            p.Fill();
            if (line == null) return;
            p.strokeColor = line.Value;
            p.lineWidth = width;
            p.Stroke();
        }

        protected static void Glyph(Painter2D p, int symbol, Vector2 c, float s, Color colour)
        {
            p.fillColor = colour;
            p.strokeColor = colour;
            p.lineWidth = s * 0.22f;
            p.BeginPath();
            switch (symbol % 6)
            {
                case 0: p.MoveTo(c + new Vector2(0, -s)); p.LineTo(c + new Vector2(s, s * 0.8f)); p.LineTo(c + new Vector2(-s, s * 0.8f)); p.ClosePath(); p.Fill(); break;
                case 1: p.MoveTo(c + new Vector2(-s, -s)); p.LineTo(c + new Vector2(s, -s)); p.LineTo(c + new Vector2(s, s)); p.LineTo(c + new Vector2(-s, s)); p.ClosePath(); p.Fill(); break;
                case 2: p.Arc(c, s, 0f, 360f); p.Fill(); break;
                case 3: p.MoveTo(c + new Vector2(0, -s)); p.LineTo(c + new Vector2(s, 0)); p.LineTo(c + new Vector2(0, s)); p.LineTo(c + new Vector2(-s, 0)); p.ClosePath(); p.Fill(); break;
                case 4: p.MoveTo(c + new Vector2(-s, 0)); p.LineTo(c + new Vector2(s, 0)); p.MoveTo(c + new Vector2(0, -s)); p.LineTo(c + new Vector2(0, s)); p.Stroke(); break;
                default: p.MoveTo(c + new Vector2(-s, -s)); p.LineTo(c + new Vector2(s, s)); p.MoveTo(c + new Vector2(s, -s)); p.LineTo(c + new Vector2(-s, s)); p.Stroke(); break;
            }
        }
    }

    // ---------------------------------------------------------------- A: cargo manifest

    /// <summary>
    /// Four manifest rows; three clues. The target matches all clues, each decoy fails at least one, so exactly one
    /// row is correct. A wrong pick strikes the row out and costs suspicion (the mission's call); it never ends the puzzle.
    /// </summary>
    public sealed class ManifestPuzzle : HarborPuzzle
    {
        public struct Row { public string Id, Destination, Class; public float Weight; public int Bay; public bool Struck; }

        public readonly Row[] Rows = new Row[4];
        public readonly string[] Clues = new string[3];
        public int Selected { get; private set; }
        public int Answer { get; private set; }
        public int AnswerBay => Rows[Answer].Bay;

        public override string Title => "CARGO MANIFEST  ·  TERMINAL 4";
        public override string Instructions => "KX-7 is listed under a cover ID. Pick the container that matches ALL three clues.   ▲▼ select   E confirm";
        public override string Body => "CLUES\n" + string.Join("\n", Clues);

        private static readonly string[] Ports = { "ROTTERDAM", "BUSAN", "VALPARAÍSO", "DURBAN", "ANTWERP", "SANTOS" };
        private static readonly string[] Classes = { "S-4 RESTRICTED", "C-1 GENERAL", "C-2 GENERAL", "H-3 HAZARD" };

        public ManifestPuzzle(System.Random rng, int targetBay)
        {
            string port = Ports[rng.Next(Ports.Length)];
            float limit = 22f + rng.Next(0, 5);
            Clues[0] = $"·  DESTINATION  {port}";
            Clues[1] = $"·  GROSS WEIGHT OVER {limit:0} T";
            Clues[2] = "·  CLASS  S-4 RESTRICTED";
            string OtherPort() { string q; do q = Ports[rng.Next(Ports.Length)]; while (q == port); return q; }
            string Id() => $"{(char)('A' + rng.Next(26))}{(char)('A' + rng.Next(26))}{(char)('A' + rng.Next(26))}U {rng.Next(100000, 999999)}";
            var rows = new List<Row>
            {
                new() { Id = Id(), Destination = port, Weight = limit + 1.5f + rng.Next(0, 6), Class = Classes[0], Bay = targetBay },
                new() { Id = Id(), Destination = OtherPort(), Weight = limit + 2f + rng.Next(0, 5), Class = Classes[0], Bay = (targetBay + 1) % 3 },
                new() { Id = Id(), Destination = port, Weight = limit - 2f - rng.Next(0, 6), Class = Classes[0], Bay = (targetBay + 2) % 3 },
                new() { Id = Id(), Destination = port, Weight = limit + 3f + rng.Next(0, 4), Class = Classes[1 + rng.Next(3)], Bay = rng.Next(3) },
            };
            for (int i = rows.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); (rows[i], rows[j]) = (rows[j], rows[i]); }
            for (int i = 0; i < 4; i++) { Rows[i] = rows[i]; if (rows[i].Bay == targetBay && rows[i].Destination == port && rows[i].Weight > limit && rows[i].Class == Classes[0]) Answer = i; }
            TimeLimit = 0f;
        }

        public void Choose(int index)
        {
            if (Status != State.Running || index < 0 || index >= Rows.Length || Rows[index].Struck) return;
            Selected = index;
            if (index == Answer) Solve();
            else { Rows[index].Struck = true; Mistake(); }
        }

        protected override void Update(float dt, PuzzleInput input)
        {
            if (input.Up) { Selected = (Selected + 3) % 4; Play("tick"); }
            if (input.Down) { Selected = (Selected + 1) % 4; Play("tick"); }
            if (input.Confirm) Choose(Selected);
        }

        private Rect RowRect(Rect area, int i) => new(area.x, area.y + 36f + i * 54f, area.width, 46f);

        public override void Draw(Painter2D p, Rect area)
        {
            for (int i = 0; i < 4; i++)
            {
                var r = RowRect(area, i);
                var row = Rows[i];
                Color fill = row.Struck ? new Color(0.25f, 0.05f, 0.05f, 0.8f) : i == Selected ? new Color(0.05f, 0.25f, 0.32f, 0.95f) : Panel;
                Box(p, r, fill, i == Selected ? Cyan : Dim);
                if (row.Struck)
                {
                    p.strokeColor = Red; p.lineWidth = 3f;
                    p.BeginPath(); p.MoveTo(new Vector2(r.xMin + 10, r.center.y)); p.LineTo(new Vector2(r.xMax - 10, r.center.y)); p.Stroke();
                }
            }
        }

        /// <summary>Row text for the view (Painter2D has no text).</summary>
        public string RowText(int i) => $"{Rows[i].Id}      {Rows[i].Destination,-11}   {Rows[i].Weight,5:0.0} T     {Rows[i].Class}";
        public Rect RowArea(Rect area, int i) => RowRect(area, i);

        public override void Click(Vector2 local, Rect area)
        {
            for (int i = 0; i < 4; i++) if (RowRect(area, i).Contains(local)) { Choose(i); return; }
        }
    }

    // ---------------------------------------------------------------- B: security gate reels

    /// <summary>
    /// Badge reader bypass: four reels; the active one cycles through six symbols. Lock it (E / tap) while it shows the
    /// reel's target symbol. A wrong lock resets that reel and counts a mistake; three mistakes or the timer trip the
    /// reader (fail; the mission locks it out for a few seconds and starts a fresh code).
    /// </summary>
    public sealed class ReelPuzzle : HarborPuzzle
    {
        public readonly int[] Target = new int[4];
        public int Active { get; private set; }
        public int Showing => Mathf.FloorToInt(phase) % 6;
        private float phase;
        private readonly float[] speeds = { 1.6f, 2.0f, 2.4f, 2.9f };

        public override string Title => "SECURITY GATE  ·  BADGE READER BYPASS";
        public override string Instructions => "Lock each reel while it shows the code symbol above it.   E / tap  lock";
        public override string Body => $"REEL {Mathf.Min(Active + 1, 4)} / 4      ERRORS {Mistakes} / 3";

        public ReelPuzzle(System.Random rng)
        {
            for (int i = 0; i < 4; i++) Target[i] = rng.Next(6);
            phase = rng.Next(6);
            TimeLimit = 28f;
        }

        public void Lock()
        {
            if (Status != State.Running) return;
            if (Showing == Target[Active])
            {
                Play("confirm");
                Active++;
                if (Active >= 4) Solve();
            }
            else
            {
                Mistake();
                if (Mistakes >= 3) Fail();
            }
        }

        protected override void Update(float dt, PuzzleInput input)
        {
            int before = Showing;
            phase += dt * speeds[Mathf.Min(Active, 3)];
            if (Showing != before) Play("tick");
            if (input.Confirm) Lock();
        }

        public override void Draw(Painter2D p, Rect area)
        {
            float w = area.width / 4f;
            for (int i = 0; i < 4; i++)
            {
                var c = new Vector2(area.x + w * (i + 0.5f), area.y + area.height * 0.6f);
                var top = new Vector2(c.x, area.y + 40f);
                Glyph(p, Target[i], top, 14f, i < Active ? Green : Amber);
                var r = new Rect(c.x - w * 0.36f, c.y - 56f, w * 0.72f, 112f);
                bool done = i < Active, now = i == Active;
                Box(p, r, now ? new Color(0.05f, 0.2f, 0.26f, 0.95f) : Panel, done ? Green : now ? Cyan : Dim, now ? 3f : 2f);
                int sym = done ? Target[i] : now ? Showing : (Showing + i * 2) % 6;
                Glyph(p, sym, c, 28f, done ? Green : now ? Color.white : Dim);
                if (now)
                {
                    // Neighbouring symbols ghosted above and below, like a drum.
                    Glyph(p, (Showing + 5) % 6, c + new Vector2(0, -44f), 10f, Dim);
                    Glyph(p, (Showing + 1) % 6, c + new Vector2(0, 44f), 10f, Dim);
                }
            }
        }

        public override void Click(Vector2 local, Rect area) => Lock();
    }

    // ---------------------------------------------------------------- C: container lock (multi-stage)

    /// <summary>
    /// KX-7's container lock in three stages: (1) TWISTLOCKS — three rings spin; lock each when its notch is in the top
    /// window; (2) SEAL SIGNAL — a drifting needle must be held inside the green band (steer / hold the arrows) for
    /// <c>holdSeconds</c>; (3) LIFT — hold E to raise the spreader. With <c>signalOnly</c> it is just stage 2 (the
    /// optional relay spike). Mistakes count but never end it.
    /// </summary>
    public sealed class LockPuzzle : HarborPuzzle
    {
        public int Stage { get; private set; }
        private readonly bool signalOnly;
        private readonly float[] ringAngle = new float[3];
        private readonly float[] ringSpeed = { 150f, -190f, 230f };
        private int ring;
        private float needle, drift, driftTimer, inBand, lift, outside;
        private readonly float holdSeconds;
        private readonly System.Random rng;
        public float Needle => needle;
        /// <summary>True when the active twistlock ring's notch is inside the top window (what a correct press needs).</summary>
        public bool RingInWindow => Stage == 0 && ring < 3 && Mathf.Abs(Mathf.DeltaAngle(ringAngle[ring], 0f)) <= 12f;
        public float SignalProgress => inBand / holdSeconds;
        public float LiftProgress => lift;
        public float TouchSteer { get; set; }

        public override string Title => signalOnly ? "DRONE RELAY  ·  SIGNAL SPIKE" : "KX-7  ·  CONTAINER LOCK";
        public override string Instructions => Stage switch
        {
            0 => "STAGE 1 · TWISTLOCKS — lock each ring when its notch reaches the top window.   E / tap",
            1 => "STAGE 2 · SEAL SIGNAL — keep the needle inside the green band.   ◀ ▶ steer / hold",
            _ => "STAGE 3 · LIFT — hold E (or hold the button) to raise the spreader."
        };
        public override string Body => Stage switch
        {
            0 => $"TWISTLOCK {ring + 1} / 3",
            1 => $"SIGNAL LOCK  {Mathf.RoundToInt(SignalProgress * 100f)}%",
            _ => $"LIFT  {Mathf.RoundToInt(lift * 100f)}%"
        };

        public LockPuzzle(System.Random random, bool signalOnly = false, float holdSeconds = 3.5f)
        {
            rng = random;
            this.signalOnly = signalOnly;
            this.holdSeconds = holdSeconds;
            Stage = signalOnly ? 1 : 0;
            for (int i = 0; i < 3; i++) ringAngle[i] = rng.Next(360);
            TimeLimit = signalOnly ? 25f : 0f;
        }

        public void LockRing()
        {
            if (Status != State.Running || Stage != 0) return;
            float a = Mathf.DeltaAngle(ringAngle[ring], 0f);
            if (Mathf.Abs(a) <= 16f)
            {
                Play("confirm");
                ring++;
                if (ring >= 3) { Stage = 1; Play("tick"); }
            }
            else Mistake();
        }

        protected override void Update(float dt, PuzzleInput input)
        {
            switch (Stage)
            {
                case 0:
                    for (int i = ring; i < 3; i++) ringAngle[i] = Mathf.Repeat(ringAngle[i] + ringSpeed[i] * dt, 360f);
                    if (input.Confirm) LockRing();
                    break;
                case 1:
                    driftTimer -= dt;
                    if (driftTimer <= 0f) { driftTimer = 0.5f + (float)rng.NextDouble() * 0.8f; drift = ((float)rng.NextDouble() * 2f - 1f) * 1.3f; }
                    float steer = Mathf.Abs(input.Steer) > 0.05f ? input.Steer : TouchSteer;
                    needle = Mathf.Clamp(needle + (drift + steer * 2.2f) * dt, -1f, 1f);
                    bool band = Mathf.Abs(needle) < 0.28f;
                    if (band) { inBand += dt; outside = 0f; }
                    else
                    {
                        inBand = Mathf.Max(0f, inBand - dt * 0.5f);
                        outside += dt;
                        if (outside > 1.6f) { outside = 0f; Mistake(); }
                    }
                    if (inBand >= holdSeconds)
                    {
                        if (signalOnly) Solve();
                        else { Stage = 2; Play("confirm"); }
                    }
                    break;
                default:
                    lift = input.ConfirmHeld || TouchLift ? Mathf.Min(1f, lift + dt / 1.3f) : Mathf.Max(0f, lift - dt * 0.6f);
                    if (lift >= 1f) Solve();
                    break;
            }
        }

        public override void Draw(Painter2D p, Rect area)
        {
            var c = area.center + new Vector2(0f, 10f);
            if (Stage == 0)
            {
                for (int i = 0; i < 3; i++)
                {
                    float radius = 40f + i * 30f;
                    p.strokeColor = i < ring ? Green : i == ring ? Cyan : Dim;
                    p.lineWidth = i == ring ? 8f : 5f;
                    p.BeginPath(); p.Arc(c, radius, 0f, 360f); p.Stroke();
                    // Notch: a gap marker on the ring.
                    float ang = (i < ring ? 0f : ringAngle[i]) - 90f;
                    var n = c + new Vector2(Mathf.Cos(ang * Mathf.Deg2Rad), Mathf.Sin(ang * Mathf.Deg2Rad)) * radius;
                    p.fillColor = i < ring ? Green : Amber;
                    p.BeginPath(); p.Arc(n, 9f, 0f, 360f); p.Fill();
                }
                // The top window.
                Box(p, new Rect(c.x - 15f, c.y - 112f, 30f, 84f), new Color(0f, 0f, 0f, 0f), Color.white, 2f);
            }
            else if (Stage == 1)
            {
                var bar = new Rect(area.x + 40f, c.y - 18f, area.width - 80f, 36f);
                Box(p, bar, Panel, Dim);
                float bw = bar.width * 0.28f;
                Box(p, new Rect(bar.center.x - bw * 0.5f, bar.y, bw, bar.height), new Color(0.1f, 0.5f, 0.25f, 0.6f), Green);
                float x = bar.center.x + needle * bar.width * 0.5f;
                Box(p, new Rect(x - 4f, bar.y - 12f, 8f, bar.height + 24f), Mathf.Abs(needle) < 0.28f ? Color.white : Red);
                var prog = new Rect(bar.x, bar.yMax + 30f, bar.width * Mathf.Clamp01(SignalProgress), 10f);
                Box(p, prog, Cyan);
                // Touch arrows.
                Box(p, LeftButton(area), Panel, Cyan); Box(p, RightButton(area), Panel, Cyan);
                foreach (var (r, dir) in new[] { (LeftButton(area), -1f), (RightButton(area), 1f) })
                {
                    p.fillColor = Cyan;
                    p.BeginPath();
                    p.MoveTo(r.center + new Vector2(dir * 14f, 0f)); p.LineTo(r.center + new Vector2(-dir * 10f, -12f)); p.LineTo(r.center + new Vector2(-dir * 10f, 12f));
                    p.ClosePath();
                    p.Fill();
                }
            }
            else
            {
                var bar = new Rect(c.x - 30f, area.y + 20f, 60f, area.height - 60f);
                Box(p, bar, Panel, Dim);
                float h = bar.height * lift;
                Box(p, new Rect(bar.x, bar.yMax - h, bar.width, h), Cyan);
            }
        }

        private static Rect LeftButton(Rect area) => new(area.x, area.yMax - 60f, 90f, 54f);
        private static Rect RightButton(Rect area) => new(area.xMax - 90f, area.yMax - 60f, 90f, 54f);

        public override void Click(Vector2 local, Rect area)
        {
            if (Stage == 0) LockRing();
        }

        public void TouchHold(Vector2 local, Rect area, bool down)
        {
            if (Stage == 1) TouchSteer = !down ? 0f : LeftButton(area).Contains(local) ? -1f : RightButton(area).Contains(local) ? 1f : 0f;
            else if (Stage == 2) TouchLift = down;
        }

        public bool TouchLift { get; private set; }
    }

    // ---------------------------------------------------------------- D: emergency power routing

    /// <summary>
    /// Emergency override at the locked east gate: a 3 × 5 junction grid. Power enters at the left and must reach the
    /// gate's breaker on the right. Rotate tiles (select with the arrows, E to rotate, or tap a tile). The layout is
    /// generated around a guaranteed path, then scrambled. Timer expiry trips the breaker (fail → sparks, new layout).
    /// </summary>
    public sealed class PowerPuzzle : HarborPuzzle
    {
        public const int Rows = 3, Cols = 5;
        // Connection bits: 1 = up, 2 = right, 4 = down, 8 = left.
        private readonly int[,] shape = new int[Rows, Cols];
        private readonly int[,] rotation = new int[Rows, Cols];
        private readonly bool[,] powered = new bool[Rows, Cols];
        public readonly int SourceRow, OutputRow;
        public int CursorRow { get; private set; }
        public int CursorCol { get; private set; }
        private readonly List<(int r, int c, int rot)> solution = new();

        public override string Title => "EAST GATE  ·  EMERGENCY POWER OVERRIDE";
        public override string Instructions => "Rotate the junctions so power flows from the feed (left) to the gate breaker (right).   arrows select  ·  E / tap rotate";
        public override string Body => Status == State.Solved ? "BREAKER CLOSED" : "FEED  ▶  JUNCTIONS  ▶  GATE BREAKER";

        public PowerPuzzle(System.Random rng)
        {
            SourceRow = rng.Next(Rows);
            OutputRow = rng.Next(Rows);
            // A monotone path left → right with vertical moves.
            var path = new List<(int r, int c)>();
            int row = SourceRow;
            for (int c = 0; c < Cols; c++)
            {
                path.Add((row, c));
                int goal = c == Cols - 1 ? OutputRow : rng.Next(Rows);
                if (c == Cols - 2) goal = OutputRow;
                while (row != goal && c < Cols - 1) { row += Math.Sign(goal - row); path.Add((row, c)); }
            }
            for (int i = 0; i < path.Count; i++)
            {
                var (r, c) = path[i];
                int bits = 0;
                var a = i > 0 ? path[i - 1] : (r, c - 1);
                var b = i < path.Count - 1 ? path[i + 1] : (r, c + 1);
                foreach (var (nr, nc) in new[] { a, b })
                    bits |= nr < r ? 1 : nc > c ? 2 : nr > r ? 4 : 8;
                shape[r, c] = bits;
            }
            for (int r = 0; r < Rows; r++)
            for (int c = 0; c < Cols; c++)
            {
                if (shape[r, c] == 0) shape[r, c] = rng.Next(3) switch { 0 => 5, 1 => 3, _ => 7 };
                rotation[r, c] = rng.Next(4);
            }
            foreach (var (r, c) in path) solution.Add((r, c, 0));
            CursorRow = SourceRow;
            Recompute();
            if (Status == State.Solved) { rotation[path[0].r, path[0].c] = 1; Status = State.Running; Recompute(); }
            TimeLimit = 40f;
        }

        private static int Rotate(int bits, int turns)
        {
            for (int i = 0; i < turns; i++) bits = ((bits << 1) | (bits >> 3)) & 15;
            return bits;
        }

        public int Connections(int r, int c) => Rotate(shape[r, c], rotation[r, c]);
        public bool Powered(int r, int c) => powered[r, c];

        public void RotateTile(int r, int c)
        {
            if (Status != State.Running) return;
            rotation[r, c] = (rotation[r, c] + 1) % 4;
            CursorRow = r; CursorCol = c;
            Play("tick");
            Recompute();
        }

        /// <summary>Validation hook: set every path tile to the rotation that matches its generated shape.</summary>
        public void DebugSolve()
        {
            foreach (var (r, c, _) in solution) rotation[r, c] = 0;
            Recompute();
        }

        private void Recompute()
        {
            Array.Clear(powered, 0, powered.Length);
            if ((Connections(SourceRow, 0) & 8) == 0) return;
            var stack = new Stack<(int, int)>();
            stack.Push((SourceRow, 0));
            powered[SourceRow, 0] = true;
            while (stack.Count > 0)
            {
                var (r, c) = stack.Pop();
                int bits = Connections(r, c);
                if (r == OutputRow && c == Cols - 1 && (bits & 2) != 0) { if (Status == State.Running) Solve(); }
                foreach (var (dr, dc, outBit, inBit) in new[] { (-1, 0, 1, 4), (0, 1, 2, 8), (1, 0, 4, 1), (0, -1, 8, 2) })
                {
                    int nr = r + dr, nc = c + dc;
                    if ((bits & outBit) == 0 || nr < 0 || nr >= Rows || nc < 0 || nc >= Cols || powered[nr, nc]) continue;
                    if ((Connections(nr, nc) & inBit) == 0) continue;
                    powered[nr, nc] = true;
                    stack.Push((nr, nc));
                }
            }
        }

        protected override void Update(float dt, PuzzleInput input)
        {
            if (input.Left) { CursorCol = Mathf.Max(0, CursorCol - 1); Play("tick"); }
            if (input.Right) { CursorCol = Mathf.Min(Cols - 1, CursorCol + 1); Play("tick"); }
            if (input.Up) { CursorRow = Mathf.Max(0, CursorRow - 1); Play("tick"); }
            if (input.Down) { CursorRow = Mathf.Min(Rows - 1, CursorRow + 1); Play("tick"); }
            if (input.Confirm) RotateTile(CursorRow, CursorCol);
        }

        private Rect Cell(Rect area, int r, int c)
        {
            float size = Mathf.Min((area.width - 120f) / Cols, (area.height - 20f) / Rows);
            float x0 = area.center.x - size * Cols * 0.5f, y0 = area.center.y - size * Rows * 0.5f;
            return new Rect(x0 + c * size, y0 + r * size, size, size);
        }

        public override void Draw(Painter2D p, Rect area)
        {
            for (int r = 0; r < Rows; r++)
            for (int c = 0; c < Cols; c++)
            {
                var cell = Cell(area, r, c);
                bool cursor = r == CursorRow && c == CursorCol;
                Box(p, new Rect(cell.x + 3, cell.y + 3, cell.width - 6, cell.height - 6), Panel, cursor ? Color.white : Dim, cursor ? 3f : 1.5f);
                int bits = Connections(r, c);
                var centre = cell.center;
                p.strokeColor = powered[r, c] ? Amber : new Color(0.5f, 0.56f, 0.62f);
                p.lineWidth = cell.width * 0.16f;
                foreach (var (bit, dir) in new[] { (1, new Vector2(0f, -1f)), (2, new Vector2(1f, 0f)), (4, new Vector2(0f, 1f)), (8, new Vector2(-1f, 0f)) })
                {
                    if ((bits & bit) == 0) continue;
                    p.BeginPath(); p.MoveTo(centre); p.LineTo(centre + dir * cell.width * 0.5f); p.Stroke();
                }
                p.fillColor = powered[r, c] ? Amber : Dim;
                p.BeginPath(); p.Arc(centre, cell.width * 0.12f, 0f, 360f); p.Fill();
            }
            var src = Cell(area, SourceRow, 0);
            Box(p, new Rect(src.x - 46f, src.center.y - 16f, 40f, 32f), Amber);
            var dst = Cell(area, OutputRow, Cols - 1);
            Box(p, new Rect(dst.xMax + 6f, dst.center.y - 16f, 40f, 32f), Status == State.Solved ? Green : Red);
        }

        public override void Click(Vector2 local, Rect area)
        {
            for (int r = 0; r < Rows; r++)
            for (int c = 0; c < Cols; c++)
                if (Cell(area, r, c).Contains(local)) { RotateTile(r, c); return; }
        }
    }

    // ---------------------------------------------------------------- View

    /// <summary>The puzzle overlay: a framed console with title, instructions, a timer, the drawn puzzle and text rows.</summary>
    public sealed class HarborPuzzleView : VisualElement
    {
        private readonly Label title, instructions, body, timer, footer;
        private readonly VisualElement canvas;
        private readonly List<Label> rowLabels = new();
        public HarborPuzzle Puzzle { get; private set; }
        private float flash;
        private Color flashColour;

        public HarborPuzzleView()
        {
            style.position = Position.Absolute;
            style.left = Length.Percent(50);
            style.top = Length.Percent(50);
            style.translate = new Translate(Length.Percent(-50), Length.Percent(-50));
            style.width = 760;
            style.backgroundColor = new Color(0.01f, 0.02f, 0.035f, 0.92f);
            style.borderTopWidth = 3;
            style.borderTopColor = new Color(0.33f, 0.91f, 1f);
            style.paddingLeft = style.paddingRight = 26;
            style.paddingTop = style.paddingBottom = 18;
            title = L(22, true, new Color(0.9f, 0.96f, 1f));
            instructions = L(14, false, new Color(0.7f, 0.85f, 0.95f));
            timer = L(16, true, new Color(1f, 0.7f, 0.2f));
            canvas = new VisualElement();
            canvas.style.height = 300;
            canvas.style.marginTop = 10;
            canvas.generateVisualContent += ctx =>
            {
                if (Puzzle == null) return;
                var area = new Rect(Vector2.zero, canvas.contentRect.size);
                Puzzle.Draw(ctx.painter2D, area);
                if (flash > 0f)
                {
                    ctx.painter2D.strokeColor = new Color(flashColour.r, flashColour.g, flashColour.b, flash);
                    ctx.painter2D.lineWidth = 6f;
                    ctx.painter2D.BeginPath();
                    ctx.painter2D.MoveTo(Vector2.zero); ctx.painter2D.LineTo(new Vector2(area.width, 0)); ctx.painter2D.LineTo(area.size); ctx.painter2D.LineTo(new Vector2(0, area.height)); ctx.painter2D.ClosePath();
                    ctx.painter2D.Stroke();
                }
            };
            canvas.RegisterCallback<PointerDownEvent>(e =>
            {
                if (Puzzle == null) return;
                var area = new Rect(Vector2.zero, canvas.contentRect.size);
                Puzzle.Click(e.localPosition, area);
                if (Puzzle is LockPuzzle lp) lp.TouchHold(e.localPosition, area, true);
            });
            canvas.RegisterCallback<PointerUpEvent>(e =>
            {
                if (Puzzle is LockPuzzle lp) lp.TouchHold(e.localPosition, new Rect(Vector2.zero, canvas.contentRect.size), false);
            });
            Add(canvas);
            body = L(15, false, new Color(0.85f, 0.92f, 1f));
            body.style.whiteSpace = WhiteSpace.Normal;
            footer = L(12, false, new Color(0.55f, 0.62f, 0.7f));
            footer.text = "SPACE  STEP AWAY (the vehicle stays parked)";
            for (int i = 0; i < 4; i++)
            {
                var row = new Label { pickingMode = PickingMode.Ignore };
                row.style.position = Position.Absolute;
                row.style.fontSize = 15;
                row.style.color = new Color(0.9f, 0.96f, 1f);
                row.style.unityFontStyleAndWeight = FontStyle.Bold;
                row.style.display = DisplayStyle.None;
                canvas.Add(row);
                rowLabels.Add(row);
            }
            style.display = DisplayStyle.None;
        }

        private Label L(int size, bool bold, Color c)
        {
            var l = new Label { pickingMode = PickingMode.Ignore };
            l.style.fontSize = size;
            l.style.color = c;
            l.style.whiteSpace = WhiteSpace.Normal;
            if (bold) l.style.unityFontStyleAndWeight = FontStyle.Bold;
            Add(l);
            return l;
        }

        public void Show(HarborPuzzle puzzle)
        {
            Puzzle = puzzle;
            style.display = puzzle != null ? DisplayStyle.Flex : DisplayStyle.None;
            if (puzzle != null) puzzle.Cue += c => { if (c is "error" or "spark") Flash(new Color(1f, 0.3f, 0.25f)); else if (c is "success" or "confirm") Flash(new Color(0.35f, 1f, 0.55f)); };
        }

        private void Flash(Color c) { flash = 1f; flashColour = c; }

        public void Refresh(float dt)
        {
            if (Puzzle == null) return;
            flash = Mathf.Max(0f, flash - dt * 2.5f);
            title.text = Puzzle.Title;
            instructions.text = Puzzle.Instructions;
            timer.text = Puzzle.TimeLeft >= 0f ? $"TIME  {Puzzle.TimeLeft:0.0}s      MISTAKES  {Puzzle.Mistakes}" : $"MISTAKES  {Puzzle.Mistakes}";
            body.text = Puzzle.Body;
            var area = new Rect(Vector2.zero, canvas.contentRect.size);
            for (int i = 0; i < rowLabels.Count; i++)
            {
                bool show = Puzzle is ManifestPuzzle;
                rowLabels[i].style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
                if (!show) continue;
                var mp = (ManifestPuzzle)Puzzle;
                var r = mp.RowArea(area, i);
                rowLabels[i].style.left = r.x + 16f;
                rowLabels[i].style.top = r.y + 12f;
                rowLabels[i].text = mp.RowText(i);
                rowLabels[i].style.color = mp.Rows[i].Struck ? new Color(1f, 0.4f, 0.35f) : new Color(0.9f, 0.96f, 1f);
            }
            canvas.MarkDirtyRepaint();
        }
    }
}
