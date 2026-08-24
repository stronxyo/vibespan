// Settings model, load/save.
//
// Lives in %LOCALAPPDATA%, not %APPDATA%: it stores monitor coordinates, a per-monitor
// scale and a monitor device name, all of which are actively hostile to a roaming profile.
// LOCALAPPDATA is also never OneDrive-synced.
//
// Every value goes through Normalize() after loading. That is not defensive noise - the
// predecessor used DataContractJsonSerializer, which skips field initializers, so a setting
// absent from an older file silently read back as default(T). A missing "scale" would have
// become 0 and rendered the widget at zero size. Parsing with explicit fallbacks plus one
// clamping pass removes that whole class of bug.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Vibespan
{
    public class Stop
    {
        public double At;
        public string Color;
        public Stop() { }
        public Stop(double at, string color) { At = at; Color = color; }
    }

    public class RowCfg
    {
        public string Key;
        public bool Visible;
        public List<string> Slots = new List<string>();   // label / percent / bar / reset
        public string ResetFormat = "countdown";          // countdown | clock | off
        public bool Invert;                               // show remaining instead of used
        public string Accent;                             // null = follow theme
        public string CustomLabel;                        // null = derived from the key

        public bool Has(string slot) { return Slots.Contains(slot); }

        public void SetSlots(params string[] slots)
        {
            Slots.Clear();
            foreach (string s in slots) Slots.Add(s);
        }

        // Presets cover the content columns only. The label ("5h", "7d") is deliberately NOT
        // part of them - wanting to drop the title is independent of wanting a bar, and
        // folding it in would double the preset count for no gain.
        public static readonly string[] PresetNames =
        {
            "Percent only", "Bar only", "Percent + Bar",
            "Percent + Bar + Remaining", "Bar + Remaining", "Percent + Remaining"
        };
        public static string[] PresetSlots(int i)
        {
            switch (i)
            {
                case 0: return new[] { "percent" };
                case 1: return new[] { "bar" };
                case 2: return new[] { "percent", "bar" };
                case 3: return new[] { "percent", "bar", "reset" };
                case 4: return new[] { "bar", "reset" };
                default: return new[] { "percent", "reset" };
            }
        }

        public bool ShowLabel
        {
            get { return Has("label"); }
            set
            {
                if (value) { if (!Has("label")) Slots.Insert(0, "label"); }
                else Slots.Remove("label");
            }
        }

        /// <summary>Apply a preset while preserving whether the label is shown.</summary>
        public void ApplyPreset(int i)
        {
            bool label = ShowLabel;
            SetSlots(PresetSlots(i));
            ShowLabel = label;
        }

        /// <summary>Index of the preset matching the current content slots, or -1 if custom.</summary>
        public int PresetIndex()
        {
            for (int i = 0; i < PresetNames.Length; i++)
            {
                string[] want = PresetSlots(i);
                int mine = Slots.Count - (ShowLabel ? 1 : 0);
                if (want.Length != mine) continue;
                bool same = true;
                for (int k = 0; k < want.Length; k++) if (!Slots.Contains(want[k])) { same = false; break; }
                if (same) return i;
            }
            return -1;
        }
    }

    public class Cfg
    {
        public const int CurrentSchema = 1;
        public int SchemaVersion = CurrentSchema;

        // ---- window ----
        public double X = double.NaN, Y = double.NaN;   // physical virtual-desktop pixels
        public string Monitor;                          // device name it was last placed on
        public double Scale = 1.0;
        public double ContentOpacity = 1.0;
        public double BackgroundAlpha = 0.95;
        public string Orientation = "horizontal";       // horizontal | vertical
        public bool ClickThrough = false;
        public bool HideFullScreen = true;
        public bool ShowLogo = true;
        public bool ShowBorder = true;
        public bool ShowBackground = true;

        // ---- display mode ----
        // Not a Style and not an Orientation: in hairline mode the window stops being a box
        // with rows and becomes a strip spanning a screen edge. Different geometry, different
        // positioning, no drag - so it gets its own axis rather than being crammed into the
        // style list.
        public string Mode = "widget";          // widget | hairline
        public string HairlineEdge = "bottom";  // bottom | top | left | right
        public double HairlineThickness = 2;

        public bool IsHairline { get { return Mode == "hairline"; } }

        // ---- appearance ----
        // Style is geometry and identity, Theme is colour. Separate axes on purpose.
        public string Style = "vibespan";
        public string Font = "";        // empty = whatever the style specifies
        public string BarStyle = "";    // empty = whatever the style specifies
        public string Mark = "";        // empty = whatever the style specifies

        // ---- theme ----
        public string ThemePreset = "claude";
        public Dictionary<string, string> Overrides = new Dictionary<string, string>(StringComparer.Ordinal);
        public List<Stop> Stops = new List<Stop>();
        public bool UseServerSeverity = false;

        // ---- rows ----
        public List<RowCfg> Rows = new List<RowCfg>();

        // ---- data ----
        public int PollSeconds = 300;
        public bool UseLiveFeed = false;

        // ---- alerts ----
        public List<int> AlertLevels = new List<int>();
        public bool AlertSound = false;
        public long MutedUntilUnix = 0;

        public string Lang = "en";

        public const string AppName = "Vibespan";

        // --config points this somewhere else. Without it, a --demo instance run for
        // screenshots or tests writes over the settings of a real install, and there is no
        // way to notice until the widget restarts with somebody else's test layout.
        static string _dirOverride;
        public static void UseDir(string dir) { _dirOverride = dir; }

        public static string Dir
        {
            get
            {
                if (!string.IsNullOrEmpty(_dirOverride)) return _dirOverride;
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppName);
            }
        }
        public static string Path_ { get { return Path.Combine(Dir, "settings.json"); } }
        public static string LogPath { get { return Path.Combine(Dir, "log.txt"); } }
        public static string FeedPath { get { return Path.Combine(Dir, "feed.json"); } }
        public static string TokenCachePath { get { return Path.Combine(Dir, "tokens.json"); } }

        public bool IsMuted
        {
            get { return MutedUntilUnix > DateTimeOffset.UtcNow.ToUnixTimeSeconds(); }
        }

        public RowCfg Row(string key)
        {
            foreach (RowCfg r in Rows) if (r.Key == key) return r;
            return null;
        }

        /// <summary>Add a row for a newly discovered bucket. Only the two core rows start visible.</summary>
        public RowCfg EnsureRow(string key)
        {
            RowCfg r = Row(key);
            if (r != null) return r;
            r = new RowCfg { Key = key, Visible = (key == "session" || key == "weekly") };
            r.SetSlots(RowCfg.PresetSlots(3));
            r.ShowLabel = true;              // presets no longer carry the label
            Rows.Add(r);
            return r;
        }

        // ---------- defaults / clamping ----------
        public void Normalize()
        {
            if (Scale < 0.6 || Scale > 3.0 || double.IsNaN(Scale)) Scale = 1.0;
            if (ContentOpacity < 0.2 || ContentOpacity > 1.0 || double.IsNaN(ContentOpacity)) ContentOpacity = 1.0;
            if (BackgroundAlpha < 0.2 || BackgroundAlpha > 1.0 || double.IsNaN(BackgroundAlpha)) BackgroundAlpha = 0.95;
            if (Orientation != "vertical") Orientation = "horizontal";
            if (PollSeconds < 180) PollSeconds = 180;      // the endpoint 429s below this
            if (PollSeconds > 3600) PollSeconds = 3600;
            if (string.IsNullOrEmpty(ThemePreset)) ThemePreset = "claude";
            if (Mode != "hairline") Mode = "widget";
            if (HairlineEdge != "top" && HairlineEdge != "left" && HairlineEdge != "right") HairlineEdge = "bottom";
            if (HairlineThickness < 1 || HairlineThickness > 8 || double.IsNaN(HairlineThickness)) HairlineThickness = 2;
            if (string.IsNullOrEmpty(Style)) Style = "vibespan";
            if (Font == null) Font = "";
            if (BarStyle == null) BarStyle = "";
            if (Mark == null) Mark = "";
            if (string.IsNullOrEmpty(Lang)) Lang = "en";
            if (Overrides == null) Overrides = new Dictionary<string, string>(StringComparer.Ordinal);
            if (Rows == null) Rows = new List<RowCfg>();
            if (AlertLevels == null) AlertLevels = new List<int>();
            if (AlertLevels.Count == 0) AlertLevels.Add(95);
            if (Stops == null || Stops.Count == 0)
            {
                Stops = new List<Stop>();
                Stops.Add(new Stop(0, "#DA7756"));
                Stops.Add(new Stop(70, "#E8A33D"));
                Stops.Add(new Stop(90, "#E05252"));
            }
            Stops.Sort(delegate (Stop a, Stop b) { return a.At.CompareTo(b.At); });

            foreach (RowCfg r in Rows)
            {
                if (r.Slots == null || r.Slots.Count == 0) { r.SetSlots(RowCfg.PresetSlots(3)); r.ShowLabel = true; }
                if (r.ResetFormat != "clock" && r.ResetFormat != "off") r.ResetFormat = "countdown";
            }
            if (Rows.Count == 0) { EnsureRow("session"); EnsureRow("weekly"); }
        }

        // ---------- load ----------
        public static Cfg Load()
        {
            var c = new Cfg();
            try
            {
                if (!File.Exists(Path_)) { c.Normalize(); return c; }
                JNode j = JsonReader.Parse(File.ReadAllText(Path_));
                if (!j.Exists) { c.Normalize(); return c; }

                int ver = j["schemaVersion"].AsInt(0);
                if (ver > CurrentSchema)
                {
                    // Never half-parse a file from a newer build: back it up and start clean,
                    // so downgrading is not destructive.
                    try { File.Copy(Path_, Path_ + ".v" + ver + ".bak", true); } catch { }
                    c.Normalize();
                    return c;
                }

                JNode w = j["window"];
                c.X = w["x"].AsDouble(double.NaN);
                c.Y = w["y"].AsDouble(double.NaN);
                c.Monitor = w["monitor"].AsString(null);
                c.Scale = w["scale"].AsDouble(1.0);
                c.ContentOpacity = w["contentOpacity"].AsDouble(1.0);
                c.BackgroundAlpha = w["backgroundAlpha"].AsDouble(0.95);
                c.Orientation = w["orientation"].AsString("horizontal");
                c.ClickThrough = w["clickThrough"].AsBool(false);
                c.HideFullScreen = w["hideFullScreen"].AsBool(true);
                c.ShowLogo = w["showLogo"].AsBool(true);
                c.ShowBackground = w["showBackground"].AsBool(true);
                c.ShowBorder = w["showBorder"].AsBool(true);

                c.Mode = w["mode"].AsString("widget");
                c.HairlineEdge = w["hairlineEdge"].AsString("bottom");
                c.HairlineThickness = w["hairlineThickness"].AsDouble(2);
                c.Style = w["style"].AsString("vibespan");
                c.Font = w["font"].AsString("");
                c.BarStyle = w["barStyle"].AsString("");
                c.Mark = w["mark"].AsString("");

                JNode t = j["theme"];
                c.ThemePreset = t["preset"].AsString("claude");
                c.UseServerSeverity = t["useServerSeverity"].AsBool(false);
                JNode ov = t["overrides"];
                foreach (string k in ov.Fields)
                {
                    string v = ov[k].AsString(null);
                    if (v != null) c.Overrides[k] = v;
                }
                JNode st = t["stops"];
                for (int i = 0; i < st.Count; i++)
                {
                    string col = st[i]["color"].AsString(null);
                    if (col != null) c.Stops.Add(new Stop(st[i]["at"].AsDouble(0), col));
                }

                JNode rows = j["rows"];
                for (int i = 0; i < rows.Count; i++)
                {
                    JNode r = rows[i];
                    string key = r["key"].AsString(null);
                    if (string.IsNullOrEmpty(key)) continue;
                    var rc = new RowCfg
                    {
                        Key = key,
                        Visible = r["visible"].AsBool(true),
                        ResetFormat = r["resetFormat"].AsString("countdown"),
                        Invert = r["invert"].AsBool(false),
                        Accent = r["accent"].AsString(null),
                        CustomLabel = r["customLabel"].AsString(null)
                    };
                    JNode sl = r["slots"];
                    for (int k = 0; k < sl.Count; k++)
                    {
                        string s = sl[k].AsString(null);
                        if (s != null) rc.Slots.Add(s);
                    }
                    c.Rows.Add(rc);
                }

                JNode d = j["data"];
                c.PollSeconds = d["pollSeconds"].AsInt(300);
                c.UseLiveFeed = d["useLiveFeed"].AsBool(false);

                JNode a = j["alerts"];
                JNode lv = a["levels"];
                for (int i = 0; i < lv.Count; i++)
                {
                    int n = lv[i].AsInt(-1);
                    if (n > 0 && n <= 100) c.AlertLevels.Add(n);
                }
                c.AlertSound = a["sound"].AsBool(false);
                c.MutedUntilUnix = a["mutedUntil"].AsLong(0);

                c.Lang = j["lang"].AsString("en");
            }
            catch { }
            c.Normalize();
            return c;
        }

        // ---------- save ----------
        public JNode ToJson()
        {
            JNode j = JNode.NewObject();
            j.Set("schemaVersion", CurrentSchema);

            JNode w = JNode.NewObject();
            if (!double.IsNaN(X)) w.Set("x", Math.Round(X));
            if (!double.IsNaN(Y)) w.Set("y", Math.Round(Y));
            if (!string.IsNullOrEmpty(Monitor)) w.Set("monitor", Monitor);
            w.Set("scale", Math.Round(Scale, 4));
            w.Set("contentOpacity", Math.Round(ContentOpacity, 3));
            w.Set("backgroundAlpha", Math.Round(BackgroundAlpha, 3));
            w.Set("orientation", Orientation);
            w.Set("clickThrough", ClickThrough);
            w.Set("hideFullScreen", HideFullScreen);
            w.Set("showLogo", ShowLogo);
            w.Set("showBackground", ShowBackground);
            w.Set("showBorder", ShowBorder);
            w.Set("mode", Mode);
            w.Set("hairlineEdge", HairlineEdge);
            w.Set("hairlineThickness", Math.Round(HairlineThickness, 2));
            w.Set("style", Style);
            w.Set("font", Font);
            w.Set("barStyle", BarStyle);
            w.Set("mark", Mark);
            j.Set("window", w);

            JNode t = JNode.NewObject();
            t.Set("preset", ThemePreset);
            t.Set("useServerSeverity", UseServerSeverity);
            JNode ov = JNode.NewObject();
            foreach (KeyValuePair<string, string> kv in Overrides) ov.Set(kv.Key, kv.Value);
            t.Set("overrides", ov);
            JNode st = JNode.NewArray();
            foreach (Stop s in Stops)
            {
                JNode o = JNode.NewObject();
                o.Set("at", s.At); o.Set("color", s.Color);
                st.Add(o);
            }
            t.Set("stops", st);
            j.Set("theme", t);

            JNode rows = JNode.NewArray();
            foreach (RowCfg r in Rows)
            {
                JNode o = JNode.NewObject();
                o.Set("key", r.Key);
                o.Set("visible", r.Visible);
                JNode sl = JNode.NewArray();
                foreach (string s in r.Slots) sl.Add(JNode.Str(s));
                o.Set("slots", sl);
                o.Set("resetFormat", r.ResetFormat);
                o.Set("invert", r.Invert);
                if (r.Accent != null) o.Set("accent", r.Accent);
                if (r.CustomLabel != null) o.Set("customLabel", r.CustomLabel);
                rows.Add(o);
            }
            j.Set("rows", rows);

            JNode d = JNode.NewObject();
            d.Set("pollSeconds", PollSeconds);
            d.Set("useLiveFeed", UseLiveFeed);
            j.Set("data", d);

            JNode a = JNode.NewObject();
            JNode lv = JNode.NewArray();
            foreach (int n in AlertLevels) lv.Add(JNode.Num(n));
            a.Set("levels", lv);
            a.Set("sound", AlertSound);
            a.Set("mutedUntil", MutedUntilUnix);
            j.Set("alerts", a);

            j.Set("lang", Lang);
            return j;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                string text = ToJson().ToPretty();

                // The temp file MUST be in the same directory. A %TEMP% temp file is often on
                // another volume, which silently degrades File.Replace from an atomic rename
                // into copy-and-delete.
                string tmp = Path_ + ".tmp";
                File.WriteAllText(tmp, text, new System.Text.UTF8Encoding(false));
                if (File.Exists(Path_))
                {
                    string bak = Path_ + ".bak";
                    File.Replace(tmp, Path_, bak);
                    try { File.Delete(bak); } catch { }
                }
                else File.Move(tmp, Path_);   // Replace requires an existing target
            }
            catch (Exception e) { Log.Write("config save failed: " + e.Message); }
        }
    }

    /// <summary>Diagnostics. English only - a log people paste into an issue must not need translating.</summary>
    public static class Log
    {
        static readonly object _lock = new object();     // written from the UI and poll threads

        public static void Write(string msg)
        {
            lock (_lock)
            {
                try
                {
                    Directory.CreateDirectory(Cfg.Dir);
                    var fi = new FileInfo(Cfg.LogPath);
                    if (fi.Exists && fi.Length > 128 * 1024)
                    {
                        string[] lines = File.ReadAllLines(Cfg.LogPath);
                        var keep = new string[lines.Length / 2];
                        Array.Copy(lines, lines.Length - keep.Length, keep, 0, keep.Length);
                        File.WriteAllLines(Cfg.LogPath, keep);
                    }
                    File.AppendAllText(Cfg.LogPath,
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
                        + "  " + msg + Environment.NewLine);
                }
                catch { }
            }
        }
    }
}
