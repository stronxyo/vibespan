// Palette and brush cache.
//
// Brushes are parsed once, frozen, and cached. The predecessor called
// `new BrushConverter().ConvertFromString(hex)` on every element of every row on every
// redraw, once a minute, forever.
//
// Threshold colours never carry meaning alone (WCAG 1.4.1): the gauge also draws a tick at
// the warning threshold and a glyph at critical, so the state survives colour blindness and
// greyscale. The "accessible" preset additionally swaps in the Okabe-Ito colour-blind-safe
// scale, whose luminance also rises monotonically.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Media;

namespace Vibespan
{
    public static class Theme
    {
        static readonly Dictionary<string, Brush> _cache = new Dictionary<string, Brush>(StringComparer.OrdinalIgnoreCase);

        public static Brush B(string hex)
        {
            if (string.IsNullOrEmpty(hex)) return Brushes.Transparent;
            Brush b;
            if (_cache.TryGetValue(hex, out b)) return b;
            try
            {
                var c = (Color)ColorConverter.ConvertFromString(hex);
                var sb = new SolidColorBrush(c);
                sb.Freeze();
                _cache[hex] = sb;
                return sb;
            }
            catch
            {
                _cache[hex] = Brushes.Magenta;   // visibly wrong beats silently invisible
                return Brushes.Magenta;
            }
        }

        public static Color C(string hex)
        {
            try { return (Color)ColorConverter.ConvertFromString(hex); }
            catch { return Colors.Magenta; }
        }

        /// <summary>Re-alpha a hex colour. Used to apply the background-alpha setting.</summary>
        public static string WithAlpha(string hex, double alpha)
        {
            Color c = C(hex);
            byte a = (byte)Math.Max(0, Math.Min(255, Math.Round(alpha * 255)));
            return "#" + a.ToString("X2", CultureInfo.InvariantCulture)
                       + c.R.ToString("X2", CultureInfo.InvariantCulture)
                       + c.G.ToString("X2", CultureInfo.InvariantCulture)
                       + c.B.ToString("X2", CultureInfo.InvariantCulture);
        }

        // ---------- tokens ----------
        public const string Background = "background";
        public const string Border = "border";
        public const string TextPrimary = "textPrimary";
        public const string TextSecondary = "textSecondary";
        public const string LabelColor = "label";
        public const string Track = "track";
        public const string Logo = "logo";

        public class Preset
        {
            public string Id, Name;
            public Dictionary<string, string> Tokens = new Dictionary<string, string>(StringComparer.Ordinal);
            public List<Stop> Stops = new List<Stop>();
        }

        static List<Preset> _presets;
        public static List<Preset> Presets
        {
            get { if (_presets == null) Build(); return _presets; }
        }

        static Preset Make(string id, string name,
                           string bg, string border, string text, string sec, string label, string track, string logo,
                           double s1, string c1, double s2, string c2, double s3, string c3)
        {
            var p = new Preset { Id = id, Name = name };
            p.Tokens[Background] = bg;
            p.Tokens[Border] = border;
            p.Tokens[TextPrimary] = text;
            p.Tokens[TextSecondary] = sec;
            p.Tokens[LabelColor] = label;
            p.Tokens[Track] = track;
            p.Tokens[Logo] = logo;
            p.Stops.Add(new Stop(s1, c1));
            p.Stops.Add(new Stop(s2, c2));
            p.Stops.Add(new Stop(s3, c3));
            return p;
        }

        static void Build()
        {
            _presets = new List<Preset>();

            // The look the predecessor established. Background is deliberately near-opaque:
            // against a white wallpaper an 80%-alpha dark plate composites to about #4C4D50,
            // where secondary text lands near 3.2:1 - under the 4.5:1 WCAG 1.4.3 needs.
            _presets.Add(Make("claude", "Claude",
                "#1E2029", "#22FFFFFF", "#F2F5F7", "#B8BCCB", "#6C7086", "#2A2D3A", "#DA7756",
                0, "#DA7756", 70, "#E8A33D", 90, "#E05252"));

            // Okabe-Ito: sky blue -> orange -> vermilion. Distinguishable under deuteranopia,
            // protanopia and tritanopia, and monotonic in luminance so it survives greyscale.
            _presets.Add(Make("accessible", "Accessible",
                "#161A1F", "#2AFFFFFF", "#F4F6F8", "#BFC6CE", "#7A838C", "#2B313A", "#56B4E9",
                0, "#56B4E9", 70, "#E69F00", 90, "#D55E00"));

            _presets.Add(Make("mono", "Mono",
                "#141518", "#22FFFFFF", "#F0F0F0", "#B0B0B0", "#707070", "#2A2A2E", "#D0D0D0",
                0, "#9A9A9A", 70, "#C8C8C8", 90, "#FFFFFF"));

            _presets.Add(Make("contrast", "High contrast",
                "#000000", "#FFFFFFFF", "#FFFFFF", "#FFFFFF", "#D0D0D0", "#3A3A3A", "#FFFFFF",
                0, "#00E5FF", 70, "#FFD400", 90, "#FF3B30"));
        }

