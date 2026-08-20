// Spike 5 - the fix for the resize feedback loop found in spike 4.
//
// Spike 4 proved that applying the scale inside Thumb.DragDelta is unstable: resizing the
// window moves the grip under the cursor, which makes Thumb raise another DragDelta, which
// resizes again. 1,526 delta events fired for 12 cursor moves and the scale oscillated
// between the 0.6 and 3.0 clamps.
//
// The cure is to decouple the driver from the geometry it perturbs:
//   * the grip only captures the mouse and records an anchor
//   * the scale is applied on CompositionTarget.Rendering, reading GetCursorPos each frame
//   * mapping stays ABSOLUTE from the anchor, so it is idempotent - re-running it with an
//     unchanged cursor yields an unchanged scale, which is what kills the loop
//
// Writes spikes\report5.txt.
using System;
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

namespace Spike5
{
    public static class N
    {
        [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
        [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll")] public static extern void mouse_event(uint f, int dx, int dy, uint d, UIntPtr e);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
        [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B;
            public int W { get { return R - L; } } public int H { get { return B - T; } } }
        public const uint LEFTDOWN = 0x0002, LEFTUP = 0x0004;
    }

    public static class Program
    {
        static StringBuilder _log = new StringBuilder();
        static void L(string s) { _log.Append(s).Append(Environment.NewLine); Console.WriteLine(s); }
        static string F(double d) { return d.ToString("0.000", CultureInfo.InvariantCulture); }

        static Window _w; static Border _root, _grip; static ScaleTransform _scale; static IntPtr _h;
        const int WX = 400, WY = 400;
        const double REF_PX = 200.0, MIN = 0.6, MAX = 3.0;

        static bool _sizing; static N.POINT _anchor; static double _startScale;
        static int _frames, _applies;
        static int _step; static DispatcherTimer _t;

        [STAThread]
        public static void Main()
        {
            var app = new Application();
            app.Startup += delegate { Begin(); };
            app.Run();
        }

        static void Begin()
        {
            L("=== vibespan spike 5 : resize driven by a clock, not by DragDelta ===");
            L("run at " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            L("");

            _scale = new ScaleTransform(1, 1);
            var rows = new StackPanel { Width = 166 };
            rows.Children.Add(new TextBlock { Text = "5h  4%   4h53", FontSize = 12, Foreground = Brushes.White, Height = 18 });
            rows.Children.Add(new TextBlock { Text = "7d 17%   2d 3h", FontSize = 12, Foreground = Brushes.White, Height = 18 });

            _grip = new Border
            {
                Width = 10, Height = 10,
                Background = (Brush)new BrushConverter().ConvertFromString("#40FFFFFF"),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Cursor = Cursors.SizeNWSE
            };
            _grip.MouseLeftButtonDown += GripDown;
            _grip.MouseLeftButtonUp += GripUp;

            var grid = new Grid();
            grid.Children.Add(rows);
            grid.Children.Add(_grip);

            _root = new Border
            {
                Background = (Brush)new BrushConverter().ConvertFromString("#F21E2029"),
                Padding = new Thickness(8, 3, 8, 3),
                Child = grid
            };
            _root.LayoutTransform = _scale;

            _w = new Window
            {
                WindowStyle = WindowStyle.None, AllowsTransparency = true,
                Background = Brushes.Transparent, Topmost = true, ShowInTaskbar = true,
                ResizeMode = ResizeMode.NoResize, SizeToContent = SizeToContent.WidthAndHeight,
                ShowActivated = false, Left = WX, Top = WY, Content = _root
            };
            _w.Loaded += delegate
            {
                _h = new WindowInteropHelper(_w).Handle;
                _t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };
                _t.Tick += delegate { Tick(); };
                _t.Start();
            };
            _w.Show();
        }

        static void GripDown(object s, MouseButtonEventArgs e)
        {
            _sizing = true;
            _startScale = _scale.ScaleX;
            N.GetCursorPos(out _anchor);
            _grip.CaptureMouse();
            CompositionTarget.Rendering += OnFrame;
            e.Handled = true;                       // never let this reach the window drag handler
            L("  grip down   anchor=(" + _anchor.X + "," + _anchor.Y + ")  startScale=" + F(_startScale));
        }

        static void GripUp(object s, MouseButtonEventArgs e)
        {
            if (!_sizing) return;
            _sizing = false;
            CompositionTarget.Rendering -= OnFrame;
            _grip.ReleaseMouseCapture();
            e.Handled = true;
        }

        // Idempotent: same cursor position => same scale. That is what breaks the loop.
        static void OnFrame(object s, EventArgs e)
        {
            _frames++;
            N.POINT p; N.GetCursorPos(out p);
            double dx = p.X - _anchor.X, dy = p.Y - _anchor.Y;
            double target = _startScale * (1 + (dx + dy) / REF_PX);
            if (target < MIN) target = MIN; if (target > MAX) target = MAX;
            if (Math.Abs(target - _scale.ScaleX) < 0.001) return;
            _applies++;
            _scale.ScaleX = target; _scale.ScaleY = target;
            _root.InvalidateMeasure();
        }

        // Start the sizing state directly. Spike 4 already proved a grip on a layered window
        // receives real mouse input (1,526 events); the open question is STABILITY, so this
        // isolates it from input simulation.
        static void BeginSize()
        {
            _sizing = true;
            _startScale = _scale.ScaleX;
            N.GetCursorPos(out _anchor);
            CompositionTarget.Rendering += OnFrame;
            L("  begin size  anchor=(" + _anchor.X + "," + _anchor.Y + ")  startScale=" + F(_startScale));
        }
        static void EndSize()
        {
            _sizing = false;
            CompositionTarget.Rendering -= OnFrame;
        }

        static int _fb, _ab, _moveNo;
        static void Tick()
        {
            _step++;
            if (_step == 1) { N.SetCursorPos(WX + 100, WY + 30); return; }
            if (_step == 2) { BeginSize(); return; }

            // Alternate: even step moves the cursor, odd step measures a tick later so that
            // real render frames have elapsed. NEVER Thread.Sleep here - it blocks the
            // dispatcher and therefore CompositionTarget.Rendering itself.
            if (_step >= 3 && _step <= 22)
            {
                if ((_step % 2) == 1)
                {
                    _moveNo++;
                    _fb = _frames; _ab = _applies;
                    N.SetCursorPos(_anchor.X + _moveNo * 12, _anchor.Y + _moveNo * 5);
                }
                else
                {
                    N.POINT p; N.GetCursorPos(out p);
                    N.RECT r; N.GetWindowRect(_h, out r);
                    double dx = p.X - _anchor.X, dy = p.Y - _anchor.Y;
                    double expect = _startScale * (1 + (dx + dy) / REF_PX);
                    if (expect > MAX) expect = MAX; if (expect < MIN) expect = MIN;
                    bool ok = Math.Abs(expect - _scale.ScaleX) < 0.02;
                    L("   move " + _moveNo.ToString("00") +
                      "  d=(" + dx + "," + dy + ")" +
                      "  expect=" + F(expect) + "  actual=" + F(_scale.ScaleX) +
                      (ok ? "  MATCH" : "  ***MISMATCH***") +
                      "  HWND " + r.W + "x" + r.H +
                      "  frames+" + (_frames - _fb) + " applies+" + (_applies - _ab));
                }
                return;
            }
            if (_step == 23) { EndSize(); return; }
            if (_step == 24)
            {
                L("");
                L("  total frames  : " + _frames);
                L("  total applies : " + _applies + "   (should be ~1 per real cursor change, not thousands)");
                L("  final scale   : " + F(_scale.ScaleX));
                L("  capture released, still sizing? " + (_sizing ? "YES - BAD" : "no"));
                Finish();
            }
        }

        static void Finish()
        {
            _t.Stop();
            L("=== end ===");
            try { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "report5.txt"), _log.ToString()); }
            catch (Exception e) { Console.WriteLine("write failed: " + e.Message); }
            try { _w.Close(); } catch { }
            Application.Current.Shutdown();
        }
    }
}
