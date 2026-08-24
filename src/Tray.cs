// Tray icon: a colour-coded read-out, a second way into the menu, and the rescue path when
// the widget has been dragged onto a monitor that no longer exists.
//
// Two UIPI filters are not optional here. A uiAccess process launched by a member of
// Administrators runs at High integrity, and UIPI silently drops messages sent to it from
// medium-integrity Explorer. Both messages the tray depends on are above WM_USER, so without
// the filters the icon appears but never responds to clicks, and disappears permanently the
// first time Explorer restarts.
//
// The WinForms ContextMenuStrip is deliberately NOT used: its dismissal and keyboard handling
// ride on Application.AddMessageFilter, which never runs under WPF's dispatcher. The same WPF
// menu the widget uses is opened instead - which needs SetForegroundWindow first, because a
// programmatically opened WPF popup on a non-foreground window never dismisses.
using System;
using System.Drawing;
using System.Windows.Forms;
// Both namespaces define ContextMenu; this file needs the WPF one (see the header note
// about why the WinForms ContextMenuStrip is unusable here).
using WpfMenu = System.Windows.Controls.ContextMenu;

namespace Vibespan
{
    public class Tray : IDisposable
    {
        readonly WidgetWindow _win;
        readonly Func<WpfMenu> _menuFactory;
        NotifyIcon _icon;
        IntPtr _lastIconHandle = IntPtr.Zero;
        string _lastTint;
        int _lastPct = -1;

        Native.POINT _hoverAnchor;
        bool _hoverVisible;
        System.Windows.Threading.DispatcherTimer _hoverWatch;   // leave test
        System.Windows.Threading.DispatcherTimer _hoverDelay;   // dwell before showing

        const int HoverLeaveSlack = 16;    // px off the anchor before the pointer counts as gone
        const int HoverPollMs = 40;        // leave test cadence; cheap, it is one GetCursorPos
        const int HoverShowDelayMs = 350;  // same dwell as the widget's ToolTipService delay

        const uint WM_TRAYMOUSEMESSAGE = 0x800;   // WM_USER + 1024, what NotifyIcon uses

        public Tray(WidgetWindow win, Func<WpfMenu> menuFactory)
        {
            _win = win;
            _menuFactory = menuFactory;
        }

        public void Start()
        {
            try
            {
                Native.ChangeWindowMessageFilter(WM_TRAYMOUSEMESSAGE, Native.MSGFLT_ADD);
                uint taskbarCreated = Native.RegisterWindowMessage("TaskbarCreated");
                if (taskbarCreated != 0) Native.ChangeWindowMessageFilter(taskbarCreated, Native.MSGFLT_ADD);
            }
            catch (Exception e) { Log.Write("UIPI filter failed: " + e.Message); }

            // Text is left EMPTY on purpose. Anything in it makes the shell draw its own
            // tooltip, which would race and overlap with the card below - and it could not
            // carry the same content anyway, since NotifyIcon.Text throws past 63 characters.
            _icon = new NotifyIcon
            {
                Text = "",
                Visible = true
            };
            UpdateIcon(null);

            // There is no mouse-leave event for a tray icon. While the pointer is over the icon
            // MouseMove keeps firing, so the last position is a live anchor; once the events stop
            // the pointer has gone, and drifting off that anchor is the signal to hide. A pointer
            // resting motionless on the icon fires nothing either, which is why the leave test
            // measures distance and not elapsed time.
            //
            // The slack is deliberately smaller than one icon: because the anchor is refreshed on
            // every move, it always sits where the pointer last was ON the icon, so any real
            // departure clears a short threshold at once. Paired with a fast tick, the card goes
            // the moment you leave, which is what a tooltip should do.
            _icon.MouseMove += delegate
            {
                Native.POINT p;
                if (!Native.GetCursorPos(out p)) return;
                _hoverAnchor = p;

                EnsureHoverTimers();
                _hoverWatch.Start();                       // leave test runs during the delay too

                // Show on a delay, matching the widget's tooltip, so sweeping the pointer across
                // the tray on the way somewhere else does not flash the card. Note this does NOT
                // re-show on every move: once visible the card stays put instead of chasing the
                // pointer around.
                if (!_hoverVisible && !_hoverDelay.IsEnabled) _hoverDelay.Start();
            };

            _icon.MouseUp += delegate (object s, MouseEventArgs e)
            {
                HideHover();
                if (e.Button == MouseButtons.Right) _win.Dispatcher.BeginInvoke(new Action(ShowMenu));
                else if (e.Button == MouseButtons.Left) _win.Dispatcher.BeginInvoke(new Action(_win.BringToCentre));
            };

            if (_win.Alerts != null) _win.Alerts.BalloonSink = Balloon;
        }

