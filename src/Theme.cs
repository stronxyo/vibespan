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

        /// <summary>Background with the user's alpha applied on top of the token's RGB.</summary>
        public static Brush BackgroundBrush(Cfg cfg)
        {
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
}
