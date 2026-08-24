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

            _icon = new NotifyIcon
            {
                Text = "Vibespan",
                Visible = true
            };
            UpdateIcon(null);

            _icon.MouseUp += delegate (object s, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Right) _win.Dispatcher.BeginInvoke(new Action(ShowMenu));
                else if (e.Button == MouseButtons.Left) _win.Dispatcher.BeginInvoke(new Action(_win.BringToCentre));
            };

            if (_win.Alerts != null) _win.Alerts.BalloonSink = Balloon;
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

            // NotifyIcon.Text throws above 63 characters.
            string text = lines.ToString();
            if (text.Length > 62) text = text.Substring(0, 62);
            try { _icon.Text = text; } catch { }

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
