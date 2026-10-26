using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using IntegratedRoaches;

internal static class IntegrationTests
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private static ControlPanel panel;
    private static System.Windows.Forms.Timer timer;
    private static string output;
    private static int stage, result;
    private static List<float> frozen;
    private static readonly List<IntPtr> handles = new List<IntPtr>();

    /// <summary>Validates one panel controlling the three original embedded assemblies together.</summary>
    /// <param name="args">Directory for application-rendered panel and sprite previews.</param>
    [STAThread]
    private static int Main(string[] args)
    {
        output = args[0]; Directory.CreateDirectory(output);
        Native.SetProcessDPIAware(); Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        using (panel = new ControlPanel())
        using (timer = new System.Windows.Forms.Timer())
        {
            timer.Interval = 350; timer.Tick += Tick;
            panel.Shown += delegate { timer.Start(); };
            Application.Run(panel);
        }
        bool allClosed = true;
        foreach (IntPtr handle in handles) allClosed &= !IsWindow(handle);
        Check(allClosed, "All 100 overlays destroyed after the only panel closes");
        return result;
    }

    /// <summary>Asserts one integration requirement and prints its outcome.</summary>
    /// <param name="condition">Whether the requirement holds.</param><param name="message">Requirement description.</param>
    private static void Check(bool condition, string message)
    {
        Console.WriteLine((condition ? "PASS: " : "FAIL: ") + message);
        if (!condition) throw new Exception(message);
    }

    /// <summary>Captures model position and animation clocks for every population without assuming one assembly identity.</summary>
    private static List<float> Snapshot()
    {
        List<float> values = new List<float>();
        foreach (Variant variant in panel.Variants)
            foreach (object roach in variant.Items)
                foreach (string field in new[] { "X", "Y", "Gait", "AntennaTime" })
                    values.Add((float)roach.GetType().GetField(field, Members).GetValue(roach));
        return values;
    }

    /// <summary>Exercises mixed generation, pause/resume, capacity, cleanup and original render output over real timer frames.</summary>
    /// <param name="sender">Test timer.</param><param name="e">Tick event arguments.</param>
    private static void Tick(object sender, EventArgs e)
    {
        try
        {
            if (stage == 0)
            {
                Check(panel.Types.Items.Count == 3 && (string)panel.Types.Items[0] == "校准版" &&
                    (string)panel.Types.Items[1] == "质感版" && (string)panel.Types.Items[2] == "fxq版", "Exact user suffix names");
                IntPtr foreground = GetForegroundWindow();
                for (int i = 0; i < 3; i++)
                {
                    panel.Types.SelectedIndex = i; panel.Batch.Value = i + 2; panel.SpawnButton.PerformClick();
                    Check(panel.Variants[i].Items.Count == i + 2, "Selected type appends the requested batch: " + panel.Names[i]);
                    Form first = panel.Variants[i].Windows[0];
                    first.GetType().GetMethod("SavePreview", Members).Invoke(first, new object[] { Path.Combine(output, "integrated-type-" + i + ".png") });
                    foreach (Form window in panel.Variants[i].Windows)
                    {
                        long style = GetWindowLong(window.Handle, -20);
                        Check((style & 0x80800A8) == 0x80800A8, "Original click-through / topmost / nonactivating overlay retained");
                        Check(Screen.PrimaryScreen.WorkingArea.Contains(window.Bounds), "Mixed-type overlay inside working area");
                    }
                }
                Check(GetForegroundWindow() == foreground, "Generating mixed types does not steal focus");
                Check(panel.Total == 9, "Three types coexist (2 + 3 + 4)");
                Check(Application.OpenForms.Count == 10, "Exactly one control panel plus nine overlays; no old control panels");
                Check(panel.Variants[0].Windows[0].GetType().Assembly != panel.Variants[1].Windows[0].GetType().Assembly &&
                    panel.Variants[1].Windows[0].GetType().Assembly != panel.Variants[2].Windows[0].GetType().Assembly,
                    "Each type uses its own original embedded renderer assembly");
                panel.PauseButton.PerformClick(); frozen = Snapshot();
                using (Bitmap preview = new Bitmap(panel.Width, panel.Height))
                { panel.DrawToBitmap(preview, new Rectangle(Point.Empty, panel.Size)); preview.Save(Path.Combine(output, "integrated-panel.png")); }
            }
            else if (stage == 1)
            {
                Check(string.Join(",", frozen) == string.Join(",", Snapshot()), "Global pause freezes every type across real timer frames");
                panel.PauseButton.PerformClick();
            }
            else if (stage == 2)
            {
                Check(string.Join(",", frozen) != string.Join(",", Snapshot()), "Resume advances mixed populations");
                panel.Batch.Value = 92; panel.SpawnButton.PerformClick();
                Check(panel.Total == 9, "Shared 100-insect limit rejects an entire overflowing batch");
                panel.PauseButton.PerformClick(); panel.Batch.Value = 1; panel.SpawnButton.PerformClick();
                Check(panel.Total == 10 && panel.Paused && panel.Variants[2].Items.Count == 5, "Generation during pause appends only the selected type");
                frozen = Snapshot();
            }
            else if (stage == 3)
            {
                Check(string.Join(",", frozen) == string.Join(",", Snapshot()), "New and existing insects remain paused together");
                panel.ClearButton.PerformClick(); Check(panel.Total == 0, "Clear removes all three populations");
                int baseline = GetGuiResources(System.Diagnostics.Process.GetCurrentProcess().Handle, 0);
                for (int cycle = 0; cycle < 5; cycle++)
                {
                    for (int i = 0; i < 3; i++) { panel.Types.SelectedIndex = i; panel.Batch.Value = 2; panel.SpawnButton.PerformClick(); }
                    panel.ClearButton.PerformClick();
                }
                int after = GetGuiResources(System.Diagnostics.Process.GetCurrentProcess().Handle, 0);
                Check(after <= baseline + 5, "Repeated mixed-type generation/clear releases GDI resources: " + baseline + " -> " + after);
                for (int i = 0; i < 3; i++)
                {
                    panel.Types.SelectedIndex = i; panel.Batch.Value = i == 2 ? 34 : 33; panel.SpawnButton.PerformClick();
                    foreach (Form window in panel.Variants[i].Windows) handles.Add(window.Handle);
                }
                Check(panel.Total == 100, "Combined capacity supports 33 + 33 + 34 mixed insects");
                // Let the common timer present all 100 windows before checking shutdown.
                panel.PauseButton.PerformClick();
            }
            else { timer.Stop(); panel.Close(); }
            stage++;
        }
        catch (Exception error)
        { Console.WriteLine("FAIL: " + error); result = 1; timer.Stop(); panel.Close(); }
    }

    /// <summary>Reads extended window style flags.</summary>
    /// <param name="window">Window handle.</param><param name="index">Style slot.</param>
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(IntPtr window, int index);
    /// <summary>Returns the current foreground window.</summary>
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    /// <summary>Checks whether an overlay handle remains alive.</summary>
    /// <param name="window">Window handle.</param>
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr window);
    /// <summary>Counts process-owned graphical resources.</summary>
    /// <param name="process">Process handle.</param><param name="flags">Resource category.</param>
    [DllImport("user32.dll")] private static extern int GetGuiResources(IntPtr process, int flags);
}
