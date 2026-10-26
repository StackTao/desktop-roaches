using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using DesktopRoaches;

internal static class NativeSmoke
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { internal int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(Native.Point point);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern int GetGuiResources(IntPtr process, int flags);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    private static ControlPanel panel;
    private static System.Windows.Forms.Timer timer;
    private static int stage;
    private static float pausedX, pausedY, pausedGait;
    private static IntPtr[] handles;
    private static int gdiBaseline;
    private static string directory;
    private static int code;
    private static Stopwatch measurement;
    private static TimeSpan cpuBefore;
    private static int frameBefore;

    /// <summary>Checks a native integration invariant.</summary>
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        Console.WriteLine("PASS: " + message);
    }

    /// <summary>Runs the actual WinForms message loop and exercises the application's controls.</summary>
    [STAThread]
    private static int Main(string[] args)
    {
        directory = args[0];
        Native.SetProcessDPIAware();
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        panel = new ControlPanel();
        timer = new System.Windows.Forms.Timer { Interval = 350 };
        timer.Tick += Tick;
        panel.Shown += delegate { timer.Start(); };
        Application.Run(panel);
        timer.Dispose(); panel.Dispose();
        if (handles != null) foreach (IntPtr hwnd in handles) Check(!IsWindow(hwnd), "Overlay destroyed after exit");
        File.WriteAllText(Path.Combine(directory, "native-result.txt"), code == 0 ? "PASS" : "FAIL");
        return code;
    }

    /// <summary>Executes staged UI operations so real timer frames run between assertions.</summary>
    private static void Tick(object sender, EventArgs e)
    {
        try
        {
            if (stage == 0)
            {
                IntPtr foreground = GetForegroundWindow();
                panel.SpawnButton.PerformClick();
                panel.Batch.Value = 3; panel.SpawnButton.PerformClick();
                Check(panel.Windows.Count == 8, "Two button clicks appended exactly 5 + 3 insects");
                Check(GetForegroundWindow() == foreground, "Spawning overlays did not steal foreground focus");
                Rectangle bounds = Screen.PrimaryScreen.WorkingArea;
                foreach (RoachWindow window in panel.Windows)
                {
                    int flags = GetWindowLong(window.Handle, -20);
                    Check((flags & 0x080800A8) == 0x080800A8, "Layered, topmost, click-through, tool-window, no-activation flags");
                    Rect r; GetWindowRect(window.Handle, out r);
                    Check(r.Left >= bounds.Left && r.Top >= bounds.Top && r.Right <= bounds.Right && r.Bottom <= bounds.Bottom,
                          "Actual native window fully inside screen working area");
                    Check(WindowFromPoint(new Native.Point((r.Left + r.Right) / 2, (r.Top + r.Bottom) / 2)) != window.Handle,
                          "Window hit test passes through to underlying application");
                }
                panel.Windows[0].SavePreview(Path.Combine(directory, "sprite-preview.png"));
                using (Bitmap bitmap = new Bitmap(panel.Width, panel.Height))
                {
                    panel.DrawToBitmap(bitmap, new Rectangle(Point.Empty, panel.Size));
                    bitmap.Save(Path.Combine(directory, "panel-preview.png"), ImageFormat.Png);
                }
                Console.WriteLine("HOTKEY: clear registration error=" + panel.ClearKeyError + "; exit registration error=" + panel.QuitKeyError);
                panel.PauseButton.PerformClick();
                Roach first = panel.Population.Items[0];
                pausedX = first.X; pausedY = first.Y; pausedGait = first.Gait;
            }
            else if (stage == 1)
            {
                Roach first = panel.Population.Items[0];
                Check(first.X == pausedX && first.Y == pausedY && first.Gait == pausedGait, "Pause freezes movement and gait across real timer frames");
                panel.PauseButton.PerformClick();
                Check(!panel.Paused, "Resume button restores movement");
                panel.ClearButton.PerformClick();
                Check(panel.Windows.Count == 0 && panel.Population.Items.Count == 0, "Clear button removes model and native windows");
                gdiBaseline = GetGuiResources(Process.GetCurrentProcess().Handle, 0);
                for (int cycle = 0; cycle < 12; cycle++)
                {
                    panel.Batch.Value = 10; panel.SpawnButton.PerformClick(); panel.ClearButton.PerformClick();
                }
                int gdiAfter = GetGuiResources(Process.GetCurrentProcess().Handle, 0);
                Check(gdiAfter <= gdiBaseline + 5, "Repeated generation/clear releases GDI resources (before=" + gdiBaseline + ", after=" + gdiAfter + ")");
                panel.Batch.Value = 100; panel.SpawnButton.PerformClick();
                Check(panel.Windows.Count == 100, "100 real transparent windows created");
                panel.Batch.Value = 1; panel.SpawnButton.PerformClick();
                Check(panel.Windows.Count == 100, "Population limit rejects extra batch");
                measurement = Stopwatch.StartNew(); cpuBefore = Process.GetCurrentProcess().TotalProcessorTime;
                frameBefore = panel.FrameCount;
                timer.Interval = 2500;
            }
            else if (stage == 2)
            {
                double cpu = (Process.GetCurrentProcess().TotalProcessorTime - cpuBefore).TotalSeconds;
                Console.WriteLine("METRIC: 100 insects; elapsed=" + measurement.Elapsed.TotalSeconds.ToString("F2") +
                                  "s; CPU=" + cpu.ToString("F2") + "s; memory=" +
                                  (Process.GetCurrentProcess().WorkingSet64 / 1048576) + " MiB");
                Console.WriteLine("METRIC: 100-insect frame rate=" + ((panel.FrameCount - frameBefore) / measurement.Elapsed.TotalSeconds).ToString("F1") + " fps");
                SendMessage(panel.Handle, 0x0312, new IntPtr(1), IntPtr.Zero);
                Check(panel.Windows.Count == 0, "Global clear hotkey handler clears everything");
                panel.Batch.Value = 5; panel.SpawnButton.PerformClick();
                handles = new IntPtr[panel.Windows.Count];
                for (int i = 0; i < handles.Length; i++) handles[i] = panel.Windows[i].Handle;
                measurement.Restart(); frameBefore = panel.FrameCount; timer.Interval = 1200;
            }
            else if (stage == 3)
            {
                Console.WriteLine("METRIC: default 5-insect frame rate=" + ((panel.FrameCount - frameBefore) / measurement.Elapsed.TotalSeconds).ToString("F1") + " fps");
                timer.Stop();
                SendMessage(panel.Handle, 0x0312, new IntPtr(2), IntPtr.Zero);
            }
            stage++;
        }
        catch (Exception error)
        {
            Console.WriteLine("FAIL: " + error); code = 1; timer.Stop(); panel.Close();
        }
    }
}
