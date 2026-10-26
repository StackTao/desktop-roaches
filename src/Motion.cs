using System;
using System.Collections.Generic;
using System.Drawing;

namespace DesktopRoaches
{
    /// <summary>One foot's ground contact and short airborne recovery, stored in screen coordinates.</summary>
    internal sealed class Foot
    {
        internal PointF Position, Start, Target;
        internal float Angle, StartAngle, TargetAngle, Progress, Duration, Phase;
        internal bool Swing;
    }

    /// <summary>Independent movement and animation state, with no dependency on windows or rendering.</summary>
    internal sealed class Roach
    {
        internal float X, Y, Heading, Speed, DecisionTime, Gait;
        internal float TargetHeading, TargetSpeed, AngularSpeed, AntennaTime;
        internal float RestBlend;
        internal float LeftProbe, RightProbe;
        internal readonly float AnimationOffset;
        internal readonly int Extent;
        internal readonly float Size;
        internal bool Stopped;
        internal readonly Foot[] Feet = new Foot[6];
        private readonly float scale;
        private bool avoidingEdge;
        // Visible joint projections approximated from the supplied photograph; hidden roots placed inside the thorax.
        // Hidden roots remain beneath the thorax; the long distal tarsi are separate from the spiny tibiae.
        private static readonly PointF[] Hips = { new PointF(5, -19), new PointF(5, -12), new PointF(5, -5) };
        private static readonly PointF[] Knees = { new PointF(10.5f, -20.5f), new PointF(14.5f, -9), new PointF(15.5f, 0) };
        private static readonly PointF[] Ankles = { new PointF(16.5f, -20.5f), new PointF(23, 1.5f), new PointF(20, 30) };
        private static readonly PointF[] Toes = { new PointF(23, -23.5f), new PointF(34, 8), new PointF(26, 48) };

        /// <summary>Creates a randomly sized insect inside the supplied screen rectangle.</summary>
        /// <param name="random">Shared random generator.</param>
        /// <param name="bounds">Usable primary-screen rectangle in physical pixels.</param>
        /// <param name="dpiScale">Scale for the primary monitor.</param>
        internal Roach(Random random, Rectangle bounds, float dpiScale)
        {
            scale = dpiScale;
#if NORMAL_HEAD
            Size = (float)(58 + random.NextDouble() * 37) * dpiScale;
#else
            Size = (float)(110 + random.NextDouble() * 55) * dpiScale;
#endif
            // A circumscribed square keeps antennae and feet inside the screen at every angle.
            Extent = (int)Math.Ceiling(Size * 1.55f) + 4;
            X = bounds.Left + Extent / 2f + (float)random.NextDouble() * Math.Max(0, bounds.Width - Extent);
            Y = bounds.Top + Extent / 2f + (float)random.NextDouble() * Math.Max(0, bounds.Height - Extent);
            Heading = (float)(random.NextDouble() * Math.PI * 2);
            TargetHeading = Heading;
            Gait = (float)(random.NextDouble() * Math.PI * 2);
            AnimationOffset = (float)(random.NextDouble() * Math.PI * 2);
            ChooseMotion(random, 180 * dpiScale);
            Constrain(bounds);
            ResetFeet();
        }

        /// <summary>Returns the neutral toe position for a front, middle or hind foot in body units.</summary>
        /// <param name="index">Leg index from zero to five; first three are left legs.</param>
        internal static PointF NeutralFoot(int index)
        {
            PointF point = Toes[index % 3];
            return new PointF((index < 3 ? -1 : 1) * point.X, point.Y);
        }

        /// <summary>Returns a reference joint on the right side in body-axis coordinates.</summary>
        /// <param name="leg">Front, middle or hind leg index.</param>
        /// <param name="joint">Hip (0), knee (1), or ankle (2).</param>
        internal static PointF ReferenceJoint(int leg, int joint)
        {
            return joint == 0 ? Hips[leg] : joint == 1 ? Knees[leg] : Ankles[leg];
        }

        /// <summary>Returns the reference tarsus length, which differs between front, middle and hind feet.</summary>
        /// <param name="leg">Front, middle or hind leg index.</param>
        internal static float TarsusLength(int leg)
        {
            float x = Toes[leg].X - Ankles[leg].X, y = Toes[leg].Y - Ankles[leg].Y;
            return (float)Math.Sqrt(x * x + y * y);
        }

        /// <summary>Returns the reference foot direction in body coordinates, mirrored for the left side.</summary>
        /// <param name="index">Leg index from zero to five.</param>
        private static float FootDirection(int index)
        {
            int leg = index % 3;
            float angle = (float)Math.Atan2(Toes[leg].Y - Ankles[leg].Y, Toes[leg].X - Ankles[leg].X);
            return index < 3 ? (float)Math.PI - angle : angle;
        }

