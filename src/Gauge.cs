// View construction. Pure rendering: no Win32, no config mutation, no I/O.
//
// Geometry comes from the active StylePreset, colour from the active Theme, and the two are
// independent - "I want a denser, squarer widget" has nothing to do with "I want a
// colour-blind-safe palette".
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
        public const double LogoColumn = 20;
        const double LabelW = 26, PercentW = 38, ResetW = 44;

        static Strings L { get { return I18n.T; } }

        // ---------- marks ----------
        /// <summary>The Claude-style asterisk, drawn as vectors so it stays crisp at any scale.</summary>
        public static Canvas Asterisk(double size, Brush fill)
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

        /// <summary>Build the left-hand mark for the active style, or null when there isn't one.</summary>
        public static FrameworkElement Mark(Cfg cfg)
        {
            StylePreset st = Styles.For(cfg);
            Brush accent = Theme.Brush_(cfg, Theme.Logo);

            switch (st.Mark)
            {
                case "asterisk":
                    return Asterisk(14, accent);

                case "rail":
                    // A vertical accent rail. Cheap, and the single clearest way to stop the
                    // widget reading as "the one with the Claude asterisk".
                    //
                    // Stretch, never a fixed height: the rail must follow however many rows
                    // are visible. Giving it a height of its own made it prop the widget open
                    // at two rows even when only one metric was shown.
                    return new Border
                    {
                        Width = 3,
                        MinHeight = 10,
                        CornerRadius = new CornerRadius(1.5),
                        Background = accent,
                        VerticalAlignment = VerticalAlignment.Stretch,
                        HorizontalAlignment = HorizontalAlignment.Left
                    };

                case "dot":
                    return new Ellipse
                    {
                        Width = 7, Height = 7,
                        Fill = accent,
                        VerticalAlignment = VerticalAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Left
                    };

                default:
                    return null;
            }
        }

        public static double MarkColumnWidth(Cfg cfg)
        {
            StylePreset st = Styles.For(cfg);
            if (st.Mark == "none") return 0;
            if (st.Mark == "asterisk") return LogoColumn;
            if (st.Mark == "rail") return 9;
            return 13;   // dot
        }

        // ---------- bar ----------
        static FrameworkElement Bar(Cfg cfg, StylePreset st, double pct, string severity, string accent, double w)
        {
            double h = st.BarHeight;
            Brush fillBrush = accent != null ? Theme.B(accent) : Theme.PercentBrush(cfg, pct, severity);
            Brush trackBrush = Theme.Brush_(cfg, Theme.Track);
            double radius = st.Bar == "continuous" ? 1 : 0;

            var grid = new Grid { Width = w, Height = h };

            if (st.Bar == "segmented" || st.Bar == "blocks")
            {
                // Quantises to the cell count, which costs precision - acceptable because the
                // number is usually right next to it, and it reads very differently from a
                // plain fill, which is the point of offering it.
                int cells = st.Bar == "segmented" ? 10 : 5;
                double gap = st.Bar == "segmented" ? 2 : 3;
                double cellW = (w - gap * (cells - 1)) / cells;
                double filled = pct / 100.0 * cells;

                var row = new StackPanel { Orientation = Orientation.Horizontal };
                for (int i = 0; i < cells; i++)
                {
                    double frac = Math.Max(0, Math.Min(1, filled - i));
                    var cell = new Border
                    {
                        Width = cellW, Height = h,
                        Margin = new Thickness(0, 0, i == cells - 1 ? 0 : gap, 0),
                        CornerRadius = new CornerRadius(radius),
                        Background = trackBrush
                    };
                    if (frac > 0)
                    {
                        // Partial cells fade rather than half-fill: a half-lit block at 5px
                        // wide just looks like a rendering artefact.
                        cell.Background = fillBrush;
                        cell.Opacity = frac < 1 ? 0.35 + 0.65 * frac : 1.0;
                    }
                    row.Children.Add(cell);
                }
                grid.Children.Add(row);
                return grid;
            }

            // continuous
            grid.Children.Add(new Border
            {
                Width = w, Height = h,
                CornerRadius = new CornerRadius(1),
                Background = trackBrush,
                VerticalAlignment = VerticalAlignment.Center
            });
            if (pct > 0)
            {
                // Floor the fill so 1% is visible rather than swallowed by antialiasing.
                double fw = Math.Max(2, w * Math.Min(100, pct) / 100.0);
                grid.Children.Add(new Border
                {
                    Width = fw, Height = h,
                    CornerRadius = new CornerRadius(1),
                    Background = fillBrush,
                    HorizontalAlignment = HorizontalAlignment.Left
                });
            }

            // Threshold tick. Colour must never be the only signal (WCAG 1.4.1), and a
            // reference mark turns a bare fill into an actual gauge.
            double warn = Theme.WarnAt(cfg);
            if (warn > 0 && warn < 100)
            {
                grid.Children.Add(new Rectangle
                {
                    Width = 1, Height = h,
                    Fill = Theme.Brush_(cfg, Theme.Background),
                    Opacity = 0.85,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Margin = new Thickness(Math.Round(w * warn / 100.0), 0, 0, 0)
                });
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

        static string LabelText(Cfg cfg, Bucket b, RowCfg r)
        {
            string t = string.IsNullOrEmpty(r.CustomLabel) ? b.Label : r.CustomLabel;
            return Styles.For(cfg).UpperLabels ? t.ToUpperInvariant() : t;
        }

        static FrameworkElement HorizontalRow(Cfg cfg, StylePreset st, Bucket b, RowCfg r, Slots u)
        {
            var row = new Grid { Height = st.RowHeight };
            var widths = new List<double>();
            if (u.Label) widths.Add(LabelW);
            if (u.Percent) widths.Add(PercentW);
            if (u.Bar) widths.Add(st.BarWidth + 6);
            if (u.Reset) widths.Add(ResetW);
            foreach (double w in widths)
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(w) });

            int col = 0;
            bool critical = b.Percent >= Theme.CriticalAt(cfg);

            if (u.Label)
            {
                if (r.Has("label"))
                {
                    var stack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                    stack.Children.Add(Text(LabelText(cfg, b, r), st.LabelSize,
                                            Theme.Brush_(cfg, Theme.LabelColor), TextAlignment.Left));
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
                col++;
            }

            if (u.Percent)
            {
                if (r.Has("percent"))
                {
                    var stack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                    stack.Children.Add(new TextBlock
                    {
                        Text = PercentText(b, r),
                        FontSize = st.PercentSize,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = r.Accent != null ? Theme.B(r.Accent) : Theme.PercentBrush(cfg, b.Percent, b.Severity),
                        VerticalAlignment = VerticalAlignment.Center
                    });
                    // Redundant non-colour cue at critical.
                    if (critical && !r.Invert)
                        stack.Children.Add(new TextBlock
                        {
                            Text = "!", FontSize = st.PercentSize, FontWeight = FontWeights.Bold,
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
                    FrameworkElement bar = Bar(cfg, st, r.Invert ? 100 - b.Percent : b.Percent,
                                               b.Severity, r.Accent, st.BarWidth);
                    bar.HorizontalAlignment = HorizontalAlignment.Left;
                    bar.VerticalAlignment = VerticalAlignment.Center;
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
                        var tb = Text(t, st.ResetSize, Theme.Brush_(cfg, Theme.TextSecondary), TextAlignment.Right);
                        Grid.SetColumn(tb, col);
                        row.Children.Add(tb);
                    }
                }
                col++;
            }

            return row;
        }

        static FrameworkElement VerticalRow(Cfg cfg, StylePreset st, Bucket b, RowCfg r)
        {
            var stack = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Width = Math.Max(54, st.BarWidth + 6),
                Margin = new Thickness(0, 0, 0, 6)
            };
            bool critical = b.Percent >= Theme.CriticalAt(cfg);

            if (r.Has("label"))
            {
                var head = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
                head.Children.Add(Text(LabelText(cfg, b, r), st.LabelSize, Theme.Brush_(cfg, Theme.LabelColor), TextAlignment.Center));
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
                    FontSize = st.PercentSize + 1,
                    FontWeight = FontWeights.SemiBold,
                    TextAlignment = TextAlignment.Center,
                    Foreground = r.Accent != null ? Theme.B(r.Accent) : Theme.PercentBrush(cfg, b.Percent, b.Severity)
                });
            }
            if (r.Has("bar"))
            {
                FrameworkElement bar = Bar(cfg, st, r.Invert ? 100 - b.Percent : b.Percent,
                                           b.Severity, r.Accent, st.BarWidth);
                bar.HorizontalAlignment = HorizontalAlignment.Center;
                bar.Margin = new Thickness(0, 2, 0, 2);
                stack.Children.Add(bar);
            }
            if (r.Has("reset"))
            {
                string t = ResetText(b, r);
                if (t.Length > 0)
                    stack.Children.Add(Text(t, st.ResetSize, Theme.Brush_(cfg, Theme.TextSecondary), TextAlignment.Center));
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
            StylePreset st = Styles.For(cfg);
            var grid = new Grid { Height = st.RowHeight * 2, MinWidth = 140 };
            grid.Children.Add(new TextBlock
            {
                Text = message,
                FontSize = st.ResetSize + 1,
                Foreground = Theme.Brush_(cfg, Theme.TextSecondary),
                VerticalAlignment = VerticalAlignment.Center
            });
            return new Rendered { Content = grid, Tooltip = message };
        }

        public static Rendered Build(Cfg cfg, Snapshot snap, string errorCode, DateTime lastOk)
        {
            StylePreset st = Styles.For(cfg);

            var shown = new List<Bucket>();
            foreach (Bucket b in snap.Buckets)
            {
                RowCfg r = cfg.Row(b.Key);
                if (r != null && r.Visible && b.HasPercent) shown.Add(b);
            }
            if (shown.Count == 0) return BuildMessage(cfg, L.Loading);

            var tips = new List<string>();
            bool vertical = cfg.Orientation == "vertical";
            // No MinHeight. Each row carries its own explicit height, so the panel sizes to
            // exactly the number of rows shown - a two-row floor here is why selecting a
            // single metric still produced a two-metric-tall widget.
            var host = new StackPanel
            {
                Orientation = Orientation.Vertical,
                VerticalAlignment = VerticalAlignment.Center
            };

            Slots union = UnionOf(cfg, shown);

            foreach (Bucket b in shown)
            {
                RowCfg r = cfg.Row(b.Key);
                host.Children.Add(vertical ? VerticalRow(cfg, st, b, r) : HorizontalRow(cfg, st, b, r, union));

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

            return new Rendered { Content = host, Tooltip = string.Join(Environment.NewLine, tips.ToArray()) };
        }
    }
}
