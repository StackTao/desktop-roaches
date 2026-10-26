using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using DesktopRoaches;

internal static class AnimationTests
{
    /// <summary>Renders the production renderer at a fixed pivot for pixel-level registration checks.</summary>
    private static Bitmap Render(RoachRenderer renderer, Roach roach)
    {
        Bitmap image = new Bitmap(200, 200, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(image))
        {
            g.TranslateTransform(100, 100);
            renderer.Draw(g, roach);
        }
        return image;
    }

    /// <summary>Counts changed pixels in a selected anatomy region.</summary>
    private static int Difference(Bitmap first, Bitmap second, Rectangle region)
    {
        int count = 0;
        for (int y = region.Top; y < region.Bottom; y++)
            for (int x = region.Left; x < region.Right; x++)
                if (first.GetPixel(x, y) != second.GetPixel(x, y)) count++;
        return count;
    }

    /// <summary>Fails when an anatomy animation or fixed-body registration requirement is violated.</summary>
    private static void Check(bool condition, string message)
    {
        if (!condition) { Console.WriteLine("FAIL: " + message); throw new Exception(message); }
        Console.WriteLine("PASS: " + message);
    }

    /// <summary>Tests actual rendered anatomy and produces an eight-second preview from the production code.</summary>
    private static void Main(string[] args)
    {
        string directory = args[0]; Directory.CreateDirectory(directory);
        using (RoachRenderer renderer = new RoachRenderer())
        {
#if NORMAL_HEAD
            Roach roach = new Roach(new Random(5), new Rectangle(0, 0, 800, 600), 1.1f);
#else
            Roach roach = new Roach(new Random(5), new Rectangle(0, 0, 800, 600), 0.65f);
#endif
            roach.Heading = roach.TargetHeading = -(float)Math.PI / 2;
            roach.Speed = roach.TargetSpeed = 100; roach.DecisionTime = 999; roach.ResetFeet();
            roach.AntennaTime = 0;
            using (Bitmap first = Render(renderer, roach))
            {
                for (int step = 0; step < 20; step++) roach.Step(1f / 60, new Rectangle(0, 0, 800, 600), 180, new Random(1));
                roach.AntennaTime = 0;
                using (Bitmap walking = Render(renderer, roach))
                {
                    // Sample opaque abdomen pixels; the transparent tail margin can reveal a moving leg beneath.
                    Check(Difference(first, walking, new Rectangle(97, 96, 6, 15)) == 0, "Opaque abdomen stays pixel-registered across gait phases");
                    Check(Difference(first, walking, new Rectangle(50, 65, 35, 75)) > 80, "Left legs visibly articulate");
                    Check(Difference(first, walking, new Rectangle(115, 65, 35, 75)) > 80, "Right legs visibly articulate");
                    Check(Difference(first, walking, new Rectangle(55, 25, 90, 35)) == 0, "Antennae use their own clock");
                }
                using (Bitmap planted = Render(renderer, roach))
                {
                roach.AntennaTime = 0.75f;
                using (Bitmap resting = Render(renderer, roach))
                {
                    Check(Difference(first, resting, new Rectangle(55, 25, 90, 35)) > 25, "Antennae keep moving at zero walking speed");
                    Check(Difference(planted, resting, new Rectangle(50, 80, 35, 60)) == 0, "Resting feet remain planted");
                    resting.Save(Path.Combine(directory, "resting.png"), ImageFormat.Png);
                }
                }
            }

            Rectangle arena = new Rectangle(370, 65, 700, 475);
            Random random = new Random(19);
#if NORMAL_HEAD
            Roach detail = new Roach(new Random(10), new Rectangle(0, 0, 1000, 1000), 3f);
#else
            Roach detail = new Roach(new Random(10), new Rectangle(0, 0, 1000, 1000), 1.7f);
#endif
            Roach[] runners = new Roach[3];
            int[] exampleSeeds = { 14, 1, 12 };
            for (int i = 0; i < runners.Length; i++)
            {
                runners[i] = new Roach(new Random(exampleSeeds[i]), arena, 1.0f);
                runners[i].X = arena.Left + (i == 0 ? 140 : i == 1 ? 480 : 310);
                runners[i].Y = arena.Top + (i == 0 ? 120 : i == 1 ? 145 : 330);
                runners[i].ResetFeet();
            }
            detail.Heading = detail.TargetHeading = -(float)Math.PI / 2;
            detail.DecisionTime = 999; detail.ResetFeet();
            using (Font title = new Font("Microsoft YaHei UI", 17, FontStyle.Bold))
            using (Font caption = new Font("Microsoft YaHei UI", 10))
            using (Pen border = new Pen(Color.FromArgb(207, 208, 200)))
            {
                for (int frame = 0; frame < 240; frame++)
                {
                    bool resting = frame >= 90 && frame < 150;
                    detail.TargetSpeed = resting ? 0 : frame < 90 ? 75 : 270;
                    detail.Step(1f / 30, new Rectangle(-10000, -10000, 20000, 20000), 300, random);
                    foreach (Roach runner in runners) runner.Step(1f / 30, arena, 140, random);
                    using (Bitmap image = new Bitmap(1100, 570, PixelFormat.Format32bppArgb))
                    using (Graphics g = Graphics.FromImage(image))
                    {
                        g.Clear(Color.FromArgb(246, 245, 240));
                        g.InterpolationMode = InterpolationMode.Bilinear;
#if NORMAL_HEAD
                        g.DrawString("正常头小号版", title, Brushes.Black, 24, 16);
#else
                        g.DrawString("融合清晰版", title, Brushes.Black, 24, 16);
#endif
                        g.DrawString(resting ? "静止时，触角继续探动" : "放大观察：落脚支撑 / 抬脚回收", caption, Brushes.DimGray, 24, 55);
                        g.DrawString("桌面实际尺寸 / 随机大小 / 巡走与短冲", caption, Brushes.DimGray, 390, 27);
                        g.DrawRectangle(border, arena);
                        GraphicsState state = g.Save();
                        g.TranslateTransform(175, 310); renderer.Draw(g, detail); g.Restore(state);
                        foreach (Roach runner in runners)
                        {
                            state = g.Save();
                            g.TranslateTransform(runner.X, runner.Y);
                            g.RotateTransform(runner.Heading * 180f / (float)Math.PI + 90);
                            renderer.Draw(g, runner); g.Restore(state);
                        }
                        image.Save(Path.Combine(directory, "frame-" + frame.ToString("D3") + ".png"), ImageFormat.Png);
                    }
                }
            }
            Console.WriteLine("PASS: 240 production-renderer preview frames written.");
        }
    }
}
