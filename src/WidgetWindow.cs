// The widget window.
//
// The window-management choices here are the ones the spikes in docs/SPIKES.md settled;
// several look arbitrary until you read why:
//
//   * ShowInTaskbar stays TRUE and WS_EX_TOOLWINDOW does the hiding. Setting it false makes
//     WPF create a hidden owner window with no WS_EX_TOPMOST, and an owned window's Z-band
//     follows its owner's - measured GW_OWNER=721118, owner EXSTYLE=0x100.
//   * No WS_EX_NOACTIVATE. WPF menu dismissal is capture-based and only a foreground window
//     gets full capture, so the style would leave the settings menu permanently open.
//     WM_MOUSEACTIVATE is answered selectively instead: left button never activates (drag
//     must not steal focus from an editor), everything else does.
//   * Resizing is NOT the OS resize loop. ResizeMode=NoResize strips WS_THICKFRAME
//     (measured STYLE=0x16080000), so HTBOTTOMRIGHT is a no-op. A grip captures the mouse
//     and the scale is applied on CompositionTarget.Rendering from an ABSOLUTE screen-space
//     mapping - applying it inside a drag event instead is unstable, because resizing moves
//     the grip under the cursor and re-triggers the event.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace Vibespan
{
    public class WidgetWindow : Window
    {
        public Cfg Config;

        Border _root;
        ContentControl _slot;
        Border _grip;
        ScaleTransform _scale;
        ContentControl _logoHost;
        Grid _shell;
        ColumnDefinition _logoCol;

        IntPtr _hwnd = IntPtr.Zero;
        HwndSourceHook _hook;                 // kept in a field: a collected delegate crashes

        Snapshot _snap;
        string _lastError;
        DateTime _lastOk = DateTime.MinValue;
        bool _hiddenForFullScreen;

        /// <summary>The hover text, so the tray can show exactly what the widget shows.</summary>
        public string CurrentTooltip { get; private set; }
        public int CurrentTooltipMetaFrom { get; private set; }
        bool _positionSettled;

        DispatcherTimer _poll, _tick, _feedWatch;
        int _currentIntervalSeconds;
        DateTime _feedStamp = DateTime.MinValue;

        public Alerts Alerts;
        public event Action MenuNeedsRebuild;
        public event Action<Snapshot> SnapshotUpdated;

        static Strings L { get { return I18n.T; } }

        public WidgetWindow(Cfg cfg)
        {
            Config = cfg;
            I18n.Use(cfg.Lang);
            Config.Lang = L.Code;

            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = true;              // WS_EX_TOOLWINDOW hides it; see header
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.WidthAndHeight;
            ShowActivated = false;
            Title = "Vibespan";
            UseLayoutRounding = true;
            Opacity = cfg.ContentOpacity;

            BuildChrome();
            Alerts = new Alerts(this);

            Loaded += delegate { OnLoadedOnce(); };
            MouseLeftButtonDown += OnBodyMouseDown;
        }

        // ---------- chrome ----------
        void BuildChrome()
        {
            _scale = new ScaleTransform(Config.Scale, Config.Scale);

            _logoHost = new ContentControl
            {
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center
            };
            _slot = new ContentControl { VerticalAlignment = VerticalAlignment.Center };

            _shell = new Grid();
            _logoCol = new ColumnDefinition { Width = new GridLength(Gauge.LogoColumn) };
            _shell.ColumnDefinitions.Add(_logoCol);
            _shell.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(_logoHost, 0);
            Grid.SetColumn(_slot, 1);
            _shell.Children.Add(_logoHost);
            _shell.Children.Add(_slot);

            // The grip must sit inside the opaque border: a layered window hit-tests from the
            // alpha channel before the wndproc, so anything on alpha 0 is not clickable.
            _grip = new Border
            {
                Width = 9, Height = 9,
                Background = Theme.B("#01000000"),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Cursor = Cursors.SizeNWSE,
                Margin = new Thickness(0, 0, -4, -1)
            };
            _grip.MouseLeftButtonDown += GripDown;
            _grip.MouseLeftButtonUp += GripUp;

            var outer = new Grid();
            outer.Children.Add(_shell);
            outer.Children.Add(_grip);

            _root = new Border
            {
                CornerRadius = new CornerRadius(7),
                Padding = new Thickness(8, 3, 8, 3),
                BorderThickness = new Thickness(1),
                Child = outer
            };
            _root.LayoutTransform = _scale;
            Content = _root;

            ApplyTheme();
        }

        public void ApplyTheme()
        {
            StylePreset st = Styles.For(Config);

            if (Config.IsHairline)
            {
                // No plate, no padding, no radius, no mark: in hairline mode the only thing
                // that should be visible on screen is the lines themselves.
                _root.Background = Brushes.Transparent;
                _root.BorderBrush = Brushes.Transparent;
                _root.BorderThickness = new Thickness(0);
                _root.CornerRadius = new CornerRadius(0);
                _root.Padding = new Thickness(0);
                _logoHost.Content = null;
                _logoCol.Width = new GridLength(0);
                _grip.Visibility = Visibility.Collapsed;

                // The content column is Auto in widget mode, which sizes to the rows. A strip
                // has to fill the monitor instead, so it becomes Star and the slot stretches -
                // otherwise every line renders at its 2px minimum and the strip looks broken.
                _shell.ColumnDefinitions[1].Width = new GridLength(1, GridUnitType.Star);
                _slot.HorizontalAlignment = HorizontalAlignment.Stretch;
                _slot.VerticalAlignment = VerticalAlignment.Stretch;

                // Neutralise the zoom. The strip's size is already computed in device pixels,
                // with thickness multiplied by Scale explicitly - leaving the LayoutTransform
                // on would scale the pinned width too, so a widget previously resized to 1.55x
                // produced a 2978px line on a 1920px monitor, hanging a thousand pixels off
                // the side of the screen.
                _scale.ScaleX = 1.0; _scale.ScaleY = 1.0;

                Opacity = Config.ContentOpacity;
                TextOptions.SetTextFormattingMode(_root, TextFormattingMode.Ideal);
                return;
            }

            _root.BorderThickness = new Thickness(1);
            _grip.Visibility = Visibility.Visible;
            _scale.ScaleX = Config.Scale; _scale.ScaleY = Config.Scale;
            _shell.ColumnDefinitions[1].Width = GridLength.Auto;
            _slot.HorizontalAlignment = HorizontalAlignment.Stretch;
            _slot.VerticalAlignment = VerticalAlignment.Center;
            _root.Background = Theme.BackgroundBrush(Config);
            _root.BorderBrush = Config.ShowBorder ? Theme.Brush_(Config, Theme.Border) : Brushes.Transparent;
            _root.CornerRadius = new CornerRadius(st.Radius);
            _root.Padding = new Thickness(st.PadX, st.PadY, st.PadX, st.PadY);

            // The mark is part of the style, and ShowLogo can switch it off entirely.
            bool wantMark = Config.ShowLogo && st.Mark != "none";
            double markW = wantMark ? Gauge.MarkColumnWidth(Config) : 0;
            _logoHost.Content = wantMark ? Gauge.Mark(Config) : null;
            // Stretch so a rail can fill whatever the rows actually come to; the asterisk and
            // dot centre themselves inside it.
            _logoHost.VerticalAlignment = VerticalAlignment.Stretch;
            _logoCol.Width = new GridLength(markW);

            // One font for the whole widget - per-element font pickers would be a settings
            // window's job, not a menu's. Border is not a Control, so set the inherited
            // attached property rather than a FontFamily member; it flows to every TextBlock.
            System.Windows.Documents.TextElement.SetFontFamily(_root, FontChoices.Resolve(Config));

            Opacity = Config.ContentOpacity;
            // Display mode is crisper for small text at exactly 1.0, but it assumes the integer
            // pixel grid and looks wrong at fractional scale, where Ideal degrades gracefully.
            TextOptions.SetTextFormattingMode(_root,
                Math.Abs(Config.Scale - 1.0) < 0.001 ? TextFormattingMode.Display : TextFormattingMode.Ideal);
        }

        // ---------- startup ----------
        void OnLoadedOnce()
        {
            _hwnd = new WindowInteropHelper(this).Handle;

            // WS_EX_TOOLWINDOW alone is not enough. ShowInTaskbar=true - which is what avoids
            // the hidden owner window that sinks Z-order - also sets WS_EX_APPWINDOW, and
            // APPWINDOW *forces* a taskbar button even on a tool window. Both were set, so the
            // widget got a taskbar button it has no use for. Strip it.
            Native.AddExStyle(_hwnd, Native.WS_EX_TOOLWINDOW);
            Native.RemoveExStyle(_hwnd, Native.WS_EX_APPWINDOW);
            // The shell only re-reads these flags on a visibility change, so the button does
            // not disappear until the window is hidden and shown again.
            Native.ShowWindow(_hwnd, Native.SW_HIDE);
            Native.ShowWindow(_hwnd, Native.SW_SHOWNOACTIVATE);

            ApplyClickThrough();

            HwndSource src = HwndSource.FromHwnd(_hwnd);
            _hook = new HwndSourceHook(WndProc);
            src.AddHook(_hook);

            Log.Write("started; uiAccess=" + (Native.HasUiAccess() ? "yes" : "NO (window will sit under the taskbar)"));

            ApplyPosition();
            Native.ReassertTopmost(_hwnd);

            Redraw();
            Refresh();

            _currentIntervalSeconds = Config.PollSeconds;
            _poll = new DispatcherTimer { Interval = TimeSpan.FromSeconds(_currentIntervalSeconds) };
            _poll.Tick += delegate { Refresh(); };
            _poll.Start();

            // Redraw so the countdowns stay alive without re-fetching.
            _tick = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            _tick.Tick += delegate { if (_snap != null) Redraw(); };
            _tick.Start();


            // The last position is written on drag, but a session that never drags would
            // otherwise lose a position set some other way. Persist on the way out too.
            Closing += delegate { try { if (!Config.IsHairline) SavePosition(); } catch { } };

            _feedWatch = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _feedWatch.Tick += delegate { PollFeedFile(); };
            _feedWatch.Start();
        }

        // ---------- wndproc ----------
        IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wp, IntPtr lp, ref bool handled)
        {
            if (msg == Native.WM_MOUSEACTIVATE)
            {
                int trigger = ((int)lp >> 16) & 0xFFFF;
                handled = true;
                // Left button: never activate, so dragging the widget does not pull focus out
                // of whatever the user is working in. Anything else (right-click) activates,
                // which is what makes the context menu dismissable.
                return new IntPtr(trigger == Native.WM_LBUTTONDOWN ? Native.MA_NOACTIVATE : Native.MA_ACTIVATE);
            }
            if (msg == Native.WM_DISPLAYCHANGE)
            {
                Dispatcher.BeginInvoke(new Action(delegate { ApplyPosition(); }), DispatcherPriority.Background);
            }
            return IntPtr.Zero;
        }

        // ---------- position ----------
        static System.Drawing.Rectangle[] Screens()
        {
            var list = new List<System.Drawing.Rectangle>();
            foreach (System.Windows.Forms.Screen s in System.Windows.Forms.Screen.AllScreens)
                list.Add(s.Bounds);
            return list.ToArray();
        }

        static bool RectIsVisible(int x, int y, int w, int h)
        {
            // Require a decent chunk on-screen, not merely a touching corner.
            var want = new System.Drawing.Rectangle(x, y, Math.Max(1, w), Math.Max(1, h));
            foreach (System.Drawing.Rectangle s in Screens())
            {
                System.Drawing.Rectangle i = System.Drawing.Rectangle.Intersect(s, want);
                if (i.Width >= Math.Min(40, want.Width) && i.Height >= Math.Min(20, want.Height)) return true;
            }
            return false;
        }

        void CurrentRect(out Native.RECT r)
        {
            if (!Native.GetWindowRect(_hwnd, out r)) r = new Native.RECT();
        }

        /// <summary>
        /// Restore the saved position, or fall back to the bottom-left of the primary screen.
        /// Everything here is physical pixels: WPF's Left/Top are converted using the CURRENT
        /// monitor's scale factor, which makes them unreliable across a multi-monitor desktop.
        /// </summary>
        /// <summary>
        /// Move to a point in physical desktop pixels, telling BOTH WPF and the window manager.
        ///
        /// SetWindowPos alone is not enough. WPF keeps its own Left/Top, and the SizeToContent
        /// resize that follows the first render repositions the window from those stale values -
        /// which silently undid every restore and left the widget at WPF's default placement.
        /// Assigning Left/Top makes WPF authoritative so the resize grows from the right corner.
        /// </summary>
        void MoveTo(int deviceX, int deviceY)
        {
            double dx = deviceX, dy = deviceY;
            var src = PresentationSource.FromVisual(this) as HwndSource;
            if (src != null && src.CompositionTarget != null)
            {
                Point p = src.CompositionTarget.TransformFromDevice.Transform(new Point(deviceX, deviceY));
                dx = p.X; dy = p.Y;
            }
            Left = dx; Top = dy;
            Native.SetWindowPos(_hwnd, IntPtr.Zero, deviceX, deviceY, 0, 0, Native.SWP_MOVE_ONLY);
        }

        public void ApplyPosition()
        {
            if (Config.IsHairline) { ApplyHairlineGeometry(); return; }
            UpdateLayout();
            Native.RECT r; CurrentRect(out r);
            int w = r.Width > 0 ? r.Width : 200, h = r.Height > 0 ? r.Height : 44;

            if (!double.IsNaN(Config.X) && !double.IsNaN(Config.Y))
            {
                int x = (int)Config.X, y = (int)Config.Y;

                // Prefer the monitor it was actually left on. Coordinates alone are ambiguous
                // after a display rearrangement: the same point can land on a different screen,
                // or on none. If that monitor is still attached, keep the widget on it.
                System.Windows.Forms.Screen saved = null;
                if (!string.IsNullOrEmpty(Config.Monitor))
                {
                    foreach (System.Windows.Forms.Screen sc in System.Windows.Forms.Screen.AllScreens)
                        if (sc.DeviceName == Config.Monitor) { saved = sc; break; }
                }
                if (saved != null)
                {
                    // Nudge back inside if the resolution shrank while it was away.
                    System.Drawing.Rectangle b = saved.Bounds;
                    if (x + w > b.Right) x = b.Right - w;
                    if (y + h > b.Bottom) y = b.Bottom - h;
                    if (x < b.Left) x = b.Left;
                    if (y < b.Top) y = b.Top;
                    MoveTo(x, y);
                    return;
                }
                if (RectIsVisible(x, y, w, h)) { MoveTo(x, y); return; }
            }

            MoveToDefaultCorner();
        }

        /// <summary>
        /// Lay the window flat against one edge of the monitor it sits on.
        ///
        /// The strip's size is pinned on the CONTENT and SizeToContent sizes the window to it -
        /// the same direction of control the widget mode already uses. Driving it the other way
        /// does not work: with SizeToContent=Manual, an assigned Window.Width of 1920 left
        /// ActualWidth reporting 136, so the content went on being laid out against the old
        /// widget-sized slot, star-sized lanes divided up 136px, and a 61% reading drew as 4%.
        /// SetWindowPos moves the window afterwards; it never sizes it.
        ///
        /// The bug only surfaced with a single visible metric: with two, the strip's height
        /// changes between layout passes and the resulting resize resynchronised things by luck.
        /// </summary>
        void ApplyHairlineGeometry()
        {
            if (_hwnd == IntPtr.Zero) return;

            System.Drawing.Rectangle b = TargetScreenBounds();
            int rows = VisibleRowCount();
            if (rows < 1) rows = 1;
            int thick = (int)Math.Max(1, Math.Round(Config.HairlineThickness * Config.Scale)) * rows;

            int x, y, w, h;
            switch (Config.HairlineEdge)
            {
                case "top":   x = b.Left;           y = b.Top;            w = b.Width; h = thick;    break;
                case "left":  x = b.Left;           y = b.Top;            w = thick;   h = b.Height; break;
                case "right": x = b.Right - thick;  y = b.Top;            w = thick;   h = b.Height; break;
                default:      x = b.Left;           y = b.Bottom - thick; w = b.Width; h = thick;    break;
            }

            // Device pixels -> DIPs, so the strip still matches the monitor above 100% scaling.
            double dipW = w, dipH = h;
            var src = PresentationSource.FromVisual(this) as HwndSource;
            if (src != null && src.CompositionTarget != null)
            {
                Matrix toDip = src.CompositionTarget.TransformFromDevice;
                Point size = toDip.Transform(new Point(w, h));
                dipW = size.X; dipH = size.Y;
            }

            SizeToContent = SizeToContent.WidthAndHeight;
            _root.Width = dipW;
            _root.Height = dipH;
            _root.UpdateLayout();

            Native.SetWindowPos(_hwnd, IntPtr.Zero, x, y, 0, 0, Native.SWP_MOVE_ONLY);
        }

        int VisibleRowCount()
        {
            if (_snap == null) return 1;
            int n = 0;
            foreach (Bucket bu in _snap.Buckets)
            {
                RowCfg r = Config.Row(bu.Key);
                if (r != null && r.Visible && bu.HasPercent) n++;
            }
            return n;
        }

        /// <summary>The monitor the widget last lived on, falling back to the primary.</summary>
        System.Drawing.Rectangle TargetScreenBounds()
        {
            if (!string.IsNullOrEmpty(Config.Monitor))
            {
                foreach (System.Windows.Forms.Screen sc in System.Windows.Forms.Screen.AllScreens)
                    if (sc.DeviceName == Config.Monitor) return sc.Bounds;
            }
            if (!double.IsNaN(Config.X) && !double.IsNaN(Config.Y))
            {
                System.Windows.Forms.Screen sc = System.Windows.Forms.Screen.FromPoint(
                    new System.Drawing.Point((int)Config.X, (int)Config.Y));
                if (sc != null) return sc.Bounds;
            }
            return System.Windows.Forms.Screen.PrimaryScreen.Bounds;
        }

        /// <summary>Switch display mode, restoring the sizing model the other mode needs.</summary>
        public void SetMode(string mode)
        {
            Config.Mode = mode;
            if (Config.IsHairline)
            {
                ApplyTheme();
                Redraw();
                ApplyHairlineGeometry();
            }
            else
            {
                // Release the pin hairline mode put on the content, or the box stays
                // strip-shaped.
                _root.Width = double.NaN;
                _root.Height = double.NaN;
                SizeToContent = SizeToContent.WidthAndHeight;
                ApplyTheme();
                Redraw();
                UpdateLayout();
                ApplyPosition();
            }
            Config.Save();
        }

        public void MoveToDefaultCorner()
        {
            UpdateLayout();
            Native.RECT r; CurrentRect(out r);
            int w = r.Width > 0 ? r.Width : 200, h = r.Height > 0 ? r.Height : 44;
            System.Drawing.Rectangle wa = System.Windows.Forms.Screen.PrimaryScreen.WorkingArea;
            MoveTo(wa.Left + 8, wa.Bottom - h - 4);
            SavePosition();
        }

        /// <summary>Rescue path from the tray, for a widget dragged onto a monitor that is now gone.</summary>
        public void BringToCentre()
        {
            UpdateLayout();
            Native.RECT r; CurrentRect(out r);
            System.Drawing.Rectangle wa = System.Windows.Forms.Screen.PrimaryScreen.WorkingArea;
            MoveTo(wa.Left + (wa.Width - r.Width) / 2, wa.Top + (wa.Height - r.Height) / 2);
            Visibility = Visibility.Visible;
            _hiddenForFullScreen = false;
            SavePosition();
        }

        public void SavePosition()
        {
            Native.RECT r; CurrentRect(out r);
            if (r.Width == 0) return;
            Config.X = r.Left; Config.Y = r.Top;
            System.Windows.Forms.Screen s = System.Windows.Forms.Screen.FromPoint(
                new System.Drawing.Point(r.Left + r.Width / 2, r.Top + r.Height / 2));
            Config.Monitor = s != null ? s.DeviceName : null;
            Config.Save();
        }

        // ---------- drag to move ----------
        void OnBodyMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (_sizing) return;
            if (Config.IsHairline) return;                 // the strip is pinned to an edge
            if (e.OriginalSource == _grip) return;         // the grip runs its own gesture
            try { DragMove(); SavePosition(); } catch { }  // throws if the button is already up
        }

        // ---------- resize ----------
        bool _sizing;
        Native.POINT _anchor;
        double _startScale;
        static readonly double[] Presets = { 0.75, 1.0, 1.25, 1.5, 2.0 };
        const double RefPx = 220.0, MinScale = 0.6, MaxScale = 3.0;

        void GripDown(object sender, MouseButtonEventArgs e)
        {
            _sizing = true;
            _startScale = Config.Scale;
            Native.GetCursorPos(out _anchor);
            _grip.CaptureMouse();
            CompositionTarget.Rendering += OnSizeFrame;
            e.Handled = true;
        }

        void GripUp(object sender, MouseButtonEventArgs e)
        {
            if (!_sizing) return;
            EndSizing();
            e.Handled = true;
        }

        void EndSizing()
        {
            _sizing = false;
            CompositionTarget.Rendering -= OnSizeFrame;
            _grip.ReleaseMouseCapture();

            // Magnet-snap so a dragged widget lands exactly on a menu preset and the radio
            // item lights up - drag and the Size menu then feel like one feature, not two.
            foreach (double p in Presets)
                if (Math.Abs(Config.Scale - p) <= 0.04) { SetScale(p); break; }

            ApplyTheme();          // text formatting mode depends on whether scale == 1.0
            SavePosition();
            Config.Save();
            if (MenuNeedsRebuild != null) MenuNeedsRebuild();
        }

        void OnSizeFrame(object sender, EventArgs e)
        {
            Native.POINT p;
            if (!Native.GetCursorPos(out p)) return;
            double dx = p.X - _anchor.X, dy = p.Y - _anchor.Y;
            double target = _startScale * (1 + (dx + dy) / RefPx);
            if (target < MinScale) target = MinScale;
            if (target > MaxScale) target = MaxScale;
            if (Math.Abs(target - Config.Scale) < 0.002) return;   // idempotent: this kills the loop
            SetScale(target);
        }

        /// <summary>
        /// Apply a scale, keeping the corner nearest the screen edge pinned. SizeToContent grows
        /// right and down from a fixed origin, so without this a bottom-docked widget walks off
        /// the bottom of the screen as it grows.
        /// </summary>
        public void SetScale(double s)
        {
            // In hairline mode Scale drives the line's thickness, not a zoom of the content.
            if (Config.IsHairline)
            {
                Config.Scale = s;
                ApplyHairlineGeometry();
                return;
            }

            Native.RECT before; CurrentRect(out before);
            Native.RECT mon;
            bool haveMon = Native.TryMonitorRect(_hwnd, out mon);
            bool anchorRight = haveMon && (before.Left + before.Width / 2) > (mon.Left + mon.Width / 2);
            bool anchorBottom = haveMon && (before.Top + before.Height / 2) > (mon.Top + mon.Height / 2);

            Config.Scale = s;
            _scale.ScaleX = s; _scale.ScaleY = s;
            _root.InvalidateMeasure();
            UpdateLayout();

            Native.RECT after; CurrentRect(out after);
            int left = anchorRight ? before.Right - after.Width : before.Left;
            int top = anchorBottom ? before.Bottom - after.Height : before.Top;
            Native.SetWindowPos(_hwnd, IntPtr.Zero, left, top, 0, 0, Native.SWP_MOVE_ONLY);
        }

        public void SetScaleAndSave(double s)
        {
            SetScale(s);
            ApplyTheme();
            SavePosition();
            Config.Save();
        }

        // ---------- click-through ----------
        public void ApplyClickThrough()
        {
            if (_hwnd == IntPtr.Zero) return;
            if (Config.ClickThrough) Native.AddExStyle(_hwnd, Native.WS_EX_TRANSPARENT);
            else Native.RemoveExStyle(_hwnd, Native.WS_EX_TRANSPARENT);
        }

        // ---------- topmost / full screen ----------
        /// <param name="mayRestack">
        /// False on the idle heartbeat. The heartbeat exists to re-check VISIBILITY - a
        /// full-screen app can change its own rect without any foreground change - and that is a
        /// read-only question. Re-stacking on a timer is what turned a rare, event-driven repair
        /// into a write every three seconds forever.
        /// </param>
        public void EvaluateTopmost(bool mayRestack)
        {
            if (_hwnd == IntPtr.Zero) return;

            bool onOurMonitor;
            bool fullScreen = FullScreenApp(out onOurMonitor);

            // Visibility and Z-order are two different questions and they do NOT have the same
            // answer. Hiding is about what the user can see, so it is per-monitor: a game on the
            // other display is no reason to blank a widget parked over here.
            bool hide = Config.HideFullScreen && fullScreen && onOurMonitor;
            if (hide != _hiddenForFullScreen)
            {
                _hiddenForFullScreen = hide;
                Visibility = hide ? Visibility.Hidden : Visibility.Visible;
            }

            // Z-order is not per-monitor - it is one desktop-wide band - so the monitor test must
            // not gate it. Suppressing the re-assert only while HIDDEN meant a full-screen game on
            // the second monitor left this running every 3 seconds, and each pass demoted and
            // re-promoted a uiAccess window across the whole desktop. That is what froze the game:
            // the process stayed alive while its presentation deadlocked on the occlusion change.
            if (fullScreen) return;
            if (!mayRestack) return;

            Native.ReassertTopmost(_hwnd);
        }

        /// <summary>
        /// True when a full-screen application is running, on ANY monitor.
        /// <paramref name="onOurMonitor"/> reports separately whether it shares the widget's
        /// display, because that answers the hide question while this answers the Z-order one.
        /// </summary>
        bool FullScreenApp(out bool onOurMonitor)
        {
            onOurMonitor = false;

            IntPtr fg = Native.GetForegroundWindow();
            if (fg == IntPtr.Zero || fg == _hwnd) return false;

            // The desktop and the shell permanently span the screen.
            string cls = Native.ClassOf(fg);
            if (cls == "Progman" || cls == "WorkerW" || cls == "Shell_TrayWnd" ||
                cls == "Shell_SecondaryTrayWnd" || cls == "Windows.UI.Core.CoreWindow") return false;

            // rcMonitor, not rcWork: a merely maximized window stops at the taskbar and must
            // not count. This is the check that catches borderless full screen, which
            // SHQueryUserNotificationState misses.
            bool covers = false;
            Native.RECT r, mon;
            if (Native.GetWindowRect(fg, out r) && Native.TryMonitorRect(fg, out mon))
                covers = r.Left <= mon.Left && r.Top <= mon.Top &&
                         r.Right >= mon.Right && r.Bottom >= mon.Bottom;

            if (!covers && !D3DFullScreenActive()) return false;

            // Compare monitor handles, not rectangles: two displays can share coordinates in
            // odd multi-monitor arrangements, and the handle is what Windows itself keys on.
            IntPtr fgMon = Native.MonitorFromWindow(fg, Native.MONITOR_DEFAULTTONEAREST);
            IntPtr myMon = Native.MonitorFromWindow(_hwnd, Native.MONITOR_DEFAULTTONEAREST);
            onOurMonitor = fgMon != IntPtr.Zero && fgMon == myMon;
            return true;
        }

        /// <summary>
        /// Exclusive-mode D3D / presentation. The signal is system-wide with no monitor attached,
        /// so it can establish THAT a game is running but never WHERE.
        /// </summary>
        static bool D3DFullScreenActive()
        {
            int state;
            if (Native.SHQueryUserNotificationState(out state) != 0) return false;
            return state == Native.QUNS_RUNNING_D3D_FULL_SCREEN ||
                   state == Native.QUNS_PRESENTATION_MODE;
        }

        // ---------- data ----------
        public void Refresh()
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                UsageResult res = Api.Fetch();
                Dispatcher.BeginInvoke(new Action(delegate { ApplyResult(res); }));
            });
        }

        void ApplyResult(UsageResult res)
        {
            if (res.Ok)
            {
                if (_lastError != null) Log.Write("refresh recovered");
                MergeSnapshot(res.Snapshot);
                _lastError = null;
                _lastOk = DateTime.Now;
                SetInterval(Config.PollSeconds);
            }
            else
            {
                Log.Write("refresh failed: " + res.Error);
                _lastError = res.Error;
                // Back OFF on failure. The predecessor tightened to one minute here, which on
                // an endpoint that 429s without a Retry-After makes the situation worse.
                int cap = res.RateLimited ? 900 : 600;
                SetInterval(Math.Min(cap, Math.Max(Config.PollSeconds, _currentIntervalSeconds * 2)));
            }
            Redraw();
        }

        /// <summary>Re-apply the configured interval after the user changes it in the menu.</summary>
        public void ApplyPollInterval()
        {
            SetInterval(Config.PollSeconds);
        }

        void SetInterval(int seconds)
        {
            if (_poll == null || seconds == _currentIntervalSeconds) return;
            _currentIntervalSeconds = seconds;
            _poll.Interval = TimeSpan.FromSeconds(seconds);
            Log.Write("poll interval -> " + seconds + "s");
        }

        /// <summary>Adopt a new snapshot and make sure every discovered bucket has a row.</summary>
        void MergeSnapshot(Snapshot s)
        {
            _snap = s;
            bool added = false;
            foreach (Bucket b in s.Buckets)
                if (Config.Row(b.Key) == null) { Config.EnsureRow(b.Key); added = true; }
            if (added)
            {
                ReorderSnapshot();
                Config.Save();
                if (MenuNeedsRebuild != null) MenuNeedsRebuild();
            }
            else ReorderSnapshot();

            if (Alerts != null) Alerts.Evaluate(s, Config);
            if (SnapshotUpdated != null) SnapshotUpdated(s);
        }

        // ---------- live feed ----------
        void PollFeedFile()
        {
            if (!Config.UseLiveFeed) return;
            try
            {
                if (!File.Exists(Cfg.FeedPath)) return;
                DateTime stamp = File.GetLastWriteTimeUtc(Cfg.FeedPath);
                if (stamp <= _feedStamp) return;
                _feedStamp = stamp;

                JNode j = JsonReader.Parse(File.ReadAllText(Cfg.FeedPath));
                Snapshot feed = Snapshot.FromFeed(j["rate_limits"]);
                if (feed.Buckets.Count == 0) return;

                // The feed only carries session and weekly. Keep everything the poll found
                // for the other buckets, so enabling the feed never loses rows.
                if (_snap != null)
                {
                    foreach (Bucket old in _snap.Buckets)
                        if (feed.Find(old.Key) == null) feed.Buckets.Add(old);
                }
                MergeSnapshot(feed);
                _lastError = null;
                _lastOk = DateTime.Now;
                Redraw();
            }
            catch { }
        }

        // ---------- render ----------
        public void Redraw()
        {
            Gauge.Rendered r;
            if (_snap != null)
                r = Config.IsHairline
                    ? Gauge.BuildHairline(Config, _snap, _lastError, _lastOk)
                    : Gauge.Build(Config, _snap, _lastError, _lastOk);
            else if (Config.IsHairline)
                r = new Gauge.Rendered { Content = new Grid(), Tooltip = L.Loading };
            else
                r = Gauge.BuildMessage(Config,
                        _lastError == null ? L.Loading : string.Format(L.Offline, Fmt.ErrorText(_lastError)));

            _slot.Content = r.Content;
            // A raw string here gets WPF's stock tooltip: small, light, system-styled, and
            // nothing like the rest of the widget. The card is the same one the tray shows.
            //
            // Rebuilt only when the text changes. Redraw runs on a timer, and swapping in a new
            // ToolTip instance while one is open closes it - the card would blink out from under
            // the pointer once a minute.
            CurrentTooltipMetaFrom = r.TooltipMetaFrom;
            if (r.Tooltip != CurrentTooltip || _root.ToolTip == null)
            {
                CurrentTooltip = r.Tooltip;
                _root.ToolTip = HoverCard.Tip(r.Tooltip, r.TooltipMetaFrom);
                // The stock 5 s auto-hide is short for three lines of numbers.
                ToolTipService.SetShowDuration(_root, 20000);
                ToolTipService.SetInitialShowDelay(_root, 350);
            }

            // Past two missed cycles the numbers on screen mean nothing, so make the stall
            // visible rather than letting a frozen value pass for a fresh one.
            if (_lastError != null && _lastOk != DateTime.MinValue)
            {
                TimeSpan age = DateTime.Now - _lastOk;
                bool stale = age.TotalMinutes >= 12;
                _root.BorderBrush = Theme.B(stale ? "#CCE05252" : "#99E8A33D");
                _slot.Opacity = stale ? 0.45 : 1.0;
            }
            else
            {
                _root.BorderBrush = Config.ShowBorder && !Config.IsHairline
                    ? Theme.Brush_(Config, Theme.Border) : Brushes.Transparent;
                _slot.Opacity = 1.0;
            }

            // The strip's thickness is a function of how many metrics are visible, so ticking a
            // row on or off has to resize the window, not just repaint it.
            if (Config.IsHairline) { ApplyHairlineGeometry(); return; }

            // Re-apply the saved position once, after the first render with real data. The
            // first ApplyPosition runs against a placeholder whose height is not the final one,
            // so a bottom-anchored default corner would sit a few pixels off.
            if (!_positionSettled && _snap != null && _hwnd != IntPtr.Zero)
            {
                _positionSettled = true;
                UpdateLayout();
                ApplyPosition();
            }
        }

        /// <summary>Full rebuild after a settings change.</summary>
        public void RefreshAppearance()
        {
            _scale.ScaleX = Config.Scale; _scale.ScaleY = Config.Scale;
            ApplyTheme();
            ApplyClickThrough();
            Redraw();
            UpdateLayout();
            if (Config.IsHairline) ApplyHairlineGeometry();
        }

        public Snapshot CurrentSnapshot { get { return _snap; } }
        public IntPtr Handle { get { return _hwnd; } }
        public Border RootBorder { get { return _root; } }

        public void RaiseMenuRebuild()
        {
            if (MenuNeedsRebuild != null) MenuNeedsRebuild();
        }

        /// <summary>
        /// Re-sort the live snapshot into the configured row order. Render order follows the
        /// snapshot, so without this a "move up" would not visibly do anything until the next
        /// poll replaced the data.
        /// </summary>
        public void ReorderSnapshot()
        {
            if (_snap == null) return;
            var ordered = new List<Bucket>();
            foreach (RowCfg r in Config.Rows)
            {
                Bucket b = _snap.Find(r.Key);
                if (b != null) ordered.Add(b);
            }
            foreach (Bucket b in _snap.Buckets)          // anything not yet in config keeps its place
                if (!ordered.Contains(b)) ordered.Add(b);
            _snap.Buckets = ordered;
        }

        /// <summary>Bring the window to the foreground so a programmatically opened menu can dismiss.</summary>
        public void ForceForeground()
        {
            if (_hwnd == IntPtr.Zero) return;
            Native.SetForegroundWindow(_hwnd);
            Native.PostMessage(_hwnd, Native.WM_NULL, IntPtr.Zero, IntPtr.Zero);
        }
    }
}
