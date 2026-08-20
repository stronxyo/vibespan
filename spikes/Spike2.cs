// Spike 2 - menu dismissal. Spike 1 showed the menu never closes when opened
// programmatically on a ShowActivated=false window, with or without WS_EX_NOACTIVATE,
// because the window is never foreground and WPF's Popup dismissal is capture-based.
//
// This tests the candidate fixes. Variants:
//   A  no NOACTIVATE, programmatic open, no SetForegroundWindow   (baseline, expect BROKEN)
//   B  no NOACTIVATE, programmatic open, + SetForegroundWindow
//   C  WITH NOACTIVATE, programmatic open, + SetForegroundWindow + PostMessage(WM_NULL)
//   D  no NOACTIVATE, real simulated RIGHT-CLICK on the window   (the widget's own path)
//   E  WITH NOACTIVATE, real simulated RIGHT-CLICK on the window
//
// The "outside click" lands on a second window we own, so nothing else on the desktop
// is touched. Writes spikes\report2.txt and exits.
using System;
using System.Collections.Generic;
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

namespace Spike2
{
    public static class N
    {
        [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h, int i);
        [DllImport("user32.dll")] public static extern int SetWindowLong(IntPtr h, int i, int v);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, UIntPtr e);

        public const int GWL_EXSTYLE = -20;
        public const int WS_EX_TOOLWINDOW = 0x00000080;
        public const int WS_EX_NOACTIVATE = 0x08000000;
        public const uint WM_NULL = 0x0000;

        public const uint MOVE_ABS  = 0x8000 | 0x0001;
        public const uint LEFTDOWN  = 0x0002;
        public const uint LEFTUP    = 0x0004;
        public const uint RIGHTDOWN = 0x0008;
        public const uint RIGHTUP   = 0x0010;

        // No MOUSEEVENTF_ABSOLUTE: without MOUSEEVENTF_VIRTUALDESK it normalises against the
        // PRIMARY monitor, which silently lands the click somewhere else entirely on a
        // multi-monitor desktop. SetCursorPos has already put the pointer in the right place,
        // so send the buttons at the current position.
        public static void ClickAt(int x, int y, bool right)
        {
            SetCursorPos(x, y);
            mouse_event(right ? RIGHTDOWN : LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
            mouse_event(right ? RIGHTUP : LEFTUP, 0, 0, 0, UIntPtr.Zero);
        }
    }

    class Variant
    {
        public string Name;
        public bool NoActivate;
        public bool ForceForeground;
        public bool PostNull;
        public bool RealRightClick;
    }

    public static class Program
    {
        static StringBuilder _log = new StringBuilder();
        static void L(string s) { _log.Append(s).Append(Environment.NewLine); Console.WriteLine(s); }
        static string YN(bool b) { return b ? "YES" : "no"; }

        static Window _w, _target;
        static Border _root;
        static ContextMenu _menu;
        static IntPtr _h;
        static List<Variant> _vars = new List<Variant>();
        static int _vi = -1, _phase;
        static DispatcherTimer _t;

        // probe results for the current variant
        static bool _opened, _fgIsUs, _sfgOk; static string _captured;

        [STAThread]
        public static void Main()
        {
            _vars.Add(new Variant { Name = "A  plain programmatic open (baseline)", NoActivate = false });
            _vars.Add(new Variant { Name = "B  + SetForegroundWindow", NoActivate = false, ForceForeground = true });
            _vars.Add(new Variant { Name = "C  NOACTIVATE + SetForegroundWindow + WM_NULL", NoActivate = true, ForceForeground = true, PostNull = true });
            _vars.Add(new Variant { Name = "D  real right-click, no NOACTIVATE", NoActivate = false, RealRightClick = true });
            _vars.Add(new Variant { Name = "E  real right-click, WITH NOACTIVATE", NoActivate = true, RealRightClick = true });

            var app = new Application();
            app.Startup += delegate { Begin(); };
            app.Run();
        }