        /// <summary>Converts body units, with the head pointing up, to physical screen coordinates.</summary>
        /// <param name="point">Position relative to the body centre at size 100.</param>
        internal PointF ToWorld(PointF point)
        {
            float a = Heading + (float)Math.PI / 2, c = (float)Math.Cos(a), s = (float)Math.Sin(a);
            return new PointF(X + (point.X * c - point.Y * s) * Size / 100,
                              Y + (point.X * s + point.Y * c) * Size / 100);
        }

        /// <summary>Converts a fixed screen contact into the moving body's coordinate frame.</summary>
        /// <param name="point">Screen position of the foot.</param>
        internal PointF ToBody(PointF point)
        {
            float a = Heading + (float)Math.PI / 2, c = (float)Math.Cos(a), s = (float)Math.Sin(a);
            float x = (point.X - X) * 100 / Size, y = (point.Y - Y) * 100 / Size;
            return new PointF(x * c + y * s, -x * s + y * c);
        }

        /// <summary>Places the six feet after creation or an explicit preview reposition.</summary>
        internal void ResetFeet()
        {
            for (int i = 0; i < Feet.Length; i++)
            {
                Feet[i] = new Foot { Position = ToWorld(NeutralFoot(i)),
                    Angle = FootDirection(i) + Heading + (float)Math.PI / 2, Phase = LegPhase(i) };
            }
        }

        /// <summary>Offsets slow leg recovery within each tripod, becoming more synchronous at speed.</summary>
        /// <param name="index">Leg index from zero to five.</param>
        private float LegPhase(int index)
        {
            int leg = index % 3;
            float slow = 1 - Math.Min(1, Speed / (Size * 1.6f));
            float phase = Gait / ((float)Math.PI * 2) + (leg == 1 ? 0.5f : 0) + (index >= 3 ? 0.5f : 0) - leg * 0.07f * slow;
            return phase - (float)Math.Floor(phase);
        }

        /// <summary>Keeps supporting toes fixed on the ground and recovers airborne toes toward predicted landing positions.</summary>
        /// <param name="dt">Elapsed simulation time in seconds.</param>
        private void StepFeet(float dt)
        {
            float fast = Math.Min(1, Speed / (Size * 1.6f)), duty = 0.72f - fast * 0.16f;
            for (int i = 0; i < Feet.Length; i++)
            {
                Foot foot = Feet[i];
                float phase = LegPhase(i);
                PointF neutral = NeutralFoot(i), local = ToBody(foot.Position);
                float reach = (float)Math.Sqrt((local.X - neutral.X) * (local.X - neutral.X) + (local.Y - neutral.Y) * (local.Y - neutral.Y));
                bool scheduled = foot.Phase < duty && phase >= duty;
                if (!foot.Swing && ((scheduled && (Speed > 0.1f || Math.Abs(AngularSpeed) > 0.02f)) || reach > 12))
                {
                    foot.Swing = true; foot.Progress = 0; foot.Start = foot.Position; foot.StartAngle = foot.Angle;
                    float effectiveSpeed = Math.Max(Speed, Math.Abs(AngularSpeed) * Size * 0.3f);
                    foot.Duration = Math.Max(0.055f, Math.Min(0.18f, Size * 0.24f / Math.Max(1, effectiveSpeed) * (1 - duty)));
                    // Aim ahead of the moving body; support contacts themselves are never moved with it.
                    float lead = Math.Min(7.2f, Speed * foot.Duration * 100 / Size * 0.8f);
                    neutral.Y -= lead;
                    float future = Heading + AngularSpeed * foot.Duration;
                    float a = future + (float)Math.PI / 2, c = (float)Math.Cos(a), s = (float)Math.Sin(a);
                    foot.Target = new PointF(X + (float)Math.Cos(future) * Speed * foot.Duration + (neutral.X * c - neutral.Y * s) * Size / 100,
                                             Y + (float)Math.Sin(future) * Speed * foot.Duration + (neutral.X * s + neutral.Y * c) * Size / 100);
                    foot.TargetAngle = FootDirection(i) + a;
                }
                if (foot.Swing)
                {
                    foot.Progress = Math.Min(1, foot.Progress + dt / foot.Duration);
                    float t = foot.Progress, eased = t * t * (3 - 2 * t);
                    foot.Position = new PointF(foot.Start.X + (foot.Target.X - foot.Start.X) * eased,
                                               foot.Start.Y + (foot.Target.Y - foot.Start.Y) * eased);
                    foot.Angle = foot.StartAngle + AngleDifference(foot.TargetAngle, foot.StartAngle) * eased;
                    if (t >= 1) foot.Swing = false;
                }
                foot.Phase = phase;
            }
        }