        public static Preset FindPreset(string id)
        {
            foreach (Preset p in Presets) if (p.Id == id) return p;
            return Presets[0];
        }

        /// <summary>Resolve a token: per-user override first, then the active preset.</summary>
        public static string Token(Cfg cfg, string token)
        {
            string v;
            if (cfg.Overrides.TryGetValue(token, out v) && !string.IsNullOrEmpty(v)) return v;
            Preset p = FindPreset(cfg.ThemePreset);
            if (p.Tokens.TryGetValue(token, out v)) return v;
            return "#FF00FF";
        }

        public static Brush Brush_(Cfg cfg, string token) { return B(Token(cfg, token)); }

        /// <summary>
        /// Background with the user's alpha applied on top of the token's RGB.
        ///
        /// With the background switched off this returns #01000000 - one step off invisible -
        /// and NOT Transparent. AllowsTransparency makes a layered window whose hit-testing the
        /// OS performs from the alpha channel before the message ever reaches the wndproc, so a
        /// genuinely transparent panel cannot be dragged, right-clicked or resized: the widget
        /// would still be on screen but nothing could touch it. One unit of alpha is invisible
        /// to the eye and solid to the mouse. The grip uses the same trick.
        /// </summary>
        public static Brush BackgroundBrush(Cfg cfg)
        {
            if (!cfg.ShowBackground) return B("#01000000");
            return B(WithAlpha(Token(cfg, Background), cfg.BackgroundAlpha));
        }

        // ---------- threshold colour ----------
        static List<Stop> ActiveStops(Cfg cfg)
        {
            // A user who edited the stops keeps them; otherwise follow the preset so that
            // switching preset actually changes the gauge colours.
            if (cfg.Overrides.ContainsKey("stops")) return cfg.Stops;
            Preset p = FindPreset(cfg.ThemePreset);
            return p.Stops;
        }

        public static string PercentHex(Cfg cfg, double pct, string severity)
        {
            List<Stop> stops = ActiveStops(cfg);

            if (cfg.UseServerSeverity && !string.IsNullOrEmpty(severity))
            {
                string s = severity.ToLowerInvariant();
                if (s == "critical" || s == "severe") return stops[stops.Count - 1].Color;
                if (s == "warning" || s == "warn") return stops[Math.Min(1, stops.Count - 1)].Color;
                if (s == "normal") return stops[0].Color;
            }

            string chosen = stops[0].Color;
            foreach (Stop st in stops) if (pct >= st.At) chosen = st.Color;
            return chosen;
        }

        public static Brush PercentBrush(Cfg cfg, double pct, string severity)
        {
            return B(PercentHex(cfg, pct, severity));
        }

        /// <summary>The warning threshold, drawn as a tick on the bar so colour is not the only cue.</summary>
        public static double WarnAt(Cfg cfg)
        {
            List<Stop> stops = ActiveStops(cfg);
            return stops.Count >= 2 ? stops[1].At : 70;
        }
        public static double CriticalAt(Cfg cfg)
        {
            List<Stop> stops = ActiveStops(cfg);
            return stops.Count >= 3 ? stops[2].At : 90;
        }

        /// <summary>Swatches offered in the colour submenu.</summary>
        public static readonly string[] Swatches =
        {
            "#DA7756", "#E8A33D", "#E05252", "#C77DFF",
            "#56B4E9", "#009E73", "#E69F00", "#D55E00",
            "#7FB3D5", "#9BA0B5", "#F2F5F7", "#6C7086"
        };
    }
    /// <summary>
    /// Visual identity, kept deliberately separate from colour. A "theme" answers what colour
    /// things are; a "style" answers what shape they are - corner radius, density, how the bar
    /// is drawn, and which mark sits on the left. Two axes, because wanting a squarer, denser
    /// widget has nothing to do with wanting a colour-blind-safe palette.
    /// </summary>
    public class StylePreset
    {
        public string Id, Name;
        public double Radius;
        public double PadX, PadY;
        public double RowHeight;
        public double BarHeight;
        public double BarWidth;
        public string Bar;        // continuous | segmented | blocks
        public string Mark;       // asterisk | rail | dot | none
        public string Font;       // default family for this style
        public bool UpperLabels;
        public double LabelSize, PercentSize, ResetSize;
    }

    public static class Styles
    {
        static List<StylePreset> _all;

