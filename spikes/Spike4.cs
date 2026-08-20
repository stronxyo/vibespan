// Spike 4 - Thumb-driven resize, the replacement for the refuted HTBOTTOMRIGHT route.
//
// Checks:
//   * a Thumb inside the OPAQUE border of a layered (AllowsTransparency) window
//     actually receives capture and drag events from real mouse input
//   * mapping raw SCREEN-pixel delta -> absolute scale is stable (no runaway).
//     The naive version uses e.HorizontalChange, which is expressed in the Thumb's own
//     LayoutTransform-scaled space, so growing the scale grows the reported delta and
//     the value diverges. This logs BOTH so the difference is visible.
//   * the HWND keeps tracking the scale during a live drag.
//
// Writes spikes\report4.txt.
using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace Spike4
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
        public const uint MOVE = 0x0001, LEFTDOWN = 0x0002, LEFTUP = 0x0004;
    }

    public static class Program
    {
        static StringBuilder _log = new StringBuilder();
        static void L(string s) { _log.Append(s).Append(Environment.NewLine); Console.WriteLine(s); }
        static string F(double d) { return d.ToString("0.000", CultureInfo.InvariantCulture); }

        static Window _w; static Border _root; static Thumb _grip; static ScaleTransform _scale;
        static IntPtr _h;
        const int WX = 400, WY = 400;
        const double REF_PX = 200.0, MIN = 0.6, MAX = 3.0;

        static double _startScale = 1.0; static N.POINT _anchor;
        static bool _dragging; static int _deltaEvents;
        static double _naiveAccum;          // what e.HorizontalChange would have produced
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
            L("=== vibespan spike 4 : Thumb resize ===");
            L("run at " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            L("REF_PX=" + REF_PX + "  clamp [" + MIN + "," + MAX + "]");
            L("");

            _scale = new ScaleTransform(1, 1);

            var rows = new StackPanel { Width = 166 };
            rows.Children.Add(new TextBlock { Text = "5h  4%   4h53", FontSize = 12, Foreground = Brushes.White, Height = 18 });
            rows.Children.Add(new TextBlock { Text = "7d 17%   2d 3h", FontSize = 12, Foreground = Brushes.White, Height = 18 });

            _grip = new Thumb
            {
                Width = 10, Height = 10,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Cursor = System.Windows.Input.Cursors.SizeNWSE,
                Opacity = 0.35
            };
            _grip.DragStarted += OnStart;
            _grip.DragDelta += OnDelta;
            _grip.DragCompleted += delegate { _dragging = false; };

            var grid = new Grid();
            grid.Children.Add(rows);
            grid.Children.Add(_grip);

            _root = new Border
            {
                // opaque: a layered window hit-tests from the alpha channel BEFORE the
                // wndproc, so anything interactive must not sit on alpha 0
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

        static void OnStart(object s, DragStartedEventArgs e)
        {
            _dragging = true; _deltaEvents = 0; _naiveAccum = _scale.ScaleX;
            _startScale = _scale.ScaleX;
            N.GetCursorPos(out _anchor);
            L("  DragStarted   anchor=(" + _anchor.X + "," + _anchor.Y + ")  startScale=" + F(_startScale));
        }

        static void OnDelta(object s, DragDeltaEventArgs e)
        {
            _deltaEvents++;

            // NAIVE: Thumb-space deltas, scaled by our own LayoutTransform -> diverges
            _naiveAccum = Math.Max(MIN, Math.Min(MAX,
                _naiveAccum * (1 + (e.HorizontalChange + e.VerticalChange) / REF_PX)));

            // CORRECT: raw screen pixels, mapped ABSOLUTELY from the drag-start anchor
            N.POINT p; N.GetCursorPos(out p);
            double dx = p.X - _anchor.X, dy = p.Y - _anchor.Y;
            double target = _startScale * (1 + (dx + dy) / REF_PX);
            if (target < MIN) target = MIN; if (target > MAX) target = MAX;

            _scale.ScaleX = target; _scale.ScaleY = target;
            _root.InvalidateMeasure();
        }

        static void Tick()
        {
            _step++;
            if (_step == 1) { N.SetCursorPos(WX + 180, WY + 40); return; }
            if (_step == 2)
            {
                // press on the grip - find it in screen space
                Point tl = _grip.PointToScreen(new Point(0, 0));
                int gx = (int)(tl.X + 5), gy = (int)(tl.Y + 5);
                L("  grip at screen (" + gx + "," + gy + ")");
                N.SetCursorPos(gx, gy);
                N.mouse_event(N.LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
                return;
            }
            if (_step >= 3 && _step <= 14)
            {
                // Absolute positioning only: MOUSEEVENTF_MOVE is relative and goes through
                // pointer ballistics, which amplifies the delta unpredictably and makes the
                // test meaningless. SetCursorPos generates a real WM_MOUSEMOVE.
                int n = _step - 2;
                int before = _deltaEvents;
                N.SetCursorPos(_anchor.X + n * 10, _anchor.Y + n * 4);

                N.POINT p; N.GetCursorPos(out p);
                N.RECT r; N.GetWindowRect(_h, out r);
                double dx = p.X - _anchor.X, dy = p.Y - _anchor.Y;
                double expect = _startScale * (1 + (dx + dy) / REF_PX);
                if (expect > MAX) expect = MAX; if (expect < MIN) expect = MIN;
                L("   move " + n.ToString("00") +
                  "  d=(" + dx + "," + dy + ")" +
                  "  expect=" + F(expect) +
                  "  actual=" + F(_scale.ScaleX) +
                  "  naive=" + F(_naiveAccum) +
                  "  HWND " + r.W + "x" + r.H +
                  "  deltaEvts+" + (_deltaEvents - before));
                return;
            }
            if (_step == 15)
            {
                N.mouse_event(N.LEFTUP, 0, 0, 0, UIntPtr.Zero);
                return;
            }
            if (_step == 16)
            {
                L("");
                L("  drag events received : " + _deltaEvents + (_deltaEvents > 0 ? "   -> Thumb DOES get capture on a layered window" : "   -> NO EVENTS: Thumb never captured"));
                L("  final scale (correct): " + F(_scale.ScaleX));
                L("  final scale (naive)  : " + F(_naiveAccum));
                L("  divergence           : " + F(Math.Abs(_naiveAccum - _scale.ScaleX)));
                L("");
                L("  snap test (nearest preset within 0.04):");
                double[] presets = { 0.75, 1.0, 1.25, 1.5, 2.0 };
                foreach (double v in new double[] { 0.77, 1.02, 1.31, 1.48, 1.97 })
                {
                    double best = v; foreach (double pz in presets) if (Math.Abs(pz - v) <= 0.04) best = pz;
                    L("     " + F(v) + " -> " + F(best));
                }
                Finish();
            }
        }

        static void Finish()
        {
            _t.Stop();
            L("=== end ===");
            try { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "report4.txt"), _log.ToString()); }
            catch (Exception e) { Console.WriteLine("write failed: " + e.Message); }
            try { _w.Close(); } catch { }
            Application.Current.Shutdown();
        }
    }
}
