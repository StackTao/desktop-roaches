using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Diagnostics;
using DesktopRoaches;

internal static class RenderBenchmark
{
    /// <summary>Separates rendering cost from native window updates and compares resampling modes.</summary>
    private static void Main()
    {
        using (RoachRenderer renderer = new RoachRenderer())
        using (Bitmap bitmap = new Bitmap(170, 170))
        using (Graphics g = Graphics.FromImage(bitmap))
        {
            Roach roach = new Roach(new Random(12), new Rectangle(0, 0, 800, 600), 1);
            foreach (InterpolationMode mode in new[] { InterpolationMode.HighQualityBicubic, InterpolationMode.Bilinear, InterpolationMode.NearestNeighbor })
            {
                g.InterpolationMode = mode;
                Stopwatch clock = Stopwatch.StartNew();
                for (int i = 0; i < 100; i++)
                {
                    g.ResetTransform(); g.Clear(Color.Transparent);
                    g.TranslateTransform(85, 85); g.RotateTransform(i * 3.6f);
                    roach.Gait = i * 0.2f; roach.AntennaTime = i * 0.03f;
                    renderer.Draw(g, roach);
                }
                Console.WriteLine(mode + ": " + clock.Elapsed.TotalMilliseconds.ToString("F1") + " ms / 100 insects");
            }
        }
    }
}
