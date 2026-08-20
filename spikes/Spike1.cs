// Spike 1 - settles the design-invalidating questions before any real code is written.
//
//   Q1  WS_EX_TOOLWINDOW + ShowInTaskbar=true  ->  is GW_OWNER really zero?
//   Q2  ShowInTaskbar=false                    ->  does it really create an owner?
//   Q3  ResizeMode=NoResize                    ->  is WS_THICKFRAME really stripped?
//                                                  (this is what refutes HTBOTTOMRIGHT)
//   Q4  LayoutTransform scale                  ->  does the HWND rect actually follow?
//   Q5  ContextMenu dismissal with / without WS_EX_NOACTIVATE
//
// Writes spikes\report.txt and exits. No human interaction required.
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace Spike
{
    public static class N
    {
        [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr h, uint cmd);
        [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h, int idx);
        [DllImport("user32.dll")] public static extern int SetWindowLong(IntPtr h, int idx, int val);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, UIntPtr e);
        [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left, Top, Right, Bottom;
            public int W { get { return Right - Left; } }
            public int H { get { return Bottom - Top; } } }

        public const uint GW_OWNER = 4;
        public const int GWL_STYLE = -16;
        public const int GWL_EXSTYLE = -20;

        public const int WS_THICKFRAME = 0x00040000;
        public const int WS_CAPTION    = 0x00C00000;
        public const int WS_SYSMENU    = 0x00080000;
        public const int WS_MINIMIZEBOX= 0x00020000;
        public const int WS_MAXIMIZEBOX= 0x00010000;

        public const int WS_EX_TOOLWINDOW = 0x00000080;
        public const int WS_EX_NOACTIVATE = 0x08000000;
        public const int WS_EX_LAYERED    = 0x00080000;
        public const int WS_EX_APPWINDOW  = 0x00040000;

        public const uint MOUSEEVENTF_MOVE_ABS = 0x8000 | 0x0001;
        public const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        public const uint MOUSEEVENTF_LEFTUP   = 0x0004;
    }

    public class Probe : Window
    {
        public Border Root;
        public ScaleTransform Scale;
        public IntPtr H;

        public Probe(bool showInTaskbar)
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = showInTaskbar;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.WidthAndHeight;
            ShowActivated = false;
            Left = 300; Top = 300;

            Scale = new ScaleTransform(1.0, 1.0);
            var panel = new StackPanel { Width = 166 };
            panel.Children.Add(new TextBlock { Text = "vibespan spike", FontSize = 12, Foreground = Brushes.White, Height = 18 });
            panel.Children.Add(new TextBlock { Text = "5h  4%  4h53", FontSize = 12, Foreground = Brushes.White, Height = 18 });

            Root = new Border
            {
                Background = (Brush)new BrushConverter().ConvertFromString("#F21E2029"),
                Padding = new Thickness(8, 3, 8, 3),
                Child = panel
            };
            Root.LayoutTransform = Scale;   // on the CONTENT, never on the Window
            Content = Root;
        }
    }

    public static class Program
    {
        static StringBuilder _log = new StringBuilder();
        static void L(string s) { _log.Append(s).Append(Environment.NewLine); Console.WriteLine(s); }
        static string Hex(int v) { return "0x" + v.ToString("X8", CultureInfo.InvariantCulture); }
        static string YN(bool b) { return b ? "YES" : "no"; }

        static Probe _a, _b;
        static ContextMenu _menu;
        static int _step;
        static DispatcherTimer _t;

        [STAThread]
        public static void Main()
        {
            var app = new Application();
            app.Startup += delegate { Begin(); };
            app.Run();
        }

        static void Begin()
        {
            L("=== vibespan spike 1 ===");
            L("run at " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            L("OS " + Environment.OSVersion.Version + "   CLR " + Environment.Version);
            L("");

            _a = new Probe(true);
            _a.Loaded += delegate
            {
                _a.H = new WindowInteropHelper(_a).Handle;
                // Q1/Q3: add TOOLWINDOW the way the real app will, then read back the truth.
                int ex = N.GetWindowLong(_a.H, N.GWL_EXSTYLE);
                N.SetWindowLong(_a.H, N.GWL_EXSTYLE, ex | N.WS_EX_TOOLWINDOW);
                _step = 0;
                _t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
                _t.Tick += delegate { Step(); };
                _t.Start();
            };
            _a.Show();
        }

        static void Step()
        {
            _step++;
            switch (_step)
            {
                case 1: Q1_Q3(); break;
                case 2: Q4_Scale(); break;
                case 3: Q5_Setup(true); break;    // with WS_EX_NOACTIVATE
                case 4: Q5_Click(); break;
                case 5: Q5_Verdict("WITH    WS_EX_NOACTIVATE"); Q5_Setup(false); break;
                case 6: Q5_Click(); break;
                case 7: Q5_Verdict("WITHOUT WS_EX_NOACTIVATE"); Q2_Owner(); break;
                case 8: Finish(); break;
            }
        }

        static void Q1_Q3()
        {
            int style = N.GetWindowLong(_a.H, N.GWL_STYLE);
            int ex = N.GetWindowLong(_a.H, N.GWL_EXSTYLE);
            IntPtr owner = N.GetWindow(_a.H, N.GW_OWNER);

            L("--- Q1  ShowInTaskbar=TRUE + WS_EX_TOOLWINDOW ---");
            L("  GW_OWNER            : " + owner.ToInt64() + "   -> owner window created? " + YN(owner != IntPtr.Zero));
            L("  WS_EX_TOOLWINDOW    : " + YN((ex & N.WS_EX_TOOLWINDOW) != 0));
            L("  WS_EX_APPWINDOW     : " + YN((ex & N.WS_EX_APPWINDOW) != 0));
            L("  WS_EX_LAYERED       : " + YN((ex & N.WS_EX_LAYERED) != 0) + "   (AllowsTransparency)");
            L("  EXSTYLE             : " + Hex(ex));
            L("");
            L("--- Q3  does ResizeMode=NoResize strip WS_THICKFRAME? ---");
            L("  STYLE               : " + Hex(style));
            L("  WS_THICKFRAME       : " + YN((style & N.WS_THICKFRAME) != 0) + "   <-- if 'no', HTBOTTOMRIGHT is a NO-OP");
            L("  WS_CAPTION          : " + YN((style & N.WS_CAPTION) != 0));
            L("  WS_SYSMENU          : " + YN((style & N.WS_SYSMENU) != 0));
            L("  WS_MINIMIZEBOX      : " + YN((style & N.WS_MINIMIZEBOX) != 0));
            L("  WS_MAXIMIZEBOX      : " + YN((style & N.WS_MAXIMIZEBOX) != 0));
            L("");
        }

        static void Q4_Scale()
        {
            L("--- Q4  does the HWND follow LayoutTransform scale? ---");
            double[] scales = { 1.0, 1.25, 1.5, 2.0, 0.75, 1.0 };
            foreach (double s in scales)
            {
                _a.Scale.ScaleX = s; _a.Scale.ScaleY = s;
                _a.Root.InvalidateMeasure();
                _a.UpdateLayout();
                N.RECT r;
                N.GetWindowRect(_a.H, out r);
                L("  scale " + s.ToString("0.00", CultureInfo.InvariantCulture) +
                  "  ->  HWND " + r.W + " x " + r.H +
                  "   (WPF ActualSize " + _a.ActualWidth.ToString("0", CultureInfo.InvariantCulture) +
                  " x " + _a.ActualHeight.ToString("0", CultureInfo.InvariantCulture) + ")");
            }
            L("");
        }

        static bool _menuOpenAfterShow;
        static void Q5_Setup(bool noActivate)
        {
            int ex = N.GetWindowLong(_a.H, N.GWL_EXSTYLE);
            if (noActivate) ex |= N.WS_EX_NOACTIVATE; else ex &= ~N.WS_EX_NOACTIVATE;
            N.SetWindowLong(_a.H, N.GWL_EXSTYLE, ex);

            _menu = new ContextMenu();
            var mi = new MenuItem { Header = "Toggle", IsCheckable = true, StaysOpenOnClick = true };
            _menu.Items.Add(mi);
            _menu.Items.Add(new MenuItem { Header = "Something else" });
            _menu.PlacementTarget = _a.Root;
            _a.Root.ContextMenu = _menu;
            _menu.IsOpen = true;

            _menuOpenAfterShow = _menu.IsOpen;
            IntPtr fg = N.GetForegroundWindow();
            L("  opened menu. IsOpen=" + YN(_menu.IsOpen) +
              "  foreground==us? " + YN(fg == _a.H) +
              "  Mouse.Captured=" + (Mouse.Captured == null ? "null" : Mouse.Captured.GetType().Name));
        }

        static void Q5_Click()
        {
            // Click far away from the menu, on the desktop area of the primary monitor.
            int sx = 1400, sy = 950;
            N.SetCursorPos(sx, sy);
            double vw = SystemParameters.VirtualScreenWidth, vh = SystemParameters.VirtualScreenHeight;
            uint nx = (uint)(sx * 65535 / vw), ny = (uint)(sy * 65535 / vh);
            N.mouse_event(N.MOUSEEVENTF_MOVE_ABS | N.MOUSEEVENTF_LEFTDOWN, nx, ny, 0, UIntPtr.Zero);
            N.mouse_event(N.MOUSEEVENTF_LEFTUP, nx, ny, 0, UIntPtr.Zero);
        }

        static void Q5_Verdict(string label)
        {
            bool still = _menu != null && _menu.IsOpen;
            L("--- Q5  " + label + " ---");
            L("  opened OK           : " + YN(_menuOpenAfterShow));
            L("  still open after an outside click : " + YN(still) +
              "   -> dismissal " + (still ? "BROKEN" : "works"));
            if (_menu != null) _menu.IsOpen = false;
            L("");
        }

        static void Q2_Owner()
        {
            _b = new Probe(false);
            _b.Left = 700; _b.Top = 300;
            _b.Loaded += delegate
            {
                _b.H = new WindowInteropHelper(_b).Handle;
                IntPtr owner = N.GetWindow(_b.H, N.GW_OWNER);
                int ex = N.GetWindowLong(_b.H, N.GWL_EXSTYLE);
                L("--- Q2  ShowInTaskbar=FALSE ---");
                L("  GW_OWNER            : " + owner.ToInt64() + "   -> owner window created? " + YN(owner != IntPtr.Zero));
                if (owner != IntPtr.Zero)
                {
                    int oex = N.GetWindowLong(owner, N.GWL_EXSTYLE);
                    L("  owner EXSTYLE       : " + Hex(oex));
                    L("  owner WS_EX_TOPMOST : " + YN((oex & 0x00000008) != 0) + "   <-- if 'no', it sinks us");
                }
                L("  our  EXSTYLE        : " + Hex(ex));
                L("");
            };
            _b.Show();
        }

        static void Finish()
        {
            _t.Stop();
            L("=== end ===");
            string dir = AppDomain.CurrentDomain.BaseDirectory;
            try { File.WriteAllText(Path.Combine(dir, "report.txt"), _log.ToString()); }
            catch (Exception e) { Console.WriteLine("write failed: " + e.Message); }
            try { if (_a != null) _a.Close(); if (_b != null) _b.Close(); } catch { }
            Application.Current.Shutdown();
        }
    }
}
