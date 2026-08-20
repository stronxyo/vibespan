// View construction. Pure rendering: no Win32, no config mutation, no I/O.
//
// Column widths are computed from the union of slots actually in use, so hiding the
// percentage really reclaims its space instead of leaving a gap - but every row shares the
// same column set, so the numbers still line up with each other.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Vibespan
{
    public static class Gauge
    {
        public const double RowHeight = 18;
        public const double LogoColumn = 20;
        const double LabelW = 24, PercentW = 36, BarW = 58, ResetW = 44, BarH = 4;

        static Strings L { get { return I18n.T; } }

        /// <summary>The Claude-style asterisk, drawn as vectors so it stays crisp at any scale.</summary>
        public static Canvas Logo(double size, Brush fill)
        {
            var cv = new Canvas { Width = size, Height = size };
            double c = size / 2;
            double[] lens = { 0.50, 0.41, 0.47, 0.42, 0.50, 0.43, 0.46, 0.40, 0.49, 0.42, 0.47, 0.41 };
            double half = 7.5 * Math.PI / 180;
            for (int i = 0; i < 12; i++)
            {
                double a = i * 30 * Math.PI / 180;
                double r = size * lens[i];
                var poly = new Polygon { Fill = fill };
                poly.Points.Add(new Point(c, c));
                poly.Points.Add(new Point(c + r * Math.Cos(a - half), c + r * Math.Sin(a - half)));
                poly.Points.Add(new Point(c + r * Math.Cos(a + half), c + r * Math.Sin(a + half)));
                cv.Children.Add(poly);
            }
            return cv;
        }

        // ---------- bar ----------
        static FrameworkElement Bar(Cfg cfg, double pct, string severity, string accent, double w, double h)
        {
            var track = new Border
            {
                Width = w, Height = h,
                CornerRadius = new CornerRadius(1),   // rounded caps eat visible fill at 4px
                Background = Theme.Brush_(cfg, Theme.Track),
                VerticalAlignment = VerticalAlignment.Center
            };

            var grid = new Grid { Width = w, Height = h };
            grid.Children.Add(track);

            if (pct > 0)
            {
                // Floor the fill so 1% is visible rather than swallowed by antialiasing.
                double fw = Math.Max(2, w * Math.Min(100, pct) / 100.0);
                var fill = new Border
                {
                    Width = fw, Height = h,
                    CornerRadius = new CornerRadius(1),
                    Background = accent != null ? Theme.B(accent) : Theme.PercentBrush(cfg, pct, severity),
                    HorizontalAlignment = HorizontalAlignment.Left
                };
                grid.Children.Add(fill);
            }

            // Threshold tick. Colour must never be the only signal (WCAG 1.4.1), and a
            // reference mark turns a bare fill into an actual gauge.
            double warn = Theme.WarnAt(cfg);
            if (warn > 0 && warn < 100)
            {
                var tick = new Rectangle
                {
                    Width = 1, Height = h,
                    Fill = Theme.Brush_(cfg, Theme.Background),
                    Opacity = 0.85,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Margin = new Thickness(Math.Round(w * warn / 100.0), 0, 0, 0)
                };
                grid.Children.Add(tick);
            }
            return grid;
        }

        static TextBlock Text(string text, double size, Brush fg, TextAlignment align)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = size,
                Foreground = fg,
                TextAlignment = align,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        // ---------- row assembly ----------
        class Slots
        {
            public bool Label, Percent, Bar, Reset;
            public bool Any { get { return Label || Percent || Bar || Reset; } }
        }

        static Slots UnionOf(Cfg cfg, List<Bucket> shown)
        {
            var u = new Slots();
            foreach (Bucket b in shown)
            {
                RowCfg r = cfg.Row(b.Key);
                if (r == null) continue;
                if (r.Has("label")) u.Label = true;
                if (r.Has("percent")) u.Percent = true;
                if (r.Has("bar")) u.Bar = true;
                if (r.Has("reset") && r.ResetFormat != "off") u.Reset = true;
            }
            return u;
        }

        static string PercentText(Bucket b, RowCfg r)
        {
            double v = r.Invert ? 100 - b.Percent : b.Percent;
            return ((int)Math.Round(v)).ToString(CultureInfo.InvariantCulture) + "%";
        }

        static string ResetText(Bucket b, RowCfg r)
        {
            if (r.ResetFormat == "off") return "";
            return r.ResetFormat == "clock" ? Fmt.Clock(b.ResetsAt) : Fmt.Countdown(b.ResetsAt);
        }

        static string LabelText(Bucket b, RowCfg r)
        {
            return string.IsNullOrEmpty(r.CustomLabel) ? b.Label : r.CustomLabel;
        }

        static FrameworkElement HorizontalRow(Cfg cfg, Bucket b, RowCfg r, Slots u)
        {
            var row = new Grid { Height = RowHeight };
            var widths = new List<double>();
            if (u.Label) widths.Add(LabelW);
            if (u.Percent) widths.Add(PercentW);
            if (u.Bar) widths.Add(BarW + 4);
            if (u.Reset) widths.Add(ResetW);
            foreach (double w in widths)
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(w) });

            int col = 0;
            bool critical = b.Percent >= Theme.CriticalAt(cfg);

            if (u.Label)
            {
                var stack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                if (r.Has("label"))
                    stack.Children.Add(Text(LabelText(b, r), 8.5, Theme.Brush_(cfg, Theme.LabelColor), TextAlignment.Left));
                if (b.IsActive)
                {
                    // Which limit is actually binding right now - the server tells us.
                    stack.Children.Add(new Ellipse
                    {
                        Width = 3, Height = 3, Margin = new Thickness(3, 0, 0, 0),
                        Fill = Theme.Brush_(cfg, Theme.TextSecondary),
                        VerticalAlignment = VerticalAlignment.Center
                    });
                }
                Grid.SetColumn(stack, col);
                row.Children.Add(stack);
            }
            if (u.Label) col++;

            if (u.Percent)
            {
                if (r.Has("percent"))
                {
                    var stack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                    stack.Children.Add(new TextBlock
                    {
                        Text = PercentText(b, r),
                        FontSize = 10,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = r.Accent != null ? Theme.B(r.Accent) : Theme.PercentBrush(cfg, b.Percent, b.Severity),
                        VerticalAlignment = VerticalAlignment.Center
                    });
                    // Redundant non-colour cue at critical.
                    if (critical && !r.Invert)
                        stack.Children.Add(new TextBlock
                        {
                            Text = "!", FontSize = 10, FontWeight = FontWeights.Bold,
                            Margin = new Thickness(2, 0, 0, 0),
                            Foreground = Theme.PercentBrush(cfg, b.Percent, b.Severity),
                            VerticalAlignment = VerticalAlignment.Center
                        });
                    Grid.SetColumn(stack, col);
                    row.Children.Add(stack);
                }
                col++;
            }

            if (u.Bar)
            {
                if (r.Has("bar"))
                {
                    FrameworkElement bar = Bar(cfg, r.Invert ? 100 - b.Percent : b.Percent, b.Severity, r.Accent, BarW, BarH);
                    bar.HorizontalAlignment = HorizontalAlignment.Left;
                    Grid.SetColumn(bar, col);
                    row.Children.Add(bar);
                }
                col++;
            }

            if (u.Reset)
            {
                if (r.Has("reset"))
                {
                    string t = ResetText(b, r);
                    if (t.Length > 0)
                    {
                        var tb = Text(t, 9, Theme.Brush_(cfg, Theme.TextSecondary), TextAlignment.Right);
                        Grid.SetColumn(tb, col);
                        row.Children.Add(tb);
                    }
                }
                col++;
            }

            return row;
        }

        static FrameworkElement VerticalRow(Cfg cfg, Bucket b, RowCfg r)
        {
            var stack = new StackPanel { Orientation = Orientation.Vertical, Width = 54, Margin = new Thickness(0, 0, 0, 6) };
            bool critical = b.Percent >= Theme.CriticalAt(cfg);

            if (r.Has("label"))
            {
                var head = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
                head.Children.Add(Text(LabelText(b, r), 8.5, Theme.Brush_(cfg, Theme.LabelColor), TextAlignment.Center));
                if (b.IsActive)
                    head.Children.Add(new Ellipse
                    {
                        Width = 3, Height = 3, Margin = new Thickness(3, 0, 0, 0),
                        Fill = Theme.Brush_(cfg, Theme.TextSecondary),
                        VerticalAlignment = VerticalAlignment.Center
                    });
                stack.Children.Add(head);
            }
            if (r.Has("percent"))
            {
                stack.Children.Add(new TextBlock
                {
                    Text = PercentText(b, r) + (critical && !r.Invert ? " !" : ""),
                    FontSize = 11, FontWeight = FontWeights.SemiBold,
                    TextAlignment = TextAlignment.Center,
                    Foreground = r.Accent != null ? Theme.B(r.Accent) : Theme.PercentBrush(cfg, b.Percent, b.Severity)
                });
            }
            if (r.Has("bar"))
            {
                FrameworkElement bar = Bar(cfg, r.Invert ? 100 - b.Percent : b.Percent, b.Severity, r.Accent, 48, BarH);
                bar.HorizontalAlignment = HorizontalAlignment.Center;
                bar.Margin = new Thickness(0, 2, 0, 2);
                stack.Children.Add(bar);
            }
            if (r.Has("reset"))
            {
                string t = ResetText(b, r);
                if (t.Length > 0)
                    stack.Children.Add(Text(t, 9, Theme.Brush_(cfg, Theme.TextSecondary), TextAlignment.Center));
            }
            return stack;
        }

        // ---------- public entry ----------
        public class Rendered
        {
            public FrameworkElement Content;
            public string Tooltip;
        }

        public static Rendered BuildMessage(Cfg cfg, string message)
        {
            var grid = new Grid { Height = 36, MinWidth = 140 };
            grid.Children.Add(new TextBlock
            {
                Text = message,
                FontSize = 10,
                Foreground = Theme.Brush_(cfg, Theme.TextSecondary),
                VerticalAlignment = VerticalAlignment.Center
            });
            return new Rendered { Content = grid, Tooltip = message };
        }

        public static Rendered Build(Cfg cfg, Snapshot snap, string errorCode, DateTime lastOk)
        {
            var shown = new List<Bucket>();
            foreach (Bucket b in snap.Buckets)
            {
                RowCfg r = cfg.Row(b.Key);
                if (r != null && r.Visible && b.HasPercent) shown.Add(b);
            }
            if (shown.Count == 0) return BuildMessage(cfg, L.Loading);

            var tips = new List<string>();
            bool vertical = cfg.Orientation == "vertical";
            Panel host = vertical
                ? (Panel)new StackPanel { Orientation = Orientation.Vertical }
                : new StackPanel { Orientation = Orientation.Vertical, MinHeight = 36, VerticalAlignment = VerticalAlignment.Center };

            Slots union = UnionOf(cfg, shown);

            foreach (Bucket b in shown)
            {
                RowCfg r = cfg.Row(b.Key);
                host.Children.Add(vertical ? VerticalRow(cfg, b, r) : HorizontalRow(cfg, b, r, union));

                string pct = PercentText(b, r);
                string reset = Fmt.Countdown(b.ResetsAt);
                string line = b.LongLabel + L.Colon + pct;
                if (reset.Length > 0) line += " (" + string.Format(L.ResetsIn, reset) + ")";
                if (b.IsActive) line += "  • " + L.ActiveLimit;
                tips.Add(line);
            }

            tips.Add(string.Format(L.Updated, lastOk.ToString("HH:mm")) + "  — " +
                     (snap.Source == Provenance.Feed ? L.SourceLive : L.SourcePolled));
            if (errorCode != null)
                tips.Add(string.Format(L.FrozenFor, Fmt.Age(DateTime.Now - lastOk), Fmt.ErrorText(errorCode)));

            return new Rendered { Content = (FrameworkElement)host, Tooltip = string.Join(Environment.NewLine, tips.ToArray()) };
        }
    }
}
