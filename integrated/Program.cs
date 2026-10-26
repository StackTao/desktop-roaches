using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace IntegratedRoaches
{
    /// <summary>Runs one control panel for all three embedded versions without launching child processes.</summary>
    internal static class Program
    {
        /// <summary>Starts the single-instance application and reports startup errors.</summary>
        [STAThread]
        private static void Main()
        {
            bool created;
            using (Mutex mutex = new Mutex(true, "Local\\DesktopRoaches.Portable.v1", out created))
            {
                if (!created) { MessageBox.Show("请先退出正在运行的蟑螂程序，再打开整合版。", "桌面蟑螂"); return; }
                Native.SetProcessDPIAware();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                try { using (ControlPanel panel = new ControlPanel()) Application.Run(panel); }
                catch (Exception error) { MessageBox.Show(error.GetBaseException().Message, "桌面蟑螂启动失败"); }
                finally { mutex.ReleaseMutex(); }
            }
        }
    }

    /// <summary>Adapts the verified population, renderer and overlay classes embedded in an original EXE.</summary>
    internal sealed class Variant : IDisposable
    {
        internal readonly string Name;
        internal readonly List<Form> Windows = new List<Form>();
        internal readonly IList Items;
        private readonly object population;
        private readonly IDisposable renderer;
        private readonly MethodInfo add, step, present;
        private readonly ConstructorInfo windowConstructor;
        private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        /// <summary>Loads a bundled assembly in memory and binds its existing model and rendering entry points.</summary>
        /// <param name="name">Exact user-provided version suffix used in the UI.</param>
        /// <param name="resource">Embedded original EXE resource name.</param>
        internal Variant(string name, string resource)
        {
            Name = name;
            Assembly assembly;
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource))
            using (MemoryStream bytes = new MemoryStream())
            {
                stream.CopyTo(bytes);
                // Byte loading keeps each version's types and resources together; no EXEs are extracted or started.
                assembly = Assembly.Load(bytes.ToArray());
            }
            Type populationType = assembly.GetType("DesktopRoaches.Population", true);
            Type rendererType = assembly.GetType("DesktopRoaches.RoachRenderer", true);
            Type windowType = assembly.GetType("DesktopRoaches.RoachWindow", true);
            Type roachType = assembly.GetType("DesktopRoaches.Roach", true);
            population = populationType.GetConstructor(Members, null, new[] { typeof(Random) }, null)
                .Invoke(new object[] { new Random(Guid.NewGuid().GetHashCode()) });
            Items = (IList)populationType.GetField("Items", Members).GetValue(population);
            add = populationType.GetMethod("Add", Members);
            step = populationType.GetMethod("Step", Members);
            present = windowType.GetMethod("Present", Members);
            windowConstructor = windowType.GetConstructor(Members, null, new[] { roachType, rendererType }, null);
            renderer = (IDisposable)Activator.CreateInstance(rendererType, true);
        }

        /// <summary>Appends one batch using this version's original size, body, legs and movement model.</summary>
        /// <param name="count">Number of insects to append.</param>
        /// <param name="bounds">Primary screen working area.</param>
        /// <param name="dpiScale">Physical pixel scaling of the control panel's primary monitor.</param>
        internal void Add(int count, Rectangle bounds, float dpiScale)
        {
            int before = Items.Count;
            if (!(bool)add.Invoke(population, new object[] { count, bounds, dpiScale }))
                throw new InvalidOperationException("本次生成超出数量上限。");
            for (int i = before; i < Items.Count; i++)
            {
                Form window = (Form)windowConstructor.Invoke(new[] { Items[i], renderer });
                Windows.Add(window);
                present.Invoke(window, null);
            }
        }

        /// <summary>Advances and displays this version's population using the shared panel timer.</summary>
        /// <param name="seconds">Elapsed seconds since the previous frame.</param>
        /// <param name="bounds">Current primary screen working area.</param>
        /// <param name="maximumSpeed">Speed limit in physical pixels per second.</param>
        /// <param name="paused">Whether all populations are paused.</param>
        internal void Step(float seconds, Rectangle bounds, float maximumSpeed, bool paused)
        {
            if (Items.Count == 0) return;
            step.Invoke(population, new object[] { seconds, bounds, maximumSpeed, paused });
            foreach (Form window in Windows) present.Invoke(window, null);
        }

        /// <summary>Destroys all overlays and empties this population while retaining reusable textures.</summary>
        internal void Clear()
        {
            foreach (Form window in Windows) window.Dispose();
            Windows.Clear(); Items.Clear();
        }

        /// <summary>Releases this version's native windows and cached rendering resources.</summary>
        public void Dispose() { Clear(); renderer.Dispose(); }
    }

    /// <summary>Provides one type selector, one batch count and shared controls for all visible insects.</summary>
    internal sealed class ControlPanel : Form
    {
        internal readonly string[] Names = { "校准版", "质感版", "fxq版" };
        internal readonly Variant[] Variants = new Variant[3];
        internal readonly ComboBox Types = new ComboBox();
        internal readonly NumericUpDown Batch = new NumericUpDown();
        internal readonly Button SpawnButton = new Button(), PauseButton = new Button(), ClearButton = new Button();
        internal bool Paused;
        internal const int Limit = 100;
        private readonly TrackBar speed = new TrackBar();
        private readonly Label status = new Label(), stateBadge = new Label(), hint = new Label(), keys = new Label();
        private readonly Label[] counts = { new Label(), new Label(), new Label() };
        private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        private readonly Stopwatch clock = new Stopwatch();
        private readonly float dpiScale;
        private double previous;
        private bool clearKey, quitKey, cleaned;

        /// <summary>Creates the integrated panel with the exact suffix names from the retained EXEs.</summary>
        internal ControlPanel()
        {
            Text = "桌面蟑螂 · 三合一";
            Font = new Font("Microsoft YaHei UI", 10);
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96, 96);
            ClientSize = new Size(540, 630);
            BackColor = Color.FromArgb(244, 247, 249);
            ForeColor = Color.FromArgb(34, 48, 58);
            DoubleBuffered = true;
            FormBorderStyle = FormBorderStyle.FixedSingle; MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            using (Graphics g = CreateGraphics()) dpiScale = g.DpiX / 96f;
            Label title = LabelAt("桌面蟑螂", 26, 22, 330, 42);
            title.Font = new Font(Font.FontFamily, 22, FontStyle.Bold);
            title.BackColor = Color.Transparent;
            Label subtitle = LabelAt("选择喜欢的版本，让桌面热闹起来。", 28, 70, 430, 25);
            subtitle.BackColor = Color.Transparent; subtitle.ForeColor = Color.FromArgb(105, 120, 130);
            Label edition = LabelAt("三合一", 434, 33, 78, 28);
            edition.TextAlign = ContentAlignment.MiddleCenter;
            edition.BackColor = Color.FromArgb(225, 239, 234); edition.ForeColor = Color.FromArgb(33, 105, 84);
            LabelAt("生成类型", 44, 133, 105, 28);
            Types.SetBounds(158, 128, 336, 32); Types.DropDownStyle = ComboBoxStyle.DropDownList;
            Types.FlatStyle = FlatStyle.Flat; Types.BackColor = Color.White; Types.ForeColor = ForeColor;
            Types.Items.AddRange(Names); Types.SelectedIndex = 0; Controls.Add(Types);
            Types.AccessibleName = "生成类型";
            LabelAt("每次生成", 44, 183, 105, 28);
            Batch.SetBounds(158, 178, 105, 32); Batch.Minimum = 1; Batch.Maximum = Limit; Batch.Value = 5;
            Batch.BorderStyle = BorderStyle.FixedSingle; Batch.TextAlign = HorizontalAlignment.Center;
            Batch.ForeColor = ForeColor; Batch.AccessibleName = "每次生成数量";
            Controls.Add(Batch);
            Label units = LabelAt("只 · 每次点击追加", 278, 183, 210, 28);
            units.ForeColor = Color.FromArgb(105, 120, 130);
            LabelAt("速度上限", 44, 233, 105, 28);
            speed.SetBounds(148, 223, 264, 40); speed.Minimum = 40; speed.Maximum = 400; speed.Value = 180;
            speed.TickStyle = TickStyle.None; speed.BackColor = Color.White; speed.AccessibleName = "速度上限";
            Controls.Add(speed);
            Label speedValue = LabelAt("180 px/s", 414, 233, 88, 28);
            speedValue.ForeColor = Color.FromArgb(33, 105, 84);
            speed.ValueChanged += delegate { speedValue.Text = speed.Value + " px/s"; };
            ButtonAt(SpawnButton, "生成 · 校准版 × 5", 44, 286, 450, 46);
            SpawnButton.BackColor = Color.FromArgb(25, 119, 96); SpawnButton.ForeColor = Color.White;
            SpawnButton.Font = new Font(Font.FontFamily, 11, FontStyle.Bold);
            SpawnButton.FlatAppearance.BorderSize = 0;
            SpawnButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(30, 137, 111);
            SpawnButton.FlatAppearance.MouseDownBackColor = Color.FromArgb(18, 96, 78);
            Types.SelectedIndexChanged += delegate { RefreshSpawnCaption(); };
            Batch.ValueChanged += delegate { RefreshSpawnCaption(); };
            SpawnButton.Click += delegate { Guard(Spawn); };
            status.SetBounds(44, 384, 330, 28); status.BackColor = Color.White;
            status.Font = new Font(Font.FontFamily, 11, FontStyle.Bold); Controls.Add(status);
            stateBadge.SetBounds(413, 383, 81, 28); stateBadge.TextAlign = ContentAlignment.MiddleCenter;
            stateBadge.Font = new Font(Font.FontFamily, 9); Controls.Add(stateBadge);
            for (int i = 0; i < counts.Length; i++)
            {
                counts[i].SetBounds(44 + i * 154, 425, 142, 30);
                counts[i].TextAlign = ContentAlignment.MiddleCenter;
                counts[i].BackColor = Color.FromArgb(243, 246, 248); Controls.Add(counts[i]);
            }
            ButtonAt(PauseButton, "全部暂停", 24, 494, 156, 42);
            PauseButton.Click += delegate { Paused = !Paused; PauseButton.Text = Paused ? "全部继续" : "全部暂停"; RefreshStatus(); };
            ButtonAt(ClearButton, "全部清空", 192, 494, 156, 42); ClearButton.Click += delegate { ClearAll(); };
            Button exit = new Button(); ButtonAt(exit, "退出", 360, 494, 156, 42); exit.Click += delegate { Close(); };
            hint.SetBounds(26, 550, 490, 25); hint.ForeColor = Color.FromArgb(105, 120, 130); Controls.Add(hint);
            hint.Text = "三种版本可同时活动，关闭面板即可全部退出。";
            keys.SetBounds(26, 582, 490, 44); keys.Font = new Font(Font.FontFamily, 8.5f);
            keys.ForeColor = Color.FromArgb(116, 130, 140); Controls.Add(keys);
            RefreshStatus(); timer.Interval = 16;
            timer.Tick += delegate { Guard(Advance); };
        }

        /// <summary>Returns the total population across all loaded versions.</summary>
        internal int Total { get { int total = 0; foreach (Variant v in Variants) if (v != null) total += v.Items.Count; return total; } }

        /// <summary>Adds a label at fixed panel coordinates.</summary>
        /// <param name="text">Displayed text.</param><param name="x">Left edge.</param><param name="y">Top edge.</param>
        /// <param name="width">Control width.</param><param name="height">Control height.</param>
        private Label LabelAt(string text, int x, int y, int width, int height)
        {
            Label label = new Label { Text = text, BackColor = Color.White, TextAlign = ContentAlignment.MiddleLeft }; label.SetBounds(x, y, width, height); Controls.Add(label); return label;
        }

        /// <summary>Applies common styling and positions a button.</summary>
        /// <param name="button">Button to configure.</param><param name="text">Caption.</param>
        /// <param name="x">Left edge.</param><param name="y">Top edge.</param><param name="width">Width.</param><param name="height">Height.</param>
        private void ButtonAt(Button button, string text, int x, int y, int width, int height)
        {
            button.Text = text; button.SetBounds(x, y, width, height); button.FlatStyle = FlatStyle.Flat;
            button.UseVisualStyleBackColor = false;
            button.BackColor = Color.White; button.ForeColor = ForeColor;
            button.FlatAppearance.BorderColor = Color.FromArgb(215, 224, 229);
            button.FlatAppearance.MouseOverBackColor = Color.FromArgb(231, 241, 237);
            button.FlatAppearance.MouseDownBackColor = Color.FromArgb(215, 232, 224);
            button.Cursor = Cursors.Hand; Controls.Add(button);
        }

        /// <summary>Shows the selected version and batch count directly in the primary action.</summary>
        private void RefreshSpawnCaption()
        {
            SpawnButton.Text = "生成 · " + Names[Types.SelectedIndex] + " × " + Batch.Value;
        }

        /// <summary>Paints the two rounded content groups at the same DPI scale as the native controls.</summary>
        /// <param name="e">Paint surface and invalidated bounds.</param>
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            GraphicsState state = e.Graphics.Save();
            e.Graphics.ScaleTransform(dpiScale, dpiScale);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (Pen border = new Pen(Color.FromArgb(226, 232, 237)))
            {
                foreach (Rectangle rectangle in new[] { new Rectangle(24, 110, 492, 240), new Rectangle(24, 370, 492, 104) })
                using (GraphicsPath path = new GraphicsPath())
                {
                    const int diameter = 24;
                    path.AddArc(rectangle.Left, rectangle.Top, diameter, diameter, 180, 90);
                    path.AddArc(rectangle.Right - diameter, rectangle.Top, diameter, diameter, 270, 90);
                    path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
                    path.AddArc(rectangle.Left, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
                    path.CloseFigure(); e.Graphics.FillPath(Brushes.White, path); e.Graphics.DrawPath(border, path);
                }
            }
            e.Graphics.Restore(state);
        }

        /// <summary>Starts the shared timer and registers application-wide clear and exit hotkeys.</summary>
        /// <param name="e">Shown event arguments.</param>
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            clearKey = Native.RegisterHotKey(Handle, 1, 0x4003, (uint)Keys.C);
            quitKey = Native.RegisterHotKey(Handle, 2, 0x4003, (uint)Keys.Q);
            keys.Text = (clearKey ? "Ctrl + Alt + C：全部清空" : "清空快捷键不可用，请使用面板按钮") + "\n" +
                (quitKey ? "Ctrl + Alt + Q：退出" : "退出快捷键不可用，请使用面板按钮");
            clock.Start(); previous = clock.Elapsed.TotalSeconds; timer.Start();
        }

        /// <summary>Appends only the selected type; enforces one combined limit before changing any population.</summary>
        internal void Spawn()
        {
            int count = (int)Batch.Value, selected = Types.SelectedIndex;
            if (Total + count > Limit) { hint.ForeColor = Color.FromArgb(165, 62, 47); hint.Text = "三种合计最多 100 只，本次未添加。"; return; }
            if (Variants[selected] == null) Variants[selected] = new Variant(Names[selected], "variant" + selected + ".exe");
            Variants[selected].Add(count, Screen.PrimaryScreen.WorkingArea, dpiScale);
            hint.ForeColor = Color.FromArgb(105, 120, 130);
            hint.Text = "已追加 " + count + " 只" + Names[selected] + (Paused ? "，当前全部暂停。" : "。可切换类型继续添加。");
            RefreshStatus();
        }

        /// <summary>Advances all versions from one frame clock, preserving their separate original models.</summary>
        private void Advance()
        {
            double now = clock.Elapsed.TotalSeconds;
            float seconds = (float)(now - previous); previous = now;
            foreach (Variant variant in Variants)
                if (variant != null) variant.Step(seconds, Screen.PrimaryScreen.WorkingArea, speed.Value * dpiScale, Paused);
        }

        /// <summary>Clears every type while keeping type selection and pause state intact.</summary>
        internal void ClearAll()
        {
            foreach (Variant variant in Variants) if (variant != null) variant.Clear();
            hint.ForeColor = Color.FromArgb(105, 120, 130);
            hint.Text = "已清空全部类型。"; RefreshStatus();
        }

        /// <summary>Shows global count, per-type counts and the current pause state.</summary>
        private void RefreshStatus()
        {
            status.Text = "桌面上共有 " + Total + " 只  /  " + Limit;
            stateBadge.Text = Paused ? "已暂停" : Total == 0 ? "待生成" : "运行中";
            stateBadge.BackColor = Paused ? Color.FromArgb(255, 240, 213) : Total == 0 ? Color.FromArgb(235, 240, 244) : Color.FromArgb(224, 241, 231);
            stateBadge.ForeColor = Paused ? Color.FromArgb(137, 88, 20) : Total == 0 ? Color.FromArgb(93, 108, 121) : Color.FromArgb(36, 113, 76);
            for (int i = 0; i < 3; i++) counts[i].Text = Names[i] + "   " + (Variants[i] == null ? 0 : Variants[i].Items.Count);
        }

        /// <summary>Closes all overlays if a legacy rendering or native-window call fails.</summary>
        /// <param name="action">UI action or timer frame to execute.</param>
        private void Guard(Action action)
        {
            try { action(); }
            catch (Exception error)
            {
                timer.Stop(); ClearAll();
                MessageBox.Show(this, error.GetBaseException().Message, "程序已停止"); Close();
            }
        }

        /// <summary>Handles only this panel's registered clear and quit hotkeys.</summary>
        /// <param name="m">Incoming native window message.</param>
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0312)
            {
                if (m.WParam.ToInt32() == 1) ClearAll();
                if (m.WParam.ToInt32() == 2) Close();
            }
            base.WndProc(ref m);
        }

        /// <summary>Releases all three renderers, all overlays, the shared timer and hotkeys exactly once.</summary>
        /// <param name="disposing">Whether managed resources can be disposed.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && !cleaned)
            {
                cleaned = true; timer.Stop(); timer.Dispose();
                if (clearKey) Native.UnregisterHotKey(Handle, 1);
                if (quitKey) Native.UnregisterHotKey(Handle, 2);
                foreach (Variant variant in Variants) if (variant != null) variant.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    /// <summary>Native DPI and global-hotkey bindings used by the integrated control panel.</summary>
    internal static class Native
    {
        /// <summary>Enables physical-pixel coordinates before creating any windows.</summary>
        [DllImport("user32.dll")] internal static extern bool SetProcessDPIAware();
        /// <summary>Registers an application hotkey.</summary>
        /// <param name="window">Receiving window.</param><param name="id">Hotkey ID.</param>
        /// <param name="modifiers">Modifier flags.</param><param name="key">Virtual key code.</param>
        [DllImport("user32.dll", SetLastError = true)] internal static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
        /// <summary>Releases a hotkey registered by this window.</summary>
        /// <param name="window">Owning window.</param><param name="id">Hotkey ID.</param>
        [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(IntPtr window, int id);
    }
}