        /// <summary>Chooses a temporary stop or a new speed, with occasional direction changes.</summary>
        /// <param name="random">Random generator.</param>
        /// <param name="maximumSpeed">Maximum speed in pixels per second.</param>
        private void ChooseMotion(Random random, float maximumSpeed)
        {
            // Pick destinations, never overwrite the visible heading or velocity at a decision boundary.
            double activity = random.NextDouble();
            bool rest = activity < 0.22, sprint = activity >= 0.22 && activity < 0.44;
            TargetSpeed = rest ? 0 : maximumSpeed * (float)(sprint ? 0.80 + random.NextDouble() * 0.20 : 0.22 + random.NextDouble() * 0.36);
            DecisionTime = rest ? (float)(1.2 + random.NextDouble() * 2.2)
                : sprint ? (float)(0.55 + random.NextDouble() * 0.55) : (float)(1.8 + random.NextDouble() * 2.8);
            if (!rest)
            {
                float amplitude = sprint ? 0.25f : random.NextDouble() < 0.85 ? 0.65f : 2.0f;
                TargetHeading = Heading + (float)(random.NextDouble() - 0.5) * amplitude;
            }
            else TargetHeading = Heading + (float)(random.NextDouble() - 0.5) * 0.35f;
        }

        /// <summary>Steers and accelerates continuously, anticipates boundaries, and advances independent limb clocks.</summary>
        /// <param name="seconds">Elapsed seconds since the previous frame.</param>
        /// <param name="bounds">Current usable screen bounds.</param>
        /// <param name="maximumSpeed">Current maximum speed in pixels per second.</param>
        /// <param name="random">Random generator.</param>
        internal void Step(float seconds, Rectangle bounds, float maximumSpeed, Random random)
        {
            float dt = Math.Max(0, Math.Min(seconds, 0.05f));
            AntennaTime = (AntennaTime + dt) % (float)(Math.PI * 200);
            DecisionTime -= dt;
            if (DecisionTime <= 0 && !avoidingEdge) ChooseMotion(random, maximumSpeed);
            float half = Extent / 2f;
            float dx = (float)Math.Cos(Heading), dy = (float)Math.Sin(Heading);
            // Start turning before contact. Keep a chosen escape heading until the turn is complete.
            float margin = Size * 0.3f + Speed * 0.55f;
            bool left = X - half - bounds.Left < margin && dx < -0.05f;
            bool right = bounds.Right - X - half < margin && dx > 0.05f;
            bool top = Y - half - bounds.Top < margin && dy < -0.05f;
            bool bottom = bounds.Bottom - Y - half < margin && dy > 0.05f;
            if (!avoidingEdge && (left || right || top || bottom))
            {
                float tx = bounds.Left + bounds.Width * (float)(0.35 + random.NextDouble() * 0.3);
                float ty = bounds.Top + bounds.Height * (float)(0.35 + random.NextDouble() * 0.3);
                TargetHeading = (float)Math.Atan2(ty - Y, tx - X);
                avoidingEdge = true;
            }
            float error = AngleDifference(TargetHeading, Heading);
            float desiredAngular = Math.Max(-3, Math.Min(3, error * 5));
            AngularSpeed = Approach(AngularSpeed, desiredAngular, 12 * dt);
            float turn = AngularSpeed * dt;
            if (Math.Abs(turn) >= Math.Abs(error) && turn * error >= 0) { turn = error; AngularSpeed = 0; }
            Heading = AngleDifference(Heading + turn, 0);
            if (avoidingEdge && Math.Abs(error) < 0.12f)
            {
                avoidingEdge = false;
                DecisionTime = Math.Max(DecisionTime, 1.2f);
            }

            // Reduce forward speed during a sharp turn rather than sliding sideways along the wall.
            float turningFactor = Math.Max(0, 1 - Math.Abs(error) / 1.35f);
            float desiredSpeed = Math.Min(TargetSpeed, maximumSpeed) * turningFactor;
            Speed = Approach(Speed, desiredSpeed, 420 * scale * dt);
            Speed = Math.Min(Speed, maximumSpeed);
            if (Speed < 0.05f && desiredSpeed == 0) Speed = 0;
            float oldX = X, oldY = Y;
            X += (float)Math.Cos(Heading) * Speed * dt;
            Y += (float)Math.Sin(Heading) * Speed * dt;
            Constrain(bounds);
            float distance = (float)Math.Sqrt((X - oldX) * (X - oldX) + (Y - oldY) * (Y - oldY));
            // Feet track travelled distance; turning on the spot also requires steps.
            Gait = (Gait + distance / (Size * 0.24f) * (float)(Math.PI * 2) + Math.Abs(turn) * 2.0f)
                   % (float)(Math.PI * 2);
            Stopped = Speed == 0;
            RestBlend = Approach(RestBlend, Speed < 3 ? 1 : 0, 1.6f * dt);
            StepFeet(dt);
            LeftProbe = Approach(LeftProbe, ProbeEdge(-1, bounds), dt * 5);
            RightProbe = Approach(RightProbe, ProbeEdge(1, bounds), dt * 5);
        }

