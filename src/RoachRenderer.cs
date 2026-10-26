using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace DesktopRoaches
{
    /// <summary>Combines a registered photographic body with continuously articulated legs and antennae.</summary>
    internal sealed class RoachRenderer : IDisposable
    {
        private readonly Bitmap body, femurTexture, tibiaTexture;
        private readonly SolidBrush footColor = new SolidBrush(Color.FromArgb(80, 43, 24));
        private readonly Pen fine = new Pen(Color.FromArgb(65, 33, 16), 0.38f);
        private readonly Pen feeler = new Pen(Color.FromArgb(81, 46, 28), 0.8f);
        private readonly GraphicsPath feet = new GraphicsPath(), claws = new GraphicsPath();

        /// <summary>Loads matching photographic body and limb materials once for all insects.</summary>
        internal RoachRenderer()
        {
#if NORMAL_HEAD
            body = LoadTexture("roach-body.png", -1, new Size(140, 370));
#else
            body = LoadTexture("roach-body.png", -1, new Size(180, 470));
#endif
            femurTexture = LoadTexture("roach-limbs.png", 0, new Size(40, 192));
            tibiaTexture = LoadTexture("roach-limbs.png", 1, new Size(48, 256));
            fine.StartCap = fine.EndCap = LineCap.Round;
        }

        /// <summary>Registers an alpha-bounded texture, optionally selecting a limb-atlas column.</summary>
        /// <param name="resource">Embedded PNG resource name.</param>
        /// <param name="column">Left/right atlas column, or -1 for the whole image.</param>
        /// <param name="size">Cached raster resolution for desktop rendering.</param>
        private static Bitmap LoadTexture(string resource, int column, Size size)
        {
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource))
            using (Bitmap original = new Bitmap(stream))
            {
                Rectangle tile = column < 0 ? new Rectangle(Point.Empty, original.Size)
                    : new Rectangle(column * original.Width / 2, 0, original.Width / 2, original.Height);
                using (Bitmap rgba = original.Clone(tile, PixelFormat.Format32bppArgb))
                {
                    Rectangle crop = FindBodyBounds(rgba);
                    Bitmap texture = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppPArgb);
                    using (Graphics g = Graphics.FromImage(texture))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.DrawImage(rgba, new Rectangle(Point.Empty, size), crop, GraphicsUnit.Pixel);
                    }
                    return texture;
                }
            }
        }

        /// <summary>Finds the nontransparent body so generated canvas margins cannot move the animation pivot.</summary>
        /// <param name="image">32-bit RGBA source image.</param>
        private static Rectangle FindBodyBounds(Bitmap image)
        {
            BitmapData data = image.LockBits(new Rectangle(Point.Empty, image.Size), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            int left = image.Width, top = image.Height, right = 0, bottom = 0;
            try
            {
                byte[] row = new byte[image.Width * 4];
                for (int y = 0; y < image.Height; y++)
                {
                    Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), row, 0, row.Length);
                    for (int x = 0; x < image.Width; x++)
                    {
                        if (row[x * 4 + 3] < 24) continue;
                        left = Math.Min(left, x); right = Math.Max(right, x);
                        top = Math.Min(top, y); bottom = Math.Max(bottom, y);
                    }
                }
            }
            finally { image.UnlockBits(data); }
            return Rectangle.FromLTRB(left, top, right + 1, bottom + 1);
        }

        /// <summary>Draws all appendages in body coordinates, then overlays the fixed photographic body.</summary>
        /// <param name="g">Graphics already translated to the body pivot and rotated to its heading.</param>
        /// <param name="roach">Continuous gait and independent antenna time.</param>
        internal void Draw(Graphics g, Roach roach)
        {
            GraphicsState state = g.Save();
            g.ScaleTransform(roach.Size / 100f, roach.Size / 100f);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            feet.Reset(); claws.Reset(); feet.FillMode = FillMode.Winding;
            for (int side = -1; side <= 1; side += 2)
            {
                for (int leg = 0; leg < 3; leg++) DrawLeg(g, side, leg, roach);
            }
            g.FillPath(footColor, feet); g.DrawPath(fine, claws);
            for (int side = -1; side <= 1; side += 2)
                DrawAntenna(g, side, roach.AntennaTime * (side < 0 ? 2.6f : 2.1f) + roach.AnimationOffset + side,
                    roach.RestBlend, side < 0 ? roach.LeftProbe : roach.RightProbe);
            // The body is never replaced, stretched per frame, or moved by the walking cycle.
#if NORMAL_HEAD
            // Natural head and elongated abdomen retain the existing calibrated thoracic attachment.
            g.DrawImage(body, -14, -27, 28, 74);
#else
            // Face, jaw transition, pronotum and abdomen share one registered texture and lighting.
            g.DrawImage(body, -18, -44, 36, 94);
#endif
            g.Restore(state);
        }

        /// <summary>Draws photograph-calibrated projected joints with spines and a separate five-segment tarsus.</summary>
        /// <param name="g">Body-coordinate graphics.</param>
        /// <param name="side">Left (-1) or right (+1).</param>
        /// <param name="index">Front, middle or hind leg.</param>
        /// <param name="roach">Ground contacts and current body transform.</param>
        private void DrawLeg(Graphics g, int side, int index, Roach roach)
        {
            Foot foot = roach.Feet[(side < 0 ? 0 : 3) + index];
            PointF tip = roach.ToBody(foot.Position);
            float toeAngle = foot.Angle - roach.Heading - (float)Math.PI / 2;
            float ux = (float)Math.Cos(toeAngle), uy = (float)Math.Sin(toeAngle);
            float tarsus = Roach.TarsusLength(index);
            PointF ankle = new PointF(tip.X - ux * tarsus, tip.Y - uy * tarsus);
            PointF hip = Roach.ReferenceJoint(index, 0), knee = Roach.ReferenceJoint(index, 1);
            PointF referenceAnkle = Roach.ReferenceJoint(index, 2);
            ankle.X *= side;
            // Preserve the photographed projected knee positions. A planar fixed-length solver made
            // the knees stick out like elbows; real legs also flex out of the image plane.
            knee.X += (ankle.X - referenceAnkle.X) * 0.22f;
            knee.Y += (ankle.Y - referenceAnkle.Y) * 0.18f;
            hip.X *= side; knee.X *= side; ankle.X *= side;
            DrawLegSegment(g, femurTexture, hip, knee, index == 0 ? 2.0f : 2.9f);
            // Tibia texture includes natural spines; no second set of vector spikes is overlaid.
            DrawLegSegment(g, tibiaTexture, knee, ankle, index == 0 ? 3.4f : 4.5f);

            PointF toe = ankle;
            // Five short tarsal segments narrow toward a small paired claw, rather than one long tip.
            for (int joint = 0; joint < 5; joint++)
            {
                float length = tarsus * (1.7f - joint * 0.18f) / 6.7f;
                PointF next = new PointF(toe.X + ux * length,
                                        toe.Y + uy * length);
                float width = (0.75f - joint * 0.11f) / 2;
                float nx = -(next.Y - toe.Y) / length * width, ny = (next.X - toe.X) / length * width;
                feet.AddPolygon(new[] { new PointF(toe.X + nx, toe.Y + ny), new PointF(next.X + nx * 0.8f, next.Y + ny * 0.8f),
                    new PointF(next.X - nx * 0.8f, next.Y - ny * 0.8f), new PointF(toe.X - nx, toe.Y - ny) });
                toe = next;
            }
            claws.StartFigure(); claws.AddLine(toe, new PointF(toe.X + ux * 0.9f - uy * 0.5f, toe.Y + uy * 0.9f + ux * 0.5f));
            claws.StartFigure(); claws.AddLine(toe, new PointF(toe.X + ux * 0.9f + uy * 0.5f, toe.Y + uy * 0.9f - ux * 0.5f));
        }

        /// <summary>Maps one photographic limb segment between its moving joints, preserving texture and shading.</summary>
        /// <param name="g">Body-coordinate graphics.</param>
        /// <param name="texture">Registered proximal-to-distal leg texture.</param>
        /// <param name="start">Proximal joint.</param>
        /// <param name="end">Distal joint.</param>
        /// <param name="width">Total projected width including any spines.</param>
        private static void DrawLegSegment(Graphics g, Bitmap texture, PointF start, PointF end, float width)
        {
            float dx = end.X - start.X, dy = end.Y - start.Y;
            float length = (float)Math.Sqrt(dx * dx + dy * dy);
            float nx = -dy / length * width / 2, ny = dx / length * width / 2;
            // Three corners map the vertical atlas segment to the articulated skeleton without rotating the body.
            PointF[] corners = { new PointF(start.X - nx, start.Y - ny),
                new PointF(start.X + nx, start.Y + ny), new PointF(end.X - nx, end.Y - ny) };
            g.DrawImage(texture, corners, new Rectangle(0, 0, texture.Width, texture.Height), GraphicsUnit.Pixel);
        }

        /// <summary>Draws a tapering curved feeler driven by an independent clock, including while resting.</summary>
        /// <param name="g">Body-coordinate graphics.</param>
        /// <param name="side">Left (-1) or right (+1).</param>
        /// <param name="phase">Independent periodic feeler phase.</param>
        /// <param name="rest">Smoothed resting amount, used for broader exploratory sweeps.</param>
        /// <param name="edge">Smoothed proximity of this feeler to the screen edge.</param>
        private void DrawAntenna(Graphics g, int side, float phase, float rest, float edge)
        {
            float sweep = (float)(Math.Sin(phase) * 8 + Math.Sin(phase * 2 + 0.5) * 3) * (0.8f + rest * 0.4f);
#if NORMAL_HEAD
            PointF start = new PointF(side * 3.4f, -25);
            PointF control1 = new PointF(side * 8, -38);
#else
            PointF start = new PointF(side * 9, -34);
            PointF control1 = new PointF(side * 12, -47);
#endif
            PointF control2 = new PointF(side * (18 + sweep * 0.5f + edge * 5), -52 + edge * 8);
            PointF end = new PointF(side * (27 + sweep), -64 + (float)Math.Cos(phase * 2 - 0.7) * 3 + edge * 18);
            // Keep the full curve inside the existing all-angle window margin.
            float radius = (float)Math.Sqrt(end.X * end.X + end.Y * end.Y);
            if (radius > 72) { end.X *= 72 / radius; end.Y *= 72 / radius; }
            PointF previous = start;
            for (int i = 1; i <= 12; i++)
            {
                float t = i / 12f, u = 1 - t;
                PointF next = new PointF(u * u * u * start.X + 3 * u * u * t * control1.X + 3 * u * t * t * control2.X + t * t * t * end.X,
                                        u * u * u * start.Y + 3 * u * u * t * control1.Y + 3 * u * t * t * control2.Y + t * t * t * end.Y);
                feeler.Width = 0.95f - t * 0.65f;
                g.DrawLine(feeler, previous, next);
                previous = next;
            }
        }

        /// <summary>Releases the body texture and shared drawing resources.</summary>
        public void Dispose()
        {
            body.Dispose(); femurTexture.Dispose(); tibiaTexture.Dispose(); footColor.Dispose(); fine.Dispose(); feeler.Dispose();
            feet.Dispose(); claws.Dispose();
        }
    }
}
