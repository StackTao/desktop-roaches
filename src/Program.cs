using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace DesktopRoaches
{
    internal static class Program
    {
        /// <summary>Starts one instance of the portable application and owns its entire lifetime.</summary>
        [STAThread]
        private static void Main()
        {
            bool created;
            using (Mutex mutex = new Mutex(true, "Local\\DesktopRoaches.Portable.v1", out created))
            {
                if (!created)
                {
                    MessageBox.Show("程序已经在运行，请从任务栏打开控制面板。", "桌面蟑螂");
                    return;
                }
                Native.SetProcessDPIAware();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
                try
                {
                    using (ControlPanel panel = new ControlPanel()) Application.Run(panel);
                }
                catch (Exception error)
                {
                    MessageBox.Show("程序已停止，所有蟑螂窗口将关闭。\n\n" + error.Message,
                                    "桌面蟑螂", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                finally { mutex.ReleaseMutex(); }
            }
        }
    }

    /// <summary>Small control panel that owns simulation, sprite windows, hotkeys and cleanup.</summary>
    internal sealed class ControlPanel : Form
    {
        internal readonly Population Population = new Population(new Random());
        internal readonly System.Collections.Generic.List<RoachWindow> Windows =
            new System.Collections.Generic.List<RoachWindow>();
        internal readonly NumericUpDown Batch = new NumericUpDown();
        internal readonly Button SpawnButton = new Button(), PauseButton = new Button(), ClearButton = new Button();
        internal bool Paused;
        internal int FrameCount;
        private readonly Label countLabel = new Label(), hint = new Label(), speedLabel = new Label(), keys = new Label();
        private readonly TrackBar speed = new TrackBar();
        private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        private readonly Stopwatch clock = new Stopwatch();
        private readonly RoachRenderer sprites;
        private readonly float dpiScale;
        private double previous;
        private bool clearKey, quitKey, cleaned;
        internal int ClearKeyError, QuitKeyError;

        /// <summary>Builds the Chinese controls and loads the executable's embedded transparent sprite.</summary>
        internal ControlPanel()
        {
#if NORMAL_HEAD
            Text = "桌面蟑螂 · 正常头小号版";
#else
            Text = "桌面蟑螂 · 融合清晰版";
#endif
            Font = new Font("Microsoft YaHei UI", 10);
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96, 96);
            BackColor = Color.FromArgb(248, 246, 242);
            ForeColor = Color.FromArgb(42, 42, 38);
            ClientSize = new Size(460, 440);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            using (Graphics graphics = CreateGraphics()) dpiScale = graphics.DpiX / 96f;
            sprites = new RoachRenderer();

            Label title = LabelAt("让桌面动起来", 26, 22, 390, 36);
            title.Font = new Font(Font.FontFamily, 19, FontStyle.Bold);
            LabelAt("随机大小 · 巡走短冲 · 触角探路", 28, 65, 400, 26).ForeColor = Color.DimGray;
            LabelAt("每次生成", 28, 116, 100, 28);
            Batch.SetBounds(130, 113, 108, 30);
            Batch.Minimum = 1; Batch.Maximum = DesktopRoaches.Population.Limit; Batch.Value = 5;
            Controls.Add(Batch);
            LabelAt("只 / 点击后追加", 253, 116, 180, 28);
            LabelAt("速度上限", 28, 164, 100, 28);
            speed.SetBounds(120, 158, 218, 44);
            speed.Minimum = 40; speed.Maximum = 400; speed.Value = 180;
            speed.TickFrequency = 60;
            speed.ValueChanged += delegate { speedLabel.Text = speed.Value + " px/s"; };
            Controls.Add(speed);
            speedLabel.SetBounds(340, 164, 100, 28);
            speedLabel.Text = "180 px/s"; Controls.Add(speedLabel);

            SetupButton(SpawnButton, "＋ 生成一批", 28, 216, 402, 44);
            SpawnButton.BackColor = Color.FromArgb(55, 80, 58);
            SpawnButton.ForeColor = Color.White;
            SpawnButton.Click += delegate { Spawn(); };
            SetupButton(PauseButton, "暂停", 28, 272, 126, 36);
            PauseButton.Click += delegate { TogglePause(); };
            SetupButton(ClearButton, "全部清空", 166, 272, 126, 36);
            ClearButton.Click += delegate { ClearAll(); };
            Button exit = new Button();
            SetupButton(exit, "退出", 304, 272, 126, 36);
            exit.Click += delegate { Close(); };
            countLabel.SetBounds(28, 325, 402, 25); Controls.Add(countLabel);
            hint.SetBounds(28, 353, 402, 25); hint.ForeColor = Color.DimGray; Controls.Add(hint);
            keys.SetBounds(28, 386, 410, 44);
            keys.Font = new Font(Font.FontFamily, 8.5f); keys.ForeColor = Color.DimGray;
            Controls.Add(keys);
            UpdateStatus("只在主屏幕活动，关闭面板即可全部退出。");
            timer.Interval = 16;
            timer.Tick += delegate { AdvanceFrame(); };
        }

        /// <summary>Adds a fixed-position text label to this panel.</summary>
        private Label LabelAt(string text, int x, int y, int width, int height)
        {
            Label label = new Label { Text = text, AutoSize = false };
            label.SetBounds(x, y, width, height); Controls.Add(label); return label;
        }

        /// <summary>Applies the panel's shared button appearance and geometry.</summary>
        private void SetupButton(Button button, string text, int x, int y, int width, int height)
        {
            button.Text = text; button.SetBounds(x, y, width, height);
            button.FlatStyle = FlatStyle.Flat; button.FlatAppearance.BorderColor = Color.FromArgb(204, 207, 197);
            button.BackColor = Color.White; button.Cursor = Cursors.Hand;
            Controls.Add(button);
        }

        /// <summary>Registers global escape controls only after the panel has a real native handle.</summary>
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            clearKey = Native.RegisterHotKey(Handle, 1, 0x4003, (uint)Keys.C);
            ClearKeyError = clearKey ? 0 : Marshal.GetLastWin32Error();
            quitKey = Native.RegisterHotKey(Handle, 2, 0x4003, (uint)Keys.Q);
            QuitKeyError = quitKey ? 0 : Marshal.GetLastWin32Error();
            keys.Text = (clearKey ? "Ctrl + Alt + C 清空" : "清空快捷键不可用，请使用面板按钮") + "\n" +
                        (quitKey ? "Ctrl + Alt + Q 退出" : "退出快捷键不可用，请使用面板按钮");
            clock.Start(); previous = clock.Elapsed.TotalSeconds; timer.Start();
        }

        /// <summary>Appends the exact batch atomically and shows all new insects without stealing focus.</summary>
        internal void Spawn()
        {
            int before = Population.Items.Count;
            if (!Population.Add((int)Batch.Value, Screen.PrimaryScreen.WorkingArea, dpiScale))
            {
                UpdateStatus("最多 100 只；本次未添加，请减少每次数量。"); return;
            }
            try
            {
                for (int i = before; i < Population.Items.Count; i++)
                {
                    RoachWindow window = new RoachWindow(Population.Items[i], sprites);
                    Windows.Add(window);
                    window.Present();
                }
            }
            catch
            {
                ClearAll(); throw;
            }
            UpdateStatus(Paused ? "已添加；点击“继续”开始活动。" : "已追加，大小随机；会巡走、短冲和停驻探路。");
        }

        /// <summary>Toggles global pause while preserving every insect's heading and gait.</summary>
        internal void TogglePause()
        {
            Paused = !Paused; PauseButton.Text = Paused ? "继续" : "暂停";
            UpdateStatus(Paused ? "已暂停，点击“继续”恢复爬行。" : "继续活动，停顿中的蟑螂也会自行恢复。");
        }

        /// <summary>Destroys all insect windows immediately and resets the simulation to empty.</summary>
        internal void ClearAll()
        {
            foreach (RoachWindow window in Windows) window.Dispose();
            Windows.Clear(); Population.Items.Clear();
            UpdateStatus("已清空，可以重新生成。");
        }

        /// <summary>Updates the count, paused state and latest action message.</summary>
        private void UpdateStatus(string message)
        {
            countLabel.Text = "当前 " + Population.Items.Count + " / 100 只" + (Paused ? "    · 已暂停" : "    · 运行中");
            hint.Text = message;
        }

        /// <summary>Advances all insects together using elapsed time instead of assuming a fixed frame rate.</summary>
        private void AdvanceFrame()
        {
            FrameCount++;
            double now = clock.Elapsed.TotalSeconds;
            float elapsed = (float)(now - previous); previous = now;
            Rectangle bounds = Screen.PrimaryScreen.WorkingArea;
            Population.Step(elapsed, bounds, speed.Value * dpiScale, Paused);
            foreach (RoachWindow window in Windows) window.Present();
        }

        /// <summary>Handles only this application's registered clear and exit hotkeys.</summary>
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0312)
            {
                if (m.WParam.ToInt32() == 1) ClearAll();
                if (m.WParam.ToInt32() == 2) Close();
                return;
            }
            base.WndProc(ref m);
        }

        /// <summary>Releases timers, hotkeys, native windows and image resources, including on exceptions.</summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing && !cleaned)
            {
                cleaned = true; timer.Stop(); timer.Dispose();
                if (clearKey) Native.UnregisterHotKey(Handle, 1);
                if (quitKey) Native.UnregisterHotKey(Handle, 2);
                ClearAll(); sprites.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    /// <summary>A small per-pixel-alpha overlay with persistent GDI resources and no mouse interaction.</summary>
    internal sealed class RoachWindow : Form
    {
        internal readonly Roach Roach;
        private readonly RoachRenderer sprites;
        private IntPtr dc, dib, oldBitmap;
        private Bitmap surface;
        private Graphics graphics;
        private bool released;
        private float lastHeading = float.NaN;
        private float lastGait = float.NaN, lastAntennaTime = float.NaN;
        private Native.Point lastLocation;
        private bool uploaded;

        /// <summary>Allocates one small reusable DIB buffer rather than repainting the entire desktop.</summary>
        /// <param name="roach">Movement model for this window.</param>
        /// <param name="sprites">Shared animation assets owned by the panel.</param>
        internal RoachWindow(Roach roach, RoachRenderer sprites)
        {
            Roach = roach; this.sprites = sprites;
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true;
            StartPosition = FormStartPosition.Manual;
            Size = new Size(roach.Extent, roach.Extent);
            try
            {
                dc = Native.CreateCompatibleDC(IntPtr.Zero);
                if (dc == IntPtr.Zero) throw new Win32Exception();
                Native.BitmapInfo info = new Native.BitmapInfo();
                info.Size = 40; info.Width = roach.Extent; info.Height = -roach.Extent;
                info.Planes = 1; info.BitCount = 32;
                IntPtr pixels;
                dib = Native.CreateDIBSection(dc, ref info, 0, out pixels, IntPtr.Zero, 0);
                if (dib == IntPtr.Zero) throw new Win32Exception();
                oldBitmap = Native.SelectObject(dc, dib);
                surface = new Bitmap(roach.Extent, roach.Extent, roach.Extent * 4, PixelFormat.Format32bppPArgb, pixels);
                graphics = Graphics.FromImage(surface);
                // Bilinear rotation avoids the high downsampling cost of bicubic on each tiny moving body.
                graphics.InterpolationMode = InterpolationMode.Bilinear;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            }
            catch { Dispose(); throw; }
        }

        /// <summary>Prevents activation even when the window is initially shown.</summary>
        protected override bool ShowWithoutActivation { get { return true; } }

        /// <summary>Combines layered alpha, click-through, tool-window and no-activation flags.</summary>
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams p = base.CreateParams;
                p.ExStyle |= 0x00080000 | 0x00000020 | 0x00000080 | 0x08000000;
                return p;
            }
        }

        /// <summary>Renders the articulated insect and places the complete window inside current screen bounds.</summary>
        internal void Present()
        {
            Rectangle bounds = Screen.PrimaryScreen.WorkingArea;
            if (bounds.Width < Roach.Extent || bounds.Height < Roach.Extent) { Hide(); return; }
            Roach.Constrain(bounds);
            Native.Point location = new Native.Point((int)Math.Round(Roach.X - Roach.Extent / 2f),
                                                    (int)Math.Round(Roach.Y - Roach.Extent / 2f));
            
            bool changed = lastGait != Roach.Gait || lastAntennaTime != Roach.AntennaTime || lastHeading != Roach.Heading;
            if (changed)
            {
                graphics.ResetTransform();
                graphics.Clear(Color.Transparent);
                // Preserve the fractional body position when the native window is placed on integer pixels.
                graphics.TranslateTransform(Roach.X - location.X, Roach.Y - location.Y);
                // Artwork points up; the movement model's zero-angle points right.
                graphics.RotateTransform(Roach.Heading * 180f / (float)Math.PI + 90);
                sprites.Draw(graphics, Roach);
                graphics.Flush(); lastGait = Roach.Gait; lastAntennaTime = Roach.AntennaTime; lastHeading = Roach.Heading;
            }
            // A global pause freezes both animation clocks and requires no repeated native image upload.
            if (uploaded && !changed && lastLocation.X == location.X && lastLocation.Y == location.Y && Visible) return;
            Native.Point source = new Native.Point(0, 0);
            Native.Size size = new Native.Size(Roach.Extent, Roach.Extent);
            Native.Blend blend = new Native.Blend { SourceConstantAlpha = 255, AlphaFormat = 1 };
            if (!Native.UpdateLayeredWindow(Handle, IntPtr.Zero, ref location, ref size, dc,
                                           ref source, 0, ref blend, 2)) throw new Win32Exception();
            uploaded = true; lastLocation = location;
            if (!Visible) Show();
        }

        /// <summary>Saves this application's rendered sprite buffer for offline visual verification.</summary>
        /// <param name="path">PNG output file.</param>
        internal void SavePreview(string path) { surface.Save(path, ImageFormat.Png); }

        /// <summary>Forwards hit testing and rejects mouse activation as an extra focus safeguard.</summary>
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0084) { m.Result = new IntPtr(-1); return; }
            if (m.Msg == 0x0021) { m.Result = new IntPtr(3); return; }
            base.WndProc(ref m);
        }

        /// <summary>Releases managed wrappers before restoring and deleting their underlying native bitmap.</summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing && !released)
            {
                released = true;
                if (graphics != null) graphics.Dispose();
                if (surface != null) surface.Dispose();
                if (dc != IntPtr.Zero && oldBitmap != IntPtr.Zero) Native.SelectObject(dc, oldBitmap);
                if (dib != IntPtr.Zero) Native.DeleteObject(dib);
                if (dc != IntPtr.Zero) Native.DeleteDC(dc);
            }
            base.Dispose(disposing);
        }
    }

    /// <summary>Minimal Win32 declarations used for transparent windows, emergency hotkeys and DPI.</summary>
    internal static class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        internal struct Point
        {
            internal int X, Y;
            internal Point(int x, int y) { X = x; Y = y; }
        }
        [StructLayout(LayoutKind.Sequential)]
        internal struct Size
        {
            internal int Width, Height;
            internal Size(int width, int height) { Width = width; Height = height; }
        }
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        internal struct Blend { internal byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }
        [StructLayout(LayoutKind.Sequential)]
        internal struct BitmapInfo
        {
            internal uint Size;
            internal int Width, Height;
            internal ushort Planes, BitCount;
            internal uint Compression, SizeImage;
            internal int XPelsPerMeter, YPelsPerMeter;
            internal uint ClrUsed, ClrImportant;
            internal uint Color;
        }
        [DllImport("user32.dll")] internal static extern bool SetProcessDPIAware();
        [DllImport("user32.dll", SetLastError = true)] internal static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
        [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(IntPtr hwnd, int id);
        [DllImport("gdi32.dll", SetLastError = true)] internal static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll", SetLastError = true)] internal static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfo info, uint usage, out IntPtr bits, IntPtr section, uint offset);
        [DllImport("gdi32.dll")] internal static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
        [DllImport("gdi32.dll")] internal static extern bool DeleteObject(IntPtr obj);
        [DllImport("gdi32.dll")] internal static extern bool DeleteDC(IntPtr dc);
        [DllImport("user32.dll", SetLastError = true)] internal static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr destinationDC, ref Point location, ref Size size, IntPtr sourceDC, ref Point source, uint colorKey, ref Blend blend, uint flags);
    }
}
