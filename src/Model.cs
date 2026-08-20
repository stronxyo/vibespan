// Usage model.
//
// The whole point of this file is that rows are DISCOVERED from the response, not hardcoded.
// The endpoint returns the same information two ways and neither is guaranteed to be present:
//
//   1. a generic "limits" array  - {kind, group, percent, severity, resets_at, scope, is_active}
//      This looks like a recent rollout; no public write-up of it exists yet.
//   2. flat named keys           - five_hour, seven_day, seven_day_opus, seven_day_sonnet,
//      seven_day_oauth_apps, seven_day_cowork, nimbus_quill, tangelo, cinder_cove, ...
//
// Both are merged onto canonical keys. The array wins for percent/severity/is_active because
// it carries the server's own severity signal and says which limit is currently binding; the
// named keys fill in reset times and dollar figures. Anything unrecognised still renders under
// a prettified name instead of being silently dropped - Anthropic has changed these buckets
// repeatedly and a fixed struct goes stale within weeks.
using System;
using System.Collections.Generic;
using System.Globalization;

namespace Vibespan
{
    public enum Provenance { None, Poll, Feed, Cache }

    public class Bucket
    {
        public string Key;                  // canonical, stable, used by config
        public string Label;                // short display label, e.g. "5h"
        public string LongLabel;            // tooltip label, e.g. "5-hour session"
        public double Percent;
        public bool HasPercent;
        public DateTimeOffset? ResetsAt;
        public string Severity;             // server's own signal: normal / warning / ...
        public bool IsActive;               // currently the binding limit
        public double? UsedDollars, LimitDollars;
        public int Order;                   // discovery order, for stable listing

        public string ResetIso
        {
            get { return ResetsAt.HasValue ? ResetsAt.Value.ToString("o", CultureInfo.InvariantCulture) : null; }
        }
    }

    public class Snapshot
    {
        public List<Bucket> Buckets = new List<Bucket>();
        public Provenance Source = Provenance.None;
        public DateTime Taken = DateTime.Now;

        public Bucket Find(string key)
        {
            foreach (Bucket b in Buckets) if (b.Key == key) return b;
            return null;
        }

        // ---------- canonical key mapping ----------

        static string KeyForNamed(string field)
        {
            switch (field)
            {
                case "five_hour": return "session";
                case "seven_day": return "weekly";
                case "seven_day_opus": return "weekly:Opus";
                case "seven_day_sonnet": return "weekly:Sonnet";
                case "seven_day_fable": return "weekly:Fable";
                case "seven_day_oauth_apps": return "oauth_apps";
                case "seven_day_cowork": return "cowork";
                default: return field;
            }
        }

        static string KeyForLimit(JNode lim)
        {
            string kind = lim["kind"].AsString("");
            if (kind == "session") return "session";
            if (kind == "weekly_all") return "weekly";
            if (kind == "weekly_scoped")
            {
                string model = lim["scope"]["model"]["display_name"].AsString(null);
                if (!string.IsNullOrEmpty(model)) return "weekly:" + model;
                string surface = lim["scope"]["surface"].AsString(null);
                if (!string.IsNullOrEmpty(surface)) return "weekly:" + surface;
                return "weekly_scoped";
            }
            return string.IsNullOrEmpty(kind) ? "unknown" : kind;
        }

        // ---------- labels ----------

        public static void LabelFor(string key, out string shortLabel, out string longLabel)
        {
            if (key == "session") { shortLabel = "5h"; longLabel = "5-hour session"; return; }
            if (key == "weekly") { shortLabel = "7d"; longLabel = "7-day, all models"; return; }
            if (key == "credits") { shortLabel = "$"; longLabel = "Extra usage credits"; return; }
            if (key == "spend") { shortLabel = "$"; longLabel = "Spend"; return; }
            if (key == "oauth_apps") { shortLabel = "app"; longLabel = "7-day, OAuth apps"; return; }
            if (key == "cowork") { shortLabel = "cw"; longLabel = "7-day, Cowork"; return; }
            if (key.StartsWith("weekly:", StringComparison.Ordinal))
            {
                string model = key.Substring(7);
                // Model names are proper nouns - keep their casing, and only clip the long
                // ones. "Fable" reading as "fab" looks like a bug.
                shortLabel = model.Length <= 6 ? model : model.Substring(0, 5);
                longLabel = "7-day, " + model;
                return;
            }
            longLabel = Prettify(key);
            shortLabel = Shorten(longLabel);
        }

        // "seven_day_omelette" -> "7-day omelette"
        static string Prettify(string raw)
        {
            string s = raw.Replace('_', ' ');
            s = s.Replace("seven day", "7-day").Replace("five hour", "5-hour");
            if (s.Length > 0) s = char.ToUpper(s[0], CultureInfo.InvariantCulture) + s.Substring(1);
            return s;
        }

        static string Shorten(string s)
        {
            if (string.IsNullOrEmpty(s)) return "?";
            if (s.Length <= 4) return s.ToLowerInvariant();
            return s.Substring(0, 3).ToLowerInvariant();
        }

        // ---------- parsing ----------

