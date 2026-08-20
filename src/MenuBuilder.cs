// The context menu IS the settings UI, so its mechanics matter more than usual:
//
//   * StaysOpenOnClick on every toggle, so flipping three checkboxes takes one visit
//   * radio groups are hand-rolled - WPF's MenuItem has no GroupName - and are drawn with a
//     bullet in the Icon slot so they don't look like checkboxes the user could tick together
//   * colour swatches go in MenuItem.Icon rather than a custom header grid: they stay real
//     MenuItems, so keyboard navigation and screen readers keep working for free
//   * no sliders anywhere. A Slider inside a Popup fights the menu for mouse capture; discrete
//     value submenus are what Rainmeter uses for exactly this reason
//
// Depth is kept to two levels for everything common, and the root is grouped with separators.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Vibespan
{
    public class MenuBuilder
    {
        readonly WidgetWindow _win;
        Cfg C { get { return _win.Config; } }
        static Strings L { get { return I18n.T; } }

        public MenuBuilder(WidgetWindow win) { _win = win; }

        // ---------- item helpers ----------
        static MenuItem Item(string header, Action onClick)
        {
            var mi = new MenuItem { Header = header };
            if (onClick != null) mi.Click += delegate { onClick(); };
            return mi;
        }

        static MenuItem Toggle(string header, bool isChecked, Action<bool> onToggle)
        {
            var mi = new MenuItem { Header = header, IsCheckable = true, IsChecked = isChecked, StaysOpenOnClick = true };
            mi.Click += delegate { onToggle(mi.IsChecked); };
            return mi;
        }

        // A bullet in the Icon slot, not a checkbox: these are mutually exclusive and should
        // not invite the user to tick two.
        static MenuItem Radio(string header, bool selected, Action onPick)
        {
            var mi = new MenuItem
            {
                Header = header,
                Icon = new TextBlock
                {
                    Text = selected ? "●" : "",
                    FontSize = 9,
                    Foreground = Brushes.Gray,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            mi.Click += delegate { onPick(); };
            return mi;
        }

        static MenuItem SwatchItem(string hex, bool selected, Action onPick)
        {
            var mi = new MenuItem
            {
                Header = hex,
                Icon = new Border
                {
                    Width = 12, Height = 12,
                    CornerRadius = new CornerRadius(2),
                    Background = Theme.B(hex),
                    BorderThickness = new Thickness(selected ? 2 : 1),
                    BorderBrush = selected ? Brushes.White : Brushes.DimGray
                }
            };
            mi.Click += delegate { onPick(); };
            return mi;
        }

        void Changed(bool rebuildMenu)
        {
            C.Save();
            _win.RefreshAppearance();
            if (rebuildMenu) _win.RaiseMenuRebuild();
        }

        // ---------- root ----------
        public ContextMenu Build()
        {
            var m = new ContextMenu();

            m.Items.Add(Item(L.MenuRefresh, delegate { _win.Refresh(); }));
            m.Items.Add(new Separator());

            m.Items.Add(MetricsMenu());

            // Per-row configuration sits at the root while there are only a few visible rows;
            // beyond that it collapses into one submenu so the root does not sprawl.
            List<RowCfg> visible = VisibleRows();
            if (visible.Count > 0 && visible.Count <= 4)
            {
                foreach (RowCfg r in visible) m.Items.Add(RowMenu(r));
            }
            else if (visible.Count > 4)
            {
                var rows = new MenuItem { Header = L.MenuMetrics + "..." };
                foreach (RowCfg r in visible) rows.Items.Add(RowMenu(r));
                m.Items.Add(rows);
            }

            m.Items.Add(new Separator());
            m.Items.Add(SizeMenu());
            m.Items.Add(AppearanceMenu());
            m.Items.Add(AlertsMenu());
            m.Items.Add(BehaviourMenu());

            m.Items.Add(new Separator());
            m.Items.Add(LanguageMenu());
            m.Items.Add(Item(L.MenuOpenLog, delegate { OpenFile(Cfg.LogPath); }));
            m.Items.Add(Item(L.MenuOpenSettings, delegate { C.Save(); OpenFile(Cfg.Path_); }));
            m.Items.Add(new Separator());
            m.Items.Add(Item(L.MenuQuit, delegate { Application.Current.Shutdown(); }));

            return m;
        }

        List<RowCfg> VisibleRows()
        {
            var list = new List<RowCfg>();
            Snapshot s = _win.CurrentSnapshot;
            if (s == null) return list;
            foreach (Bucket b in s.Buckets)
            {
                RowCfg r = C.Row(b.Key);
                if (r != null && r.Visible && b.HasPercent) list.Add(r);
            }
            return list;
        }

        string RowTitle(RowCfg r)
        {
            if (!string.IsNullOrEmpty(r.CustomLabel)) return r.CustomLabel;
            string s, l;
            Snapshot.LabelFor(r.Key, out s, out l);
            return l;
        }

        // ---------- metrics ----------
        MenuItem MetricsMenu()
        {
            var m = new MenuItem { Header = L.MenuMetrics };
            Snapshot snap = _win.CurrentSnapshot;

            if (snap == null || snap.Buckets.Count == 0)
            {
                m.Items.Add(new MenuItem { Header = L.Loading, IsEnabled = false });
                return m;
            }

            foreach (Bucket b in snap.Buckets)
            {
                if (!b.HasPercent) continue;
                RowCfg r = C.EnsureRow(b.Key);
                string header = b.LongLabel + (b.IsActive ? "   ●" : "");
                RowCfg captured = r;
                var mi = Toggle(header, r.Visible, delegate (bool on)
                {
                    captured.Visible = on;
                    Changed(true);
                });
                if (b.IsActive) mi.ToolTip = L.ActiveLimit;
                m.Items.Add(mi);
            }

            m.Items.Add(new Separator());
            List<RowCfg> vis = VisibleRows();
            foreach (RowCfg r in vis)
            {
                RowCfg captured = r;
                int idx = C.Rows.IndexOf(r);
                var up = Item(L.MenuMoveUp + "  —  " + RowTitle(r), delegate { MoveRow(captured, -1); });
                up.IsEnabled = idx > 0;
                m.Items.Add(up);
            }
            foreach (RowCfg r in vis)
            {
                RowCfg captured = r;
                int idx = C.Rows.IndexOf(r);
                var dn = Item(L.MenuMoveDown + "  —  " + RowTitle(r), delegate { MoveRow(captured, +1); });
                dn.IsEnabled = idx >= 0 && idx < C.Rows.Count - 1;
                m.Items.Add(dn);
            }
            return m;
        }

        void MoveRow(RowCfg r, int delta)
        {
            int i = C.Rows.IndexOf(r);
            int j = i + delta;
            if (i < 0 || j < 0 || j >= C.Rows.Count) return;
            C.Rows.RemoveAt(i);
            C.Rows.Insert(j, r);
            // The snapshot drives render order, so reorder it too rather than waiting for a poll.
            _win.ReorderSnapshot();
            Changed(true);
        }

        // ---------- one row ----------
        MenuItem RowMenu(RowCfg r)
        {
            var m = new MenuItem { Header = RowTitle(r) };
            int current = r.PresetIndex();

            for (int i = 0; i < RowCfg.PresetNames.Length; i++)
            {
                int idx = i;
                RowCfg captured = r;
                m.Items.Add(Radio(RowCfg.PresetNames[i], current == i, delegate
                {
                    captured.SetSlots(RowCfg.PresetSlots(idx));
                    Changed(true);
                }));
            }

            m.Items.Add(new Separator());

            var remaining = new MenuItem { Header = L.MenuRemaining };
            string[] fmts = { "countdown", "clock", "off" };
            string[] names = { L.MenuCountdown, L.MenuClock, L.MenuOff };
            for (int i = 0; i < fmts.Length; i++)
            {
                string f = fmts[i];
                RowCfg captured = r;
                remaining.Items.Add(Radio(names[i], r.ResetFormat == f, delegate
                {
                    captured.ResetFormat = f;
                    Changed(true);
                }));
            }
            m.Items.Add(remaining);

            RowCfg cap2 = r;
            m.Items.Add(Toggle(L.MenuShowRemaining, r.Invert, delegate (bool on)
            {
                cap2.Invert = on;
                Changed(false);
            }));

            m.Items.Add(new Separator());
            m.Items.Add(ColourMenu(r));
            return m;
        }

        MenuItem ColourMenu(RowCfg r)
        {
            var m = new MenuItem { Header = L.MenuColour };
            RowCfg cap = r;

            m.Items.Add(Radio(L.MenuUseThemeColour, r.Accent == null, delegate
            {
                cap.Accent = null;
                Changed(true);
            }));
            m.Items.Add(new Separator());

            foreach (string hex in Theme.Swatches)
            {
                string h = hex;
                bool sel = string.Equals(r.Accent, hex, StringComparison.OrdinalIgnoreCase);
                m.Items.Add(SwatchItem(hex, sel, delegate
                {
                    cap.Accent = h;
                    Changed(true);
                }));
            }

            m.Items.Add(new Separator());
            m.Items.Add(Item(L.MenuMoreColours, delegate { PickColour(cap); }));
            return m;
        }

        // ColorDialog is in the GAC, so it costs nothing to reach. It has no alpha channel,
        // which is fine - transparency is a separate, window-level setting here.
        void PickColour(RowCfg r)
        {
            try
            {
                using (var dlg = new System.Windows.Forms.ColorDialog())
                {
                    dlg.FullOpen = true;
                    dlg.AnyColor = true;
                    if (!string.IsNullOrEmpty(r.Accent))
                    {
                        Color c = Theme.C(r.Accent);
                        dlg.Color = System.Drawing.Color.FromArgb(c.R, c.G, c.B);
                    }
                    _win.ForceForeground();
                    if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    {
                        r.Accent = "#" + dlg.Color.R.ToString("X2", CultureInfo.InvariantCulture)
                                       + dlg.Color.G.ToString("X2", CultureInfo.InvariantCulture)
                                       + dlg.Color.B.ToString("X2", CultureInfo.InvariantCulture);
                        Changed(true);
                    }
                }
            }
            catch (Exception e) { Log.Write("colour picker failed: " + e.Message); }
        }

        // ---------- size ----------
        MenuItem SizeMenu()
        {
            var m = new MenuItem { Header = L.MenuSize };
            double[] steps = { 0.75, 1.0, 1.25, 1.5, 2.0 };
            foreach (double s in steps)
            {
                double v = s;
                bool sel = Math.Abs(C.Scale - v) < 0.02;
                m.Items.Add(Radio(((int)(v * 100)).ToString(CultureInfo.InvariantCulture) + "%", sel, delegate
                {
                    _win.SetScaleAndSave(v);
                    _win.RaiseMenuRebuild();
                }));
            }
            m.Items.Add(new Separator());
            m.Items.Add(Item(L.MenuResetSize, delegate
            {
                _win.SetScaleAndSave(1.0);
                _win.RaiseMenuRebuild();
            }));
            return m;
        }

        // ---------- appearance ----------
        MenuItem AppearanceMenu()
        {
            var m = new MenuItem { Header = L.MenuAppearance };

            var theme = new MenuItem { Header = L.MenuTheme };
            foreach (Theme.Preset p in Theme.Presets)
            {
                Theme.Preset cap = p;
                theme.Items.Add(Radio(p.Name, C.ThemePreset == p.Id, delegate
                {
                    C.ThemePreset = cap.Id;
                    C.Overrides.Clear();          // a preset switch that kept overrides would look broken
                    Changed(true);
                }));
            }
            m.Items.Add(theme);

            var opacity = new MenuItem { Header = L.MenuOpacity };
            double[] steps = { 1.0, 0.9, 0.8, 0.7, 0.6, 0.5 };
            foreach (double o in steps)
            {
                double v = o;
                opacity.Items.Add(Radio(((int)(v * 100)).ToString(CultureInfo.InvariantCulture) + "%",
                                        Math.Abs(C.ContentOpacity - v) < 0.01, delegate
                {
                    C.ContentOpacity = v;
                    Changed(true);
                }));
            }
            m.Items.Add(opacity);

            var orient = new MenuItem { Header = L.MenuOrientation };
            orient.Items.Add(Radio(L.MenuHorizontal, C.Orientation == "horizontal", delegate
            {
                C.Orientation = "horizontal"; Changed(true);
            }));
            orient.Items.Add(Radio(L.MenuVertical, C.Orientation == "vertical", delegate
            {
                C.Orientation = "vertical"; Changed(true);
            }));
            m.Items.Add(orient);

            m.Items.Add(new Separator());
            m.Items.Add(Toggle(L.MenuShowLogo, C.ShowLogo, delegate (bool on) { C.ShowLogo = on; Changed(false); }));
            m.Items.Add(Toggle(L.MenuShowBorder, C.ShowBorder, delegate (bool on) { C.ShowBorder = on; Changed(false); }));
            m.Items.Add(new Separator());
            m.Items.Add(Item(L.MenuResetAppearance, delegate
            {
                C.ThemePreset = "claude";
                C.Overrides.Clear();
                C.ContentOpacity = 1.0;
                C.BackgroundAlpha = 0.95;
                C.ShowLogo = true; C.ShowBorder = true;
                C.Orientation = "horizontal";
                foreach (RowCfg r in C.Rows) r.Accent = null;
                Changed(true);
            }));
            return m;
        }

        // ---------- alerts ----------
        MenuItem AlertsMenu()
        {
            var m = new MenuItem { Header = L.MenuAlerts };
            int[] levels = { 50, 70, 80, 90, 95 };
            foreach (int lv in levels)
            {
                int v = lv;
                m.Items.Add(Toggle(string.Format(L.MenuNotifyAt, v), C.AlertLevels.Contains(v), delegate (bool on)
                {
                    if (on) { if (!C.AlertLevels.Contains(v)) C.AlertLevels.Add(v); }
                    else C.AlertLevels.Remove(v);
                    C.AlertLevels.Sort();
                    // Re-arm so switching a level on does not immediately fire for a value that
                    // was already above it.
                    if (_win.Alerts != null) _win.Alerts.Rearm();
                    Changed(false);
                }));
            }
            m.Items.Add(new Separator());
            m.Items.Add(Toggle(L.MenuPlaySound, C.AlertSound, delegate (bool on) { C.AlertSound = on; Changed(false); }));

            if (C.IsMuted)
            {
                string until = DateTimeOffset.FromUnixTimeSeconds(C.MutedUntilUnix).ToLocalTime().ToString("HH:mm");
                var un = Item(string.Format(L.MenuMuted, until), delegate { C.MutedUntilUnix = 0; Changed(true); });
                m.Items.Add(un);
            }
            else
            {
                m.Items.Add(Item(L.MenuMuteOneHour, delegate
                {
                    C.MutedUntilUnix = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds();
                    Changed(true);
                }));
            }
            return m;
        }

        // ---------- behaviour ----------
        MenuItem BehaviourMenu()
        {
            var m = new MenuItem { Header = L.MenuBehaviour };

            m.Items.Add(Toggle(L.MenuHideFullScreen, C.HideFullScreen, delegate (bool on)
            {
                C.HideFullScreen = on;
                Changed(false);
                _win.EvaluateTopmost();
            }));
            m.Items.Add(Toggle(L.MenuClickThrough, C.ClickThrough, delegate (bool on)
            {
                C.ClickThrough = on;
                Changed(false);
            }));
            m.Items.Add(new Separator());
            m.Items.Add(Toggle(L.MenuStartWithWindows, AutoStart.IsEnabled, delegate (bool on)
            {
                AutoStart.Set(on);
            }));

            m.Items.Add(new Separator());
            Feed.FeedState fs = Feed.Detect();
            var feed = Toggle(L.MenuUseLiveFeed, fs == Feed.FeedState.OwnedByUs, delegate (bool on)
            {
                if (on) { if (Feed.Enable()) { C.UseLiveFeed = true; Changed(true); } }
                else { Feed.Disable(); C.UseLiveFeed = false; Changed(true); }
            });
            if (fs == Feed.FeedState.ForeignStatusLine)
            {
                feed.IsEnabled = false;
                feed.ToolTip = L.FeedBusy;
            }
            m.Items.Add(feed);

            m.Items.Add(new Separator());
            m.Items.Add(Item(L.MenuBringToCentre, delegate { _win.BringToCentre(); }));
            return m;
        }

        // ---------- language ----------
        MenuItem LanguageMenu()
        {
            var m = new MenuItem { Header = L.MenuLanguage };
            foreach (Strings s in I18n.Catalog)
            {
                Strings lang = s;
                m.Items.Add(Radio(lang.Native, lang.Code == L.Code, delegate
                {
                    I18n.Use(lang.Code);
                    C.Lang = lang.Code;
                    Changed(true);
                }));
            }
            return m;
        }

        static void OpenFile(string path)
        {
            try
            {
                if (!File.Exists(path)) return;
                // Launch through explorer so the child does NOT inherit this process's token.
                // uiAccess under an admin account means High integrity, and Process.Start would
                // otherwise open an elevated Notepad.
                Process.Start("explorer.exe", "\"" + path + "\"");
            }
            catch (Exception e) { Log.Write("open failed: " + e.Message); }
        }
    }

    public static class AutoStart
    {
        static string LinkPath
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup),
                                    "Vibespan.lnk");
            }
        }

        public static bool IsEnabled { get { return File.Exists(LinkPath); } }

        public static void Set(bool on)
        {
            try
            {
                if (on)
                {
                    Type t = Type.GetTypeFromProgID("WScript.Shell");
                    dynamic shell = Activator.CreateInstance(t);
                    dynamic lnk = shell.CreateShortcut(LinkPath);
                    lnk.TargetPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                    lnk.Save();
                }
                else if (File.Exists(LinkPath)) File.Delete(LinkPath);
            }
            catch (Exception e) { Log.Write("autostart change failed: " + e.Message); }
        }
    }
}
