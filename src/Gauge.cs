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
        // The real Claude wordmark glyph, not an approximation. The predecessor drew twelve
        // triangular spokes of jittered length, which reads as "an asterisk" but is not the
        // actual mark - the real one has unequal, tapered rays and is visibly asymmetric.
        //
        // Path data is the brand glyph on a 0 0 24 24 viewBox. WPF's path mini-language is a
        // superset of SVG's for everything used here, so it parses as-is.
        //
        // The mark is Anthropic's trademark, used to identify the service this widget reports
        // on. The project is not affiliated with or endorsed by Anthropic.
        const string ClaudeGlyph =
            "m4.7144 15.9555 4.7174-2.6471.079-.2307-.079-.1275h-.2307l-.7893-.0486-2.6956-.0729-2.3375-.0971-2.2646-.1214-.5707-.1215-.5343-.7042.0546-.3522.4797-.3218.686.0608 1.5179.1032 2.2767.1578 1.6514.0972 2.4468.255h.3886l.0546-.1579-.1336-.0971-.1032-.0972L6.973 9.8356l-2.55-1.6879-1.3356-.9714-.7225-.4918-.3643-.4614-.1578-1.0078.6557-.7225.8803.0607.2246.0607.8925.686 1.9064 1.4754 2.4893 1.8336.3643.3035.1457-.1032.0182-.0728-.164-.2733-1.3539-2.4467-1.445-2.4893-.6435-1.032-.17-.6194c-.0607-.255-.1032-.4674-.1032-.7285L6.287.1335 6.6997 0l.9957.1336.419.3642.6192 1.4147 1.0018 2.2282 1.5543 3.0296.4553.8985.2429.8318.091.255h.1579v-.1457l.1275-1.706.2368-2.0947.2307-2.6957.0789-.7589.3764-.9107.7468-.4918.5828.2793.4797.686-.0668.4433-.2853 1.8517-.5586 2.9021-.3643 1.9429h.2125l.2429-.2429.9835-1.3053 1.6514-2.0643.7286-.8196.85-.9046.5464-.4311h1.0321l.759 1.1293-.34 1.1657-1.0625 1.3478-.8804 1.1414-1.2628 1.7-.7893 1.36.0729.1093.1882-.0183 2.8535-.607 1.5421-.2794 1.8396-.3157.8318.3886.091.3946-.3278.8075-1.967.4857-2.3072.4614-3.4364.8136-.0425.0304.0486.0607 1.5482.1457.6618.0364h1.621l3.0175.2247.7892.522.4736.6376-.079.4857-1.2142.6193-1.6393-.3886-3.825-.9107-1.3113-.3279h-.1822v.1093l1.0929 1.0686 2.0035 1.8092 2.5075 2.3314.1275.5768-.3218.4554-.34-.0486-2.2039-1.6575-.85-.7468-1.9246-1.621h-.1275v.17l.4432.6496 2.3436 3.5214.1214 1.0807-.17.3521-.6071.2125-.6679-.1214-1.3721-1.9246L14.38 17.959l-1.1414-1.9428-.1397.079-.674 7.2552-.3156.3703-.7286.2793-.6071-.4614-.3218-.7468.3218-1.4753.3886-1.9246.3157-1.53.2853-1.9004.17-.6314-.0121-.0425-.1397.0182-1.4328 1.9672-2.1796 2.9446-1.7243 1.8456-.4128.164-.7164-.3704.0667-.6618.4008-.5889 2.386-3.0357 1.4389-1.882.929-1.0868-.0062-.1579h-.0546l-6.3385 4.1164-1.1293.1457-.4857-.4554.0608-.7467.2307-.2429 1.9064-1.3114Z";

        static Geometry _claudeGeometry;
        static Geometry ClaudeGeometry
        {
            get
            {
                if (_claudeGeometry == null)
                {
                    _claudeGeometry = Geometry.Parse(ClaudeGlyph);
                    _claudeGeometry.Freeze();   // parsed once, shared by widget and tray
                }
                return _claudeGeometry;
            }
        }

        /// <summary>The Claude mark as vectors, so it stays crisp at any scale.</summary>
        public static FrameworkElement Asterisk(double size, Brush fill)
        {
            return new System.Windows.Shapes.Path
            {
                Data = ClaudeGeometry,
                Fill = fill,
                Stretch = Stretch.Uniform,     // 24x24 viewBox -> whatever size is asked for
                Width = size,
                Height = size,
                SnapsToDevicePixels = false
            };
        }

        /// <summary>
        /// Render the Claude mark to a PNG byte stream at the given pixel size. The tray icon
        /// uses this rather than re-drawing the glyph with GDI+: hand-porting 1.8 KB of path
        /// data to GraphicsPath would guarantee the two marks drift apart.
        /// </summary>
        public static byte[] MarkPng(int px, Brush fill)
        {
            return GlyphPng(px, fill, ClaudeGeometry);
        }

        /// <summary>Render any frozen geometry to a square PNG, filled with the given brush.</summary>
        public static byte[] GlyphPng(int px, Brush fill, Geometry geometry)
        {
            var path = new System.Windows.Shapes.Path
            {
                Data = geometry,
                Fill = fill,
                Stretch = Stretch.Uniform,
                Width = px,
                Height = px
            };
            path.Measure(new Size(px, px));
            path.Arrange(new Rect(0, 0, px, px));

            var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(
                px, px, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(path);

            var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
            enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
            using (var ms = new System.IO.MemoryStream())
            {
                enc.Save(ms);
                return ms.ToArray();
            }
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

            /// <summary>
            /// Index of the first metadata line in Tooltip - the "updated 15:43 - polled" tail.
            /// The hover card dims everything from here down so the numbers stay the thing your
            /// eye lands on. -1 means the whole tooltip is one message.
            /// </summary>
            public int TooltipMetaFrom = -1;
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

        // ---------- hairline ----------
        /// <summary>
        /// The calmest rendering: one thin line per visible metric, stacked along a screen edge,
        /// length encoding the fill. No text, no chrome, no box - a bezel rather than a widget.
        ///
        /// The track is drawn very faint rather than omitted: without it a 4% fill is a stub of
        /// light with no scale to read it against, and the eye cannot tell "nearly empty" from
        /// "not rendering".
        /// </summary>
        public static Rendered BuildHairline(Cfg cfg, Snapshot snap, string errorCode, DateTime lastOk)
        {
            var shown = new List<Bucket>();
            foreach (Bucket b in snap.Buckets)
            {
                RowCfg r = cfg.Row(b.Key);
                if (r != null && r.Visible && b.HasPercent) shown.Add(b);
            }

            bool vertical = cfg.HairlineEdge == "left" || cfg.HairlineEdge == "right";
            double t = cfg.HairlineThickness;

            var host = new StackPanel
            {
                Orientation = vertical ? Orientation.Horizontal : Orientation.Vertical,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };

            // Lines sit hard against the screen edge, so the stack order flips with the edge:
            // on a bottom edge the first metric should be the outermost line.
            bool reverse = cfg.HairlineEdge == "bottom" || cfg.HairlineEdge == "right";
            var ordered = new List<Bucket>(shown);
            if (reverse) ordered.Reverse();

            var tips = new List<string>();
            foreach (Bucket b in ordered)
            {
                RowCfg r = cfg.Row(b.Key);
                double pct = r.Invert ? 100 - b.Percent : b.Percent;
                Brush fill = r.Accent != null ? Theme.B(r.Accent) : Theme.PercentBrush(cfg, b.Percent, b.Severity);

                // Proportion is expressed as star-weighted grid tracks, NOT computed in a
                // SizeChanged handler. The handler version was wrong: the window is widened to
                // the monitor by SetWindowPos AFTER the content is built, and when no further
                // SizeChanged arrived the bar kept a width measured against the pre-resize
                // layout - a 61% reading rendered as 4%. Star sizing is resolved by layout at
                // whatever size the strip ends up, so it cannot drift out of sync.
                double p = Math.Max(0, Math.Min(100, pct));
                var lane = new Grid();
                if (vertical) { lane.Width = t; lane.HorizontalAlignment = HorizontalAlignment.Stretch; }
                else { lane.Height = t; lane.VerticalAlignment = VerticalAlignment.Stretch; }

                var track = new Border { Background = Theme.Brush_(cfg, Theme.Track), Opacity = 0.35 };
                lane.Children.Add(track);

                var bar = new Border { Background = fill };
                if (p > 0) { if (vertical) bar.MinHeight = 2; else bar.MinWidth = 2; }

                if (vertical)
                {
                    // A level fills upward, so the empty share is the top row.
                    lane.RowDefinitions.Add(new RowDefinition { Height = new GridLength(100 - p, GridUnitType.Star) });
                    lane.RowDefinitions.Add(new RowDefinition { Height = new GridLength(p, GridUnitType.Star) });
                    Grid.SetRowSpan(track, 2);
                    Grid.SetRow(bar, 1);
                }
                else
                {
                    lane.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(p, GridUnitType.Star) });
                    lane.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100 - p, GridUnitType.Star) });
                    Grid.SetColumnSpan(track, 2);
                    Grid.SetColumn(bar, 0);
                }
                lane.Children.Add(bar);
                host.Children.Add(lane);
            }

            // The line carries no text, so the tooltip is the only readout. Build it in the
            // metrics' configured order, not the reversed draw order.
            foreach (Bucket b in shown)
            {
                RowCfg r = cfg.Row(b.Key);
                string line = b.Label + "   " + PercentText(b, r);
                string reset = Fmt.Countdown(b.ResetsAt);
                if (reset.Length > 0) line += "   ·   " + string.Format(L.ResetsIn, reset);
                tips.Add(line);
            }
            if (tips.Count == 0) tips.Add(L.Loading);
            int metaFrom = tips.Count;
            tips.Add(string.Format(L.Updated, lastOk.ToString("HH:mm")) + "  — " +
                     (snap.Source == Provenance.Feed ? L.SourceLive : L.SourcePolled));
            if (errorCode != null)
                tips.Add(string.Format(L.FrozenFor, Fmt.Age(DateTime.Now - lastOk), Fmt.ErrorText(errorCode)));

            return new Rendered
            {
                Content = host,
                Tooltip = string.Join(Environment.NewLine, tips.ToArray()),
                TooltipMetaFrom = metaFrom
            };
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
                string line = b.Label + "   " + pct;
                if (reset.Length > 0) line += "   ·   " + string.Format(L.ResetsIn, reset);
                tips.Add(line);
            }

            int metaFrom = tips.Count;
            tips.Add(string.Format(L.Updated, lastOk.ToString("HH:mm")) + "  — " +
                     (snap.Source == Provenance.Feed ? L.SourceLive : L.SourcePolled));
            if (errorCode != null)
                tips.Add(string.Format(L.FrozenFor, Fmt.Age(DateTime.Now - lastOk), Fmt.ErrorText(errorCode)));

            return new Rendered
            {
                Content = host,
                Tooltip = string.Join(Environment.NewLine, tips.ToArray()),
                TooltipMetaFrom = metaFrom
            };
        }
    }
}