        static void Begin()
        {
            L("=== vibespan spike 2 : context-menu dismissal ===");
            L("run at " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            L("");

            // The widget-like window.
            _root = new Border
            {
                Background = (Brush)new BrushConverter().ConvertFromString("#F21E2029"),
                Padding = new Thickness(8, 3, 8, 3),
                Child = new TextBlock { Text = "vibespan  5h 4%  4h53", FontSize = 12,
                                        Foreground = Brushes.White, Width = 166, Height = 36 }
            };
            _w = new Window
            {
                WindowStyle = WindowStyle.None, AllowsTransparency = true,
                Background = Brushes.Transparent, Topmost = true,
                ShowInTaskbar = true, ResizeMode = ResizeMode.NoResize,
                SizeToContent = SizeToContent.WidthAndHeight, ShowActivated = false,
                Left = 300, Top = 300, Content = _root
            };

            // A click target we own, so the "outside click" never touches the user's desktop.
            _target = new Window
            {
                WindowStyle = WindowStyle.None, Topmost = true, ShowInTaskbar = false,
                ResizeMode = ResizeMode.NoResize, ShowActivated = false,
                Left = 900, Top = 600, Width = 220, Height = 140,
                Background = (Brush)new BrushConverter().ConvertFromString("#FF303848"),
                Content = new TextBlock { Text = "click target", Foreground = Brushes.White,
                                          HorizontalAlignment = HorizontalAlignment.Center,
                                          VerticalAlignment = VerticalAlignment.Center }
            };

            _w.Loaded += delegate
            {
                _h = new WindowInteropHelper(_w).Handle;
                int ex = N.GetWindowLong(_h, N.GWL_EXSTYLE);
                N.SetWindowLong(_h, N.GWL_EXSTYLE, ex | N.WS_EX_TOOLWINDOW);
                _t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(650) };
                _t.Tick += delegate { Tick(); };
                _t.Start();
            };
            _w.Show();
            _target.Show();
        }

        static void Tick()
        {
            if (_vi < 0) { _vi = 0; _phase = 0; }
            switch (_phase)
            {
                case 0: Setup(); _phase++; break;
                case 1: Probe(); _phase++; break;
                case 2: N.ClickAt(1010, 670, false); _phase++; break;   // click our own target window
                case 3:
                    Verdict();
                    _phase = 0; _vi++;
                    if (_vi >= _vars.Count) Finish();
                    break;
            }
        }

        static void Setup()
        {
            Variant v = _vars[_vi];

            int ex = N.GetWindowLong(_h, N.GWL_EXSTYLE);
            if (v.NoActivate) ex |= N.WS_EX_NOACTIVATE; else ex &= ~N.WS_EX_NOACTIVATE;
            N.SetWindowLong(_h, N.GWL_EXSTYLE, ex);

            _menu = new ContextMenu();
            var mi = new MenuItem { Header = "Toggle me", IsCheckable = true, StaysOpenOnClick = true };
            _menu.Items.Add(mi);
            _menu.Items.Add(new MenuItem { Header = "Another item" });
            _menu.PlacementTarget = _root;
            _root.ContextMenu = _menu;

            _sfgOk = false;
            if (v.ForceForeground)
            {
                _sfgOk = N.SetForegroundWindow(_h);
                if (v.PostNull) N.PostMessage(_h, N.WM_NULL, IntPtr.Zero, IntPtr.Zero);
            }

            if (v.RealRightClick) N.ClickAt(360, 320, true);   // inside the widget
            else _menu.IsOpen = true;
        }

        static void Probe()
        {
            _opened = _menu.IsOpen;
            _fgIsUs = N.GetForegroundWindow() == _h;
            _captured = Mouse.Captured == null ? "null" : Mouse.Captured.GetType().Name;
        }

        static void Verdict()
        {
            bool still = _menu.IsOpen;
            Variant v = _vars[_vi];
            L("--- " + v.Name + " ---");
            L("   SetForegroundWindow returned : " + (v.ForceForeground ? YN(_sfgOk) : "n/a"));
            L("   menu opened                  : " + YN(_opened));
            L("   window was foreground        : " + YN(_fgIsUs));
            L("   Mouse.Captured               : " + _captured);
            L("   still open after outside click : " + YN(still) +
              "   ->  " + (still ? "DISMISSAL BROKEN" : "dismissal WORKS"));
            L("");
            _menu.IsOpen = false;
            _root.ContextMenu = null;
        }

        static void Finish()
        {
            _t.Stop();
            L("=== end ===");
            try { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "report2.txt"), _log.ToString()); }
            catch (Exception e) { Console.WriteLine("write failed: " + e.Message); }
            try { _w.Close(); _target.Close(); } catch { }
            Application.Current.Shutdown();
        }
    }
}