        /// <summary>Measures proximity of a forward feeler to the usable desktop edge.</summary>
        /// <param name="side">Left (-1) or right (+1) feeler.</param>
        /// <param name="bounds">Usable primary-screen bounds.</param>
        private float ProbeEdge(int side, Rectangle bounds)
        {
            PointF tip = ToWorld(new PointF(side * 27, -64));
            float distance = Math.Min(Math.Min(tip.X - bounds.Left, bounds.Right - tip.X),
                                      Math.Min(tip.Y - bounds.Top, bounds.Bottom - tip.Y));
            return Math.Max(0, Math.Min(1, 1 - distance / (Size * 0.65f)));
        }

        /// <summary>Returns the shortest signed angular difference in radians.</summary>
        /// <param name="target">Target angle.</param>
        /// <param name="current">Current angle.</param>
        internal static float AngleDifference(float target, float current)
        {
            return (float)Math.Atan2(Math.Sin(target - current), Math.Cos(target - current));
        }

        /// <summary>Moves a scalar toward its target without overshoot.</summary>
        /// <param name="value">Current scalar value.</param>
        /// <param name="target">Desired scalar value.</param>
        /// <param name="amount">Maximum change during this frame.</param>
        private static float Approach(float value, float target, float amount)
        {
            return value < target ? Math.Min(value + amount, target) : Math.Max(value - amount, target);
        }

        /// <summary>Clamps the full window to new bounds, including after a monitor resolution change.</summary>
        /// <param name="bounds">Usable screen rectangle.</param>
        internal void Constrain(Rectangle bounds)
        {
            float oldX = X, oldY = Y;
            float half = Extent / 2f;
            X = bounds.Width < Extent ? bounds.Left + bounds.Width / 2f
                : Math.Max(bounds.Left + half, Math.Min(bounds.Right - half, X));
            Y = bounds.Height < Extent ? bounds.Top + bounds.Height / 2f
                : Math.Max(bounds.Top + half, Math.Min(bounds.Bottom - half, Y));
            // A screen resize can relocate the entire insect; preserve its pose across that relocation.
            if (X != oldX || Y != oldY)
                foreach (Foot foot in Feet)
                    if (foot != null)
                    {
                        foot.Position = new PointF(foot.Position.X + X - oldX, foot.Position.Y + Y - oldY);
                        foot.Start = new PointF(foot.Start.X + X - oldX, foot.Start.Y + Y - oldY);
                        foot.Target = new PointF(foot.Target.X + X - oldX, foot.Target.Y + Y - oldY);
                    }
        }
    }

    /// <summary>Owns all insects and enforces an atomic per-click population limit.</summary>
    internal sealed class Population
    {
        internal const int Limit = 100;
        internal readonly List<Roach> Items = new List<Roach>();
        private readonly Random random;

        /// <summary>Creates an empty population with an injectable random generator.</summary>
        /// <param name="random">Generator used for every insect.</param>
        internal Population(Random random) { this.random = random; }

        /// <summary>Appends exactly the requested batch, or rejects the complete batch if it exceeds the limit.</summary>
        /// <param name="count">Number to append, from 1 to 100.</param>
        /// <param name="bounds">Current screen bounds.</param>
        /// <param name="dpiScale">Current display scale.</param>
        /// <returns>Whether the entire batch was added.</returns>
        internal bool Add(int count, Rectangle bounds, float dpiScale)
        {
            if (count < 1 || count > Limit - Items.Count) return false;
            for (int i = 0; i < count; i++) Items.Add(new Roach(random, bounds, dpiScale));
            return true;
        }

        /// <summary>Moves every insect once; leaves positions and animation unchanged while paused.</summary>
        /// <param name="seconds">Elapsed frame time.</param>
        /// <param name="bounds">Current screen bounds.</param>
        /// <param name="maximumSpeed">Speed cap in physical pixels per second.</param>
        /// <param name="paused">Whether movement is paused.</param>
        internal void Step(float seconds, Rectangle bounds, float maximumSpeed, bool paused)
        {
            foreach (Roach roach in Items)
            {
                if (paused) roach.Constrain(bounds);
                else roach.Step(seconds, bounds, maximumSpeed, random);
            }
        }
    }
}
