using System;
using System.Drawing;
using DesktopRoaches;

internal static class MotionTests
{
    private static int assertions;

    /// <summary>Checks a simulation property and stops immediately on a failure.</summary>
    private static void Check(bool condition, string message)
    {
        assertions++;
        if (!condition) throw new Exception(message);
    }

    /// <summary>Runs deterministic population, pause, stop/resume and screen-boundary regressions.</summary>
    private static void Main()
    {
        Rectangle bounds = new Rectangle(-1600, 40, 1600, 860);
        Population population = new Population(new Random(731));
        Check(population.Add(5, bounds, 1), "First batch rejected");
        Check(population.Add(5, bounds, 1), "Second batch rejected");
        Check(population.Items.Count == 10, "Clicks must append, not replace");
        Check(!population.Add(91, bounds, 1), "Exceeded population limit");
        Check(population.Items.Count == 10, "Rejected batch was partially added");
        Check(!population.Add(0, bounds, 1), "Accepted zero batch");
        Check(population.Add(90, bounds, 1), "Could not fill limit");
        Check(!population.Add(1, bounds, 1), "Accepted insect beyond limit");
        float smallest = float.MaxValue, largest = 0;
        foreach (Roach r in population.Items)
        {
#if NORMAL_HEAD
            Check(r.Size >= 58 && r.Size <= 95, "Random size outside the smaller normal-head scale");
#else
            Check(r.Size >= 110 && r.Size <= 165, "Random size outside the designed screen scale");
#endif
            smallest = Math.Min(smallest, r.Size); largest = Math.Max(largest, r.Size);
        }
#if NORMAL_HEAD
        Check(largest - smallest > 30, "Smaller individuals should still have varied sizes");
#else
        Check(largest - smallest > 50, "Random individuals should have visibly varied sizes");
#endif
        Roach first = population.Items[0];
        float x = first.X, y = first.Y, gait = first.Gait, antenna = first.AntennaTime;
        population.Step(10, bounds, 180, true);
        Check(first.X == x && first.Y == y && first.Gait == gait && first.AntennaTime == antenna, "Pause changed state");

        bool stopped = false, resumed = false, sprinted = false, explored = false;
        for (int frame = 0; frame < 12000; frame++)
        {
            // Exercise negative virtual-screen coordinates, a taskbar, DPI and runtime resizing.
            if (frame == 3000) bounds = new Rectangle(0, 0, 800, 600);
            if (frame == 6000) bounds = new Rectangle(100, -1080, 1920, 1080);
            float speed = frame < 8000 ? 400 : 45;
            population.Step(frame % 177 == 0 ? 20 : 0.033f, bounds, speed, false);
            foreach (Roach r in population.Items)
            {
                float half = r.Extent / 2f;
                Check(!float.IsNaN(r.X) && !float.IsNaN(r.Y), "Position is NaN");
                Check(r.X - half >= bounds.Left - 0.01 && r.X + half <= bounds.Right + 0.01,
                      "Insect crossed horizontal bounds");
                Check(r.Y - half >= bounds.Top - 0.01 && r.Y + half <= bounds.Bottom + 0.01,
                      "Insect crossed vertical bounds");
                Check(r.Speed >= 0 && r.Speed <= speed, "Speed outside configured range");
                foreach (Foot foot in r.Feet)
                {
                    PointF local = r.ToBody(foot.Position);
                    Check(!float.IsNaN(local.X) && local.X * local.X + local.Y * local.Y < 70 * 70,
                          "Foot crossed the safe rendering radius");
                }
            }
            if (first.Speed == 0) stopped = true;
            else if (stopped) resumed = true;
            if (first.TargetSpeed > speed * 0.8f) sprinted = true;
            if (first.TargetSpeed > speed * 0.2f && first.TargetSpeed < speed * 0.6f) explored = true;
        }
        Check(stopped && resumed, "No explicit stop followed by resume");
        Check(sprinted && explored, "No mix of slow exploration and short fast runs");
        Random random = new Random(5);
        first.DecisionTime = 100;
        first.Speed = 200;
        first.TargetSpeed = 200;
        first.X = bounds.Left + first.Extent / 2f;
        first.Heading = (float)Math.PI;
        first.TargetHeading = first.Heading;
        for (int i = 0; i < 120; i++)
        {
            float previous = first.Heading;
            first.Step(0.033f, bounds, 200, random);
            Check(Math.Abs(Roach.AngleDifference(first.Heading, previous)) <= 3.001f * 0.033f,
                  "Boundary caused an instantaneous heading jump");
        }
        Check(first.X > bounds.Left + first.Extent / 2f + 10, "Insect did not turn and leave the left edge");

        first.X = bounds.Left + bounds.Width / 2f; first.Y = bounds.Top + bounds.Height / 2f;
        first.Speed = first.TargetSpeed = first.AngularSpeed = 0;
        first.Heading = first.TargetHeading = 0; first.DecisionTime = 100;
        gait = first.Gait; antenna = first.AntennaTime;
        first.Step(0.033f, bounds, 200, random);
        Check(first.Gait == gait && first.AntennaTime > antenna, "Resting antennae must move while feet stay planted");
        first.Constrain(new Rectangle(0, 0, 10, 10));
        Check(first.X == 5 && first.Y == 5, "Tiny work area not centered safely");
        population.Items.Clear();
        Check(population.Items.Count == 0, "Clear failed");
        Console.WriteLine("PASS: " + assertions + " assertions; 100 insects x 12,000 frames.");
    }
}