        public static List<StylePreset> All
        {
            get
            {
                if (_all != null) return _all;
                _all = new List<StylePreset>();

                // The default. Denser and squarer than the widget this project grew out of,
                // with a segmented bar and an accent rail instead of the Claude asterisk - the
                // asterisk is the single biggest reason two of these look like the same app.
                _all.Add(new StylePreset
                {
                    Id = "vibespan", Name = "Vibespan",
                    Radius = 3, PadX = 9, PadY = 4, RowHeight = 17,
                    BarHeight = 6, BarWidth = 54, Bar = "segmented", Mark = "rail",
                    Font = "Consolas", UpperLabels = true,
                    LabelSize = 8, PercentSize = 10.5, ResetSize = 8.5
                });

                _all.Add(new StylePreset
                {
                    Id = "classic", Name = "Classic",
                    Radius = 7, PadX = 8, PadY = 3, RowHeight = 18,
                    BarHeight = 4, BarWidth = 58, Bar = "continuous", Mark = "asterisk",
                    Font = "Segoe UI", UpperLabels = false,
                    LabelSize = 8.5, PercentSize = 10, ResetSize = 9
                });

                _all.Add(new StylePreset
                {
                    Id = "slim", Name = "Slim",
                    Radius = 2, PadX = 7, PadY = 1, RowHeight = 14,
                    BarHeight = 2, BarWidth = 46, Bar = "continuous", Mark = "dot",
                    Font = "Segoe UI", UpperLabels = false,
                    LabelSize = 8, PercentSize = 9, ResetSize = 8
                });

                _all.Add(new StylePreset
                {
                    Id = "card", Name = "Card",
                    Radius = 12, PadX = 13, PadY = 8, RowHeight = 21,
                    BarHeight = 5, BarWidth = 62, Bar = "continuous", Mark = "asterisk",
                    Font = "Segoe UI", UpperLabels = false,
                    LabelSize = 9, PercentSize = 11.5, ResetSize = 9.5
                });

                _all.Add(new StylePreset
                {
                    Id = "terminal", Name = "Terminal",
                    Radius = 0, PadX = 8, PadY = 4, RowHeight = 16,
                    BarHeight = 9, BarWidth = 50, Bar = "blocks", Mark = "none",
                    Font = "Consolas", UpperLabels = true,
                    LabelSize = 8.5, PercentSize = 10, ResetSize = 8.5
                });

                return _all;
            }
        }

        public static StylePreset Find(string id)
        {
            foreach (StylePreset p in All) if (p.Id == id) return p;
            return All[0];
        }

        /// <summary>Active style with any per-user overrides folded in.</summary>
        public static StylePreset For(Cfg cfg)
        {
            StylePreset p = Find(cfg.Style);
            if (string.IsNullOrEmpty(cfg.BarStyle) && string.IsNullOrEmpty(cfg.Mark)) return p;

            // Copy so the shared preset is never mutated.
            var o = new StylePreset
            {
                Id = p.Id, Name = p.Name, Radius = p.Radius, PadX = p.PadX, PadY = p.PadY,
                RowHeight = p.RowHeight, BarHeight = p.BarHeight, BarWidth = p.BarWidth,
                Bar = p.Bar, Mark = p.Mark, Font = p.Font, UpperLabels = p.UpperLabels,
                LabelSize = p.LabelSize, PercentSize = p.PercentSize, ResetSize = p.ResetSize
            };
            if (!string.IsNullOrEmpty(cfg.BarStyle)) o.Bar = cfg.BarStyle;
            if (!string.IsNullOrEmpty(cfg.Mark)) o.Mark = cfg.Mark;
            return o;
        }

        public static readonly string[] BarStyles = { "continuous", "segmented", "blocks" };
        public static readonly string[] Marks = { "asterisk", "rail", "dot", "none" };
    }

    public static class FontChoices
    {
        // Curated rather than "every installed font": a menu is not a font browser, and most
        // of what is installed is unusable at 9px. Anything missing is filtered out, and
        // "More fonts..." opens the real dialog for everything else.
        static readonly string[] Candidates =
        {
            "Segoe UI", "Segoe UI Variable Text", "Consolas", "Cascadia Mono", "Cascadia Code",
            "JetBrains Mono", "Fira Code", "IBM Plex Mono", "Inter", "Roboto", "Roboto Mono",
            "Tahoma", "Verdana", "Trebuchet MS", "Lucida Console", "Courier New", "Arial"
        };

        static List<string> _available;
        public static List<string> Available
        {
            get
            {
                if (_available != null) return _available;
                _available = new List<string>();
                var installed = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    foreach (FontFamily f in System.Windows.Media.Fonts.SystemFontFamilies)
                    {
                        string n = f.Source;
                        int slash = n.LastIndexOf('#');
                        if (slash >= 0) n = n.Substring(slash + 1);
                        installed[n] = true;
                    }
                }
                catch { }
                foreach (string c in Candidates) if (installed.ContainsKey(c)) _available.Add(c);
                if (_available.Count == 0) _available.Add("Segoe UI");
                return _available;
            }
        }

        public static FontFamily Resolve(Cfg cfg)
        {
            string name = !string.IsNullOrEmpty(cfg.Font) ? cfg.Font : Styles.For(cfg).Font;
            try { return new FontFamily(name); }
            catch { return new FontFamily("Segoe UI"); }
        }
    }
}