        void EnsureHoverTimers()
        {
            if (_hoverWatch == null)
            {
                _hoverWatch = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(HoverPollMs)
                };
                _hoverWatch.Tick += delegate { HoverTick(); };
            }
            if (_hoverDelay == null)
            {
                _hoverDelay = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(HoverShowDelayMs)
                };
                _hoverDelay.Tick += delegate
                {
                    _hoverDelay.Stop();
                    _hoverVisible = true;
                    ShowHover();
                };
            }
        }

        void ShowHover()
        {
            string text = _win.CurrentTooltip;
            if (string.IsNullOrEmpty(text)) return;
            HoverCard.ShowAt(text, _hoverAnchor.X, _hoverAnchor.Y, _win.CurrentTooltipMetaFrom);
        }

        void HoverTick()
        {
            Native.POINT p;
            if (!Native.GetCursorPos(out p)) { HideHover(); return; }
            int dx = p.X - _hoverAnchor.X, dy = p.Y - _hoverAnchor.Y;
            if (dx * dx + dy * dy > HoverLeaveSlack * HoverLeaveSlack) HideHover();
        }

        void HideHover()
        {
            _hoverVisible = false;
            if (_hoverDelay != null) _hoverDelay.Stop();     // cancels a hover that never landed
            if (_hoverWatch != null) _hoverWatch.Stop();
            HoverCard.HideTray();
        }

        void ShowMenu()
        {
            try
            {
                WpfMenu menu = _menuFactory();
                menu.PlacementTarget = _win.RootBorder;
                menu.Placement = System.Windows.Controls.Primitives.PlacementMode.AbsolutePoint;
                Native.POINT p;
                Native.GetCursorPos(out p);
                menu.HorizontalOffset = p.X;
                menu.VerticalOffset = p.Y;

                // Without this the popup never receives the outside click that closes it.
                _win.ForceForeground();
                menu.IsOpen = true;
            }
            catch (Exception e) { Log.Write("tray menu failed: " + e.Message); }
        }

        /// <summary>Best-effort. Elevated/High-IL processes have their notifications suppressed
        /// silently, which is why the widget's own pulse is the primary alert channel.</summary>
        public void Balloon(string title, string body)
        {
            try
            {
                if (_icon == null) return;
                _icon.BalloonTipTitle = title;
                _icon.BalloonTipText = body;
                _icon.BalloonTipIcon = ToolTipIcon.Warning;
                _icon.ShowBalloonTip(5000);
            }
            catch { }
        }

        /// <summary>Tint the icon by the worst visible metric, and put the numbers in the hover text.</summary>
        public void Update(Snapshot snap, Cfg cfg)
        {
            if (_icon == null) return;
            double worst = 0;
            string severity = null;
            var lines = new System.Text.StringBuilder("Vibespan");

            if (snap != null)
            {
                foreach (Bucket b in snap.Buckets)
                {
                    RowCfg r = cfg.Row(b.Key);
                    if (r == null || !r.Visible || !b.HasPercent) continue;
                    if (b.Percent > worst) { worst = b.Percent; severity = b.Severity; }
                    if (lines.Length < 55)
                        lines.Append('\n').Append(b.Label).Append(' ')
                             .Append((int)Math.Round(b.Percent)).Append('%');
                }
            }

            // Text stays EMPTY. Setting it makes the shell draw its own tooltip, which then
            // sits on top of the hover card - two read-outs of the same thing, one of them the
            // truncated version this class can no longer render properly anyway (NotifyIcon.Text
            // throws above 63 characters). Emptying it in the constructor was not enough: this
            // method runs on every snapshot and used to put it straight back.
            try { _icon.Text = ""; } catch { }

            string tint = Theme.PercentHex(cfg, worst, severity);
            int pct = (int)Math.Round(worst);
            if (tint != _lastTint || pct != _lastPct) { _lastTint = tint; _lastPct = pct; UpdateIcon(tint); }
        }

        void UpdateIcon(string tintHex)
        {
            try
            {
                var brush = new System.Windows.Media.SolidColorBrush(Theme.C(tintHex ?? "#DA7756"));
                brush.Freeze();

                // The Vibespan mark, not the Claude one: the widget face shows whose usage is
                // being reported, but the shell icon identifies the application. Still vector and
                // still tinted, so the icon keeps carrying the worst current level at a glance.
                byte[] png = Gauge.GlyphPng(16, brush, Brand.Starburst);
                using (var ms = new System.IO.MemoryStream(png))
                using (var bmp = new Bitmap(ms))
                {
                    IntPtr h = bmp.GetHicon();
                    Icon fresh = Icon.FromHandle(h);
                    Icon old = _icon.Icon;
                    IntPtr oldHandle = _lastIconHandle;
                    _icon.Icon = fresh;
                    _lastIconHandle = h;

                    // Icon.FromHandle does NOT take ownership. Tray icons come out of a finite
                    // user-object quota, so a missed DestroyIcon here leaks until the process dies.
                    if (old != null) { try { old.Dispose(); } catch { } }
                    if (oldHandle != IntPtr.Zero) Native.DestroyIcon(oldHandle);
                }
            }
            catch (Exception e) { Log.Write("tray icon draw failed: " + e.Message); }
        }

        public void Dispose()
        {
            try { HideHover(); HoverCard.Close(); } catch { }
            try
            {
                if (_icon != null)
                {
                    _icon.Visible = false;          // otherwise a ghost lingers until hover
                    if (_icon.Icon != null) { try { _icon.Icon.Dispose(); } catch { } }
                    _icon.Dispose();
                    _icon = null;
                }
                if (_lastIconHandle != IntPtr.Zero)
                {
                    Native.DestroyIcon(_lastIconHandle);
                    _lastIconHandle = IntPtr.Zero;
                }
            }
            catch { }
        }
    }
}
