using System;
using System.Drawing;
using DesktopRoaches;

internal static class DynamicsTests
{
    /// <summary>Measures frame-to-frame movement discontinuities over a deterministic open-field run.</summary>
    private static int Main()
    {
        Random random = new Random(46);
        Rectangle bounds = new Rectangle(-50000, -50000, 100000, 100000);
        Roach roach = new Roach(random, bounds, 1);
        float maxAngle = 0, maxSpeedJump = 0;
        float maxFootSlip = 0;
        int supportSamples = 0, airborneSamples = 0;
        const float dt = 1f / 60;
        for (int i = 0; i < 12000; i++)
        {
            float heading = roach.Heading, speed = roach.Speed;
            PointF[] contacts = new PointF[6];
            bool[] airborne = new bool[6];
            for (int leg = 0; leg < 6; leg++)
            {
                contacts[leg] = roach.Feet[leg].Position;
                airborne[leg] = roach.Feet[leg].Swing;
            }
            roach.Step(dt, bounds, 180, random);
            for (int leg = 0; leg < 6; leg++)
            {
                Foot foot = roach.Feet[leg];
                if (!airborne[leg] && !foot.Swing)
                {
                    float dx = contacts[leg].X - foot.Position.X, dy = contacts[leg].Y - foot.Position.Y;
                    maxFootSlip = Math.Max(maxFootSlip, (float)Math.Sqrt(dx * dx + dy * dy));
                    supportSamples++;
                }
                if (foot.Swing) airborneSamples++;
                PointF local = roach.ToBody(foot.Position);
                if (float.IsNaN(local.X) || local.X * local.X + local.Y * local.Y > 70 * 70)
                    throw new Exception("A foot escaped the insect's safe rendering radius.");
            }
            float difference = (float)Math.Atan2(Math.Sin(roach.Heading - heading), Math.Cos(roach.Heading - heading));
            maxAngle = Math.Max(maxAngle, Math.Abs(difference));
            maxSpeedJump = Math.Max(maxSpeedJump, Math.Abs(roach.Speed - speed));
        }
        Console.WriteLine("Maximum per-frame turn: " + (maxAngle * 180 / Math.PI).ToString("F2") + " degrees");
        Console.WriteLine("Maximum per-frame speed change: " + maxSpeedJump.ToString("F2") + " px/s");
        if (maxAngle > 3.01f * dt || maxSpeedJump > 450.01f * dt)
        {
            Console.WriteLine("FAIL: Heading or speed jumps instead of changing continuously.");
            return 1;
        }
        Console.WriteLine("PASS: Bounded angular speed and acceleration.");
        Console.WriteLine("Ground contacts: " + supportSamples + "; airborne samples: " + airborneSamples + "; maximum planted-toe slip: " + maxFootSlip.ToString("F5") + " px");
        if (supportSamples < 1000 || airborneSamples < 1000 || maxFootSlip > 0.001f)
            throw new Exception("Ground contacts must remain fixed while the body translates and turns.");
        Console.WriteLine("PASS: Planted toes stay fixed in screen coordinates through walking and turning.");
        return 0;
    }
}