        static DateTimeOffset? ParseReset(JNode n)
        {
            if (n == null || n.IsNull) return null;
            if (n.Kind == JKind.Number)          // statusline feed uses unix epoch seconds
            {
                try { return DateTimeOffset.FromUnixTimeSeconds(n.AsLong(0)); }
                catch { return null; }
            }
            string s = n.AsString(null);
            if (string.IsNullOrEmpty(s)) return null;
            DateTimeOffset d;
            if (DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture,
                                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out d))
                return d;
            return null;
        }

        Bucket Ensure(string key, Dictionary<string, Bucket> index)
        {
            Bucket b;
            if (index.TryGetValue(key, out b)) return b;
            b = new Bucket { Key = key, Order = index.Count };
            string s, l;
            LabelFor(key, out s, out l);
            b.Label = s; b.LongLabel = l;
            index[key] = b;
            Buckets.Add(b);
            return b;
        }

        /// <summary>Parse the /api/oauth/usage response.</summary>
        public static Snapshot FromUsage(JNode root)
        {
            var snap = new Snapshot { Source = Provenance.Poll };
            if (root == null || !root.Exists) return snap;
            var index = new Dictionary<string, Bucket>(StringComparer.Ordinal);

            // 1. flat named keys first, so the array can overwrite the authoritative fields
            foreach (string field in root.Fields)
            {
                if (field == "limits" || field == "extra_usage" || field == "spend") continue;
                JNode v = root[field];
                if (v.Kind != JKind.Object) continue;
                JNode util = v["utilization"];
                if (!util.Exists) continue;

                Bucket b = snap.Ensure(KeyForNamed(field), index);
                b.Percent = Clamp(util.AsNumberLoose(0));
                b.HasPercent = true;
                b.ResetsAt = ParseReset(v["resets_at"]);
                if (v["used_dollars"].Exists) b.UsedDollars = v["used_dollars"].AsNumberLoose(0);
                if (v["limit_dollars"].Exists) b.LimitDollars = v["limit_dollars"].AsNumberLoose(0);
            }

            // 2. the generic array wins where present
            JNode limits = root["limits"];
            for (int i = 0; i < limits.Count; i++)
            {
                JNode lim = limits[i];
                Bucket b = snap.Ensure(KeyForLimit(lim), index);
                if (lim["percent"].Exists) { b.Percent = Clamp(lim["percent"].AsNumberLoose(0)); b.HasPercent = true; }
                b.Severity = lim["severity"].AsString(b.Severity);
                b.IsActive = lim["is_active"].AsBool(b.IsActive);
                DateTimeOffset? r = ParseReset(lim["resets_at"]);
                if (r.HasValue) b.ResetsAt = r;
            }

            // 3. synthetic money rows
            JNode extra = root["extra_usage"];
            if (extra.Kind == JKind.Object && extra["utilization"].Exists)
            {
                Bucket b = snap.Ensure("credits", index);
                b.Percent = Clamp(extra["utilization"].AsNumberLoose(0));
                b.HasPercent = true;
                if (extra["used_credits"].Exists) b.UsedDollars = extra["used_credits"].AsNumberLoose(0);
                if (extra["monthly_limit"].Exists) b.LimitDollars = extra["monthly_limit"].AsNumberLoose(0) / 100.0;
            }
            JNode spend = root["spend"];
            if (spend.Kind == JKind.Object && spend["percent"].Exists)
            {
                Bucket b = snap.Ensure("spend", index);
                b.Percent = Clamp(spend["percent"].AsNumberLoose(0));
                b.HasPercent = true;
                b.Severity = spend["severity"].AsString(null);
                double exp = spend["used"]["exponent"].AsDouble(2);
                double div = Math.Pow(10, exp);
                if (spend["used"]["amount_minor"].Exists) b.UsedDollars = spend["used"]["amount_minor"].AsNumberLoose(0) / div;
                if (spend["limit"]["amount_minor"].Exists) b.LimitDollars = spend["limit"]["amount_minor"].AsNumberLoose(0) / div;
            }

            return snap;
        }

        /// <summary>
        /// Parse the rate_limits block Claude Code pushes to a statusline command. Different
        /// shape from the polled endpoint: "used_percentage" not "utilization", and resets_at
        /// is a unix epoch integer rather than an ISO-8601 string.
        /// </summary>
        public static Snapshot FromFeed(JNode rateLimits)
        {
            var snap = new Snapshot { Source = Provenance.Feed };
            if (rateLimits == null || !rateLimits.Exists) return snap;
            var index = new Dictionary<string, Bucket>(StringComparer.Ordinal);

            string[] fields = { "five_hour", "seven_day" };
            foreach (string f in fields)
            {
                JNode v = rateLimits[f];
                if (v.Kind != JKind.Object) continue;
                JNode pct = v["used_percentage"];
                if (!pct.Exists) pct = v["utilization"];
                if (!pct.Exists) continue;

                Bucket b = snap.Ensure(KeyForNamed(f), index);
                b.Percent = Clamp(pct.AsNumberLoose(0));
                b.HasPercent = true;
                b.ResetsAt = ParseReset(v["resets_at"]);
            }
            return snap;
        }

        static double Clamp(double d) { return d < 0 ? 0 : (d > 100 ? 100 : d); }
    }
}
