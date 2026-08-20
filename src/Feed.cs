// The opt-in live feed.
//
// Claude Code pushes a JSON blob to its configured statusLine command on every turn, and for
// Claude.ai subscribers that blob contains rate_limits - the same 5-hour and 7-day numbers the
// widget otherwise polls for, except pushed, unauthenticated and with no rate limit at all.
// That makes it strictly better than the undocumented endpoint whenever Claude Code is running.
//
// Wiring it means writing a statusLine key into the user's own ~/.claude/settings.json, so it
// is opt-in, reversible, and refuses to touch a statusLine somebody else configured.
//
// Because Claude Code renders whatever we print, running as the statusline also has to LEAVE a
// status line - printing nothing would blank a UI element the user can see. So this prints the
// usage summary, and the user gets a statusline out of the deal.
using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace Vibespan
{
    public static class Feed
    {
        public static string SettingsPath
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                                    ".claude\\settings.json");
            }
        }

        static string ExePath { get { return System.Reflection.Assembly.GetExecutingAssembly().Location; } }

        public enum FeedState { Off, OwnedByUs, ForeignStatusLine }

        public static FeedState Detect()
        {
            try
            {
                if (!File.Exists(SettingsPath)) return FeedState.Off;
                JNode j = JsonReader.Parse(File.ReadAllText(SettingsPath));
                JNode sl = j["statusLine"];
                if (!sl.Exists) return FeedState.Off;
                string cmd = sl["command"].AsString("");
                if (cmd.IndexOf("Vibespan", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    cmd.IndexOf("--feed", StringComparison.OrdinalIgnoreCase) >= 0)
                    return FeedState.OwnedByUs;
                return FeedState.ForeignStatusLine;
            }
            catch { return FeedState.Off; }
        }

        /// <summary>Write our statusLine key. Refuses if somebody else already owns the slot.</summary>
        public static bool Enable()
        {
            try
            {
                FeedState st = Detect();
                if (st == FeedState.ForeignStatusLine) return false;

                JNode j = File.Exists(SettingsPath)
                    ? JsonReader.Parse(File.ReadAllText(SettingsPath))
                    : JNode.NewObject();
                if (j.Kind != JKind.Object) j = JNode.NewObject();

                JNode sl = JNode.NewObject();
                sl.Set("type", "command");
                sl.Set("command", "\"" + ExePath + "\" --feed");
                sl.Set("padding", 0);
                j.Set("statusLine", sl);

                WriteSettings(j);
                Log.Write("live feed enabled (statusLine written)");
                return true;
            }
            catch (Exception e) { Log.Write("feed enable failed: " + e.Message); return false; }
        }

        /// <summary>Remove the statusLine key, but only if it is ours.</summary>
        public static bool Disable()
        {
            try
            {
                if (Detect() != FeedState.OwnedByUs) return false;
                JNode j = JsonReader.Parse(File.ReadAllText(SettingsPath));
                if (j.Kind != JKind.Object) return false;

                JNode clean = JNode.NewObject();
                foreach (string k in j.Fields)
                    if (k != "statusLine") clean.Set(k, j[k]);

                WriteSettings(clean);
                try { File.Delete(Cfg.FeedPath); } catch { }
                Log.Write("live feed disabled (statusLine removed)");
                return true;
            }
            catch (Exception e) { Log.Write("feed disable failed: " + e.Message); return false; }
        }

        static void WriteSettings(JNode j)
        {
            // Same-directory temp file: File.Replace degrades to copy-and-delete across volumes.
            string tmp = SettingsPath + ".vibespan.tmp";
            string bak = SettingsPath + ".vibespan.bak";
            File.WriteAllText(tmp, j.ToPretty(), new UTF8Encoding(false));
            if (File.Exists(SettingsPath))
            {
                try
                {
                    File.Replace(tmp, SettingsPath, bak);
                    try { File.Delete(bak); } catch { }
                }
                catch
                {
                    File.Copy(tmp, SettingsPath, true);
                    try { File.Delete(tmp); } catch { }
                }
            }
            else File.Move(tmp, SettingsPath);
        }

        // ---------- --feed mode ----------
        /// <summary>
        /// Runs as Claude Code's statusline command: consume the pushed JSON on stdin, park the
        /// rate_limits block where the widget can see it, and print a status line.
        /// </summary>
        public static int RunAsStatusLine()
        {
            string input = "";
            try { input = Console.In.ReadToEnd(); } catch { }

            JNode j = JsonReader.Parse(input);
            JNode limits = j["rate_limits"];

            if (limits.Exists)
            {
                try
                {
                    Directory.CreateDirectory(Cfg.Dir);
                    JNode wrap = JNode.NewObject();
                    wrap.Set("rate_limits", limits);
                    wrap.Set("stamp", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                    string tmp = Cfg.FeedPath + ".tmp";
                    File.WriteAllText(tmp, wrap.ToString(), new UTF8Encoding(false));
                    if (File.Exists(Cfg.FeedPath)) File.Replace(tmp, Cfg.FeedPath, null);
                    else File.Move(tmp, Cfg.FeedPath);
                }
                catch { }
            }

            Console.Out.Write(StatusText(j, limits));
            return 0;
        }

        static string StatusText(JNode root, JNode limits)
        {
            var sb = new StringBuilder();
            string model = root["model"]["display_name"].AsString(null);
            if (!string.IsNullOrEmpty(model)) sb.Append(model).Append("  ");

            bool any = false;
            string[] keys = { "five_hour", "seven_day" };
            string[] labels = { "5h", "7d" };
            for (int i = 0; i < keys.Length; i++)
            {
                JNode v = limits[keys[i]];
                if (v.Kind != JKind.Object) continue;
                JNode pct = v["used_percentage"];
                if (!pct.Exists) pct = v["utilization"];
                if (!pct.Exists) continue;
                if (any) sb.Append(" | ");
                sb.Append(labels[i]).Append(' ')
                  .Append(((int)Math.Round(pct.AsNumberLoose(0))).ToString(CultureInfo.InvariantCulture))
                  .Append('%');
                any = true;
            }

            if (!any)
            {
                // rate_limits only appears for Claude.ai subscribers, and only after the first
                // API response of a session. Say something useful rather than nothing.
                string ctx = root["context_window"]["used_percentage"].AsString(null);
                if (ctx == null && root["context_window"]["used_percentage"].Exists)
                    ctx = ((int)Math.Round(root["context_window"]["used_percentage"].AsNumberLoose(0)))
                          .ToString(CultureInfo.InvariantCulture);
                sb.Append(ctx != null ? "ctx " + ctx + "%" : "vibespan");
            }
            return sb.ToString();
        }
    }
}
