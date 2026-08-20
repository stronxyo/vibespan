// Spike 3 - the real menu-dismissal gate.
//
// Spike 2 was contaminated: its "outside click" landed on a window in the SAME process,
// which a captured Popup can see even when not foreground. A real user clicks another
// APP. So this spike launches a second copy of itself (--target) and clicks THAT - a
// genuinely foreign HWND in a foreign process.
//
// Variants:
//   1  real right-click, no styles, no hook        (control)
//   2  real right-click + selective WM_MOUSEACTIVATE hook   <- the design's choice
//   3  programmatic open + SetForegroundWindow      (the tray-icon path)
//   4  LEFT press with the hook -> must NOT take foreground (drag must not steal focus)
//
// Writes spikes\report3.txt.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace Spike3
{
    public static class N
    {
        [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h, int i);
        [DllImport("user32.dll")] public static extern int SetWindowLong(IntPtr h, int i, int v);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, UIntPtr e);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);

        public const int GWL_EXSTYLE = -20;
        public const int WS_EX_TOOLWINDOW = 0x00000080;

        public const int WM_MOUSEACTIVATE = 0x0021;
        public const int WM_LBUTTONDOWN   = 0x0201;
        public const int MA_ACTIVATE      = 1;
        public const int MA_NOACTIVATE    = 3;

        public const uint LEFTDOWN = 0x0002, LEFTUP = 0x0004, RIGHTDOWN = 0x0008, RIGHTUP = 0x0010;

        public static void Click(int x, int y, bool right)
        {
            SetCursorPos(x, y);
            mouse_event(right ? RIGHTDOWN : LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
            mouse_event(right ? RIGHTUP : LEFTUP, 0, 0, 0, UIntPtr.Zero);
        }
        public static void LeftPressRelease(int x, int y)
        {
            SetCursorPos(x, y);
            mouse_event(LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
            mouse_event(LEFTUP, 0, 0, 0, UIntPtr.Zero);
        }
    }

    public static class Program
    {
        static StringBuilder _log = new StringBuilder();
        static void L(string s) { _log.Append(s).Append(Environment.NewLine); Console.WriteLine(s); }
        static string YN(bool b) { return b ? "YES" : "no"; }

        static Window _w; static Border _root; static ContextMenu _menu;
        static IntPtr _h; static Process _target; static IntPtr _targetHwnd = IntPtr.Zero;
        static bool _hookOn;
        static int _vi, _phase;
        static DispatcherTimer _t;
        static bool _opened, _fgIsUs; static string _cap;

        // widget geometry
        const int WX = 300, WY = 300;

        [STAThread]
        public static void Main(string[] argv)
        {
            if (argv.Length > 0 && argv[0] == "--target") { RunTarget(); return; }
            var app = new Application();
            app.Startup += delegate { Begin(); };
            app.Run();
        }

        // ---- the foreign click target, a separate process ----
        static void RunTarget()
        {
            var app = new Application();
            var w = new Window
            {
                Title = "spike3-target", WindowStyle = WindowStyle.None, Topmost = true,
                ShowInTaskbar = false, ResizeMode = ResizeMode.NoResize, ShowActivated = false,
                Left = 950, Top = 620, Width = 240, Height = 150,
                Background = (Brush)new BrushConverter().ConvertFromString("#FF3A2E4A"),
                Content = new TextBlock { Text = "foreign window", Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            };
            var kill = new DispatcherTimer { Interval = TimeSpan.FromSeconds(90) };
            kill.Tick += delegate { app.Shutdown(); };
            kill.Start();
            w.Show();
            app.Run();
        }

        static void Begin()
        {
            L("=== vibespan spike 3 : menu dismissal against a FOREIGN window ===");
            L("run at " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            L("");

            _target = Process.Start(new ProcessStartInfo(
                System.Reflection.Assembly.GetExecutingAssembly().Location, "--target")
                { UseShellExecute = false });

            _root = new Border
            {
                Background = (Brush)new BrushConverter().ConvertFromString("#F21E2029"),
                Padding = new Thickness(8, 3, 8, 3),
                Child = new TextBlock { Text = "vibespan 5h 4%", FontSize = 12,
                    Foreground = Brushes.White, Width = 166, Height = 36 }
            };
            _w = new Window
            {
                Title = "spike3-widget",
                WindowStyle = WindowStyle.None, AllowsTransparency = true,
                Background = Brushes.Transparent, Topmost = true, ShowInTaskbar = true,
                ResizeMode = ResizeMode.NoResize, SizeToContent = SizeToContent.WidthAndHeight,
                ShowActivated = false, Left = WX, Top = WY, Content = _root
            };
            _w.Loaded += delegate
            {
                _h = new WindowInteropHelper(_w).Handle;
                int ex = N.GetWindowLong(_h, N.GWL_EXSTYLE);
                N.SetWindowLong(_h, N.GWL_EXSTYLE, ex | N.WS_EX_TOOLWINDOW);
                HwndSource.FromHwnd(_h).AddHook(Hook);      // kept alive: static method
                _vi = 1; _phase = 0;
                _t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(750) };
                _t.Tick += delegate { Tick(); };
                _t.Start();
            };
            _w.Show();
        }

        // Selective activation: left button never activates (drag must not steal focus),
        // everything else does (so the right-click menu gets foreground -> real capture).
        static IntPtr Hook(IntPtr hwnd, int msg, IntPtr wp, IntPtr lp, ref bool handled)
        {
            if (_hookOn && msg == N.WM_MOUSEACTIVATE)
            {
                int trigger = ((int)lp >> 16) & 0xFFFF;
                handled = true;
                return new IntPtr(trigger == N.WM_LBUTTONDOWN ? N.MA_NOACTIVATE : N.MA_ACTIVATE);
            }
            return IntPtr.Zero;
        }

        static void Tick()
        {
            switch (_phase)
            {
                case 0: Setup(); _phase++; break;
                case 1: Probe(); _phase++; break;
                case 2: DismissAction(); _phase++; break;
                case 3:
                    Verdict(); _phase = 0; _vi++;
                    if (_vi > 4) Finish();
                    break;
            }
        }

        static void Setup()
        {
            _hookOn = (_vi == 2 || _vi == 4);

            // make sure we do NOT start out as foreground
            if (_targetHwnd == IntPtr.Zero) _targetHwnd = FindTarget();
            if (_targetHwnd != IntPtr.Zero) N.SetForegroundWindow(_targetHwnd);

            _menu = new ContextMenu();
            _menu.Items.Add(new MenuItem { Header = "Toggle", IsCheckable = true, StaysOpenOnClick = true });
            _menu.Items.Add(new MenuItem { Header = "Another" });
            _menu.PlacementTarget = _root;
            _root.ContextMenu = _menu;

            if (_vi == 3) { N.SetForegroundWindow(_h); _menu.IsOpen = true; }
            else if (_vi == 4) N.LeftPressRelease(WX + 60, WY + 20);   // left press, no menu
            else N.Click(WX + 60, WY + 20, true);                      // real right-click
        }

        static IntPtr FindTarget()
        {
            foreach (Process p in Process.GetProcessesByName("Spike3"))
            {
                if (p.Id == Process.GetCurrentProcess().Id) continue;
                if (p.MainWindowHandle != IntPtr.Zero) return p.MainWindowHandle;
            }
            return IntPtr.Zero;
        }

        static void Probe()
        {
            _opened = _menu.IsOpen;
            _fgIsUs = N.GetForegroundWindow() == _h;
            _cap = Mouse.Captured == null ? "null" : Mouse.Captured.GetType().Name;
        }

        static void DismissAction()
        {
            if (_vi == 4) return;                       // nothing to dismiss
            N.Click(1070, 695, false);                  // click the FOREIGN window
        }

        static void Verdict()
        {
            string name;
            switch (_vi)
            {
                case 1: name = "1  real right-click, no hook (control)"; break;
                case 2: name = "2  real right-click + selective WM_MOUSEACTIVATE"; break;
                case 3: name = "3  programmatic open + SetForegroundWindow (tray path)"; break;
                default: name = "4  LEFT press with hook - must NOT take foreground"; break;
            }
            L("--- " + name + " ---");
            if (_vi == 4)
            {
                L("   window took foreground on left press : " + YN(_fgIsUs) +
                  "   ->  " + (_fgIsUs ? "BAD - drag would steal focus" : "GOOD - drag keeps focus elsewhere"));
            }
            else
            {
                bool still = _menu.IsOpen;
                L("   menu opened                    : " + YN(_opened));
                L("   window was foreground          : " + YN(_fgIsUs));
                L("   Mouse.Captured                 : " + _cap);
                L("   still open after FOREIGN click : " + YN(still) +
                  "   ->  " + (still ? "DISMISSAL BROKEN" : "dismissal WORKS"));
            }
            L("");
            if (_menu != null) _menu.IsOpen = false;
            _root.ContextMenu = null;
        }

        static void Finish()
        {
            _t.Stop();
            L("=== end ===");
            try { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "report3.txt"), _log.ToString()); }
            catch (Exception e) { Console.WriteLine("write failed: " + e.Message); }
            try { if (_target != null && !_target.HasExited) _target.Kill(); } catch { }
            try { _w.Close(); } catch { }
            Application.Current.Shutdown();
        }
    }
}
