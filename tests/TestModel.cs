// Data-layer test. Console app, no WPF. Run it after touching Json.cs or Model.cs.
//   csc /out:tests\TestModel.exe src\Json.cs src\Model.cs tests\TestModel.cs
using System;
using System.Globalization;
using System.IO;
using Vibespan;

public static class TestModel
{
    static int _fail, _pass;

    static void Check(bool ok, string what)
    {
        if (ok) { _pass++; Console.WriteLine("   ok    " + what); }
        else { _fail++; Console.WriteLine("   FAIL  " + what); }
    }

    static void Dump(Snapshot s)
    {
        foreach (Bucket b in s.Buckets)
        {
            string reset = b.ResetsAt.HasValue
                ? b.ResetsAt.Value.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) : "-";
            string money = b.UsedDollars.HasValue || b.LimitDollars.HasValue
                ? "  $" + (b.UsedDollars.HasValue ? b.UsedDollars.Value.ToString("0.00", CultureInfo.InvariantCulture) : "?")
                  + "/" + (b.LimitDollars.HasValue ? b.LimitDollars.Value.ToString("0.00", CultureInfo.InvariantCulture) : "?")
                : "";
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "      {0,-16} {1,-5} {2,5:0.0}%  {3,-16} sev={4,-8} active={5}{6}",
                b.Key, b.Label, b.Percent, reset, b.Severity ?? "-", b.IsActive ? "YES" : "no", money));
        }
    }

    static JNode Load(string name)
    {
        string p = Path.Combine("tests", Path.Combine("fixtures", name));
        return JsonReader.Parse(File.ReadAllText(p));
    }

    public static void Main()
    {
        Console.WriteLine("=== vibespan data layer ===");
        Console.WriteLine();

        // ---------- parser basics ----------
        Console.WriteLine("-- parser --");
        Check(JsonReader.Parse("{\"a\":1}")["a"].AsInt(0) == 1, "object member");
        Check(JsonReader.Parse("[1,2,3]").Count == 3, "array count");
        Check(JsonReader.Parse("{\"a\":{\"b\":\"c\"}}")["a"]["b"].AsString("") == "c", "nested");
        Check(JsonReader.Parse("{\"a\":null}")["a"].IsNull, "explicit null");
        Check(JsonReader.Parse("{\"a\":1}")["zz"]["yy"][3].IsNull, "missing path does not throw");
        Check(JsonReader.Parse("not json at all").IsNull, "garbage -> Null, no throw");
        Check(JsonReader.Parse("{\"a\":1,").Exists, "truncated object still yields what it read");
        Check(JsonReader.Parse("{\"s\":\"a\\\"b\\nc\"}")["s"].AsString("") == "a\"b\nc", "escapes");
        Check(JsonReader.Parse("{\"u\":\"\\u00e9\"}")["u"].AsString("") == "\u00e9", "\\u escape");
        Check(JsonReader.Parse("{\"n\":-1.5e2}")["n"].AsDouble(0) == -150.0, "exponent number");
        Check(JsonReader.Parse("{\"n\":\"42\"}")["n"].AsNumberLoose(0) == 42, "loose number from string");
        // ordering must not matter - this is exactly what DataContractJsonSerializer got wrong
        JNode ooo = JsonReader.Parse("{\"z\":1,\"a\":2,\"m\":3}");
        Check(ooo["a"].AsInt(0) == 2 && ooo["z"].AsInt(0) == 1, "out-of-order members");
        Console.WriteLine();

        // ---------- round trip ----------
        Console.WriteLine("-- writer --");
        JNode o = JNode.NewObject();
        o.Set("name", "vibe\"span");
        o.Set("scale", 1.25);
        o.Set("on", true);
        JNode arr = JNode.NewArray(); arr.Add(JNode.Num(1)); arr.Add(JNode.Str("two"));
        o.Set("list", arr);
        string txt = o.ToPretty();
        JNode back = JsonReader.Parse(txt);
        Check(back["name"].AsString("") == "vibe\"span", "round trip string with quote");
        Check(back["scale"].AsDouble(0) == 1.25, "round trip double");
        Check(back["on"].AsBool(false), "round trip bool");
        Check(back["list"][1].AsString("") == "two", "round trip array");
        Check(back.Fields[0] == "name" && back.Fields[3] == "list", "key order preserved");
        Console.WriteLine();

        // ---------- live shape ----------
        Console.WriteLine("-- usage-live.json (captured from the real endpoint) --");
        Snapshot live = Snapshot.FromUsage(Load("usage-live.json"));
        Dump(live);
        Check(live.Find("session") != null && live.Find("session").Percent == 5, "session = 5%");
        Check(live.Find("weekly") != null && live.Find("weekly").Percent == 17, "weekly = 17%");
        Check(live.Find("weekly").IsActive, "weekly is the binding limit");
        Check(!live.Find("session").IsActive, "session is not binding");
        Check(live.Find("weekly:Fable") != null, "model-scoped weekly discovered from limits[]");
        Check(live.Find("nimbus_quill") != null, "unknown named bucket still discovered");
        Check(live.Find("seven_day_opus") == null, "null buckets are skipped");
        Check(live.Find("credits") != null, "extra_usage becomes a credits row");
        Check(live.Find("spend") != null && live.Find("spend").LimitDollars == 50.0, "spend minor units -> $50.00");
        Check(live.Find("session").ResetsAt.HasValue, "reset parsed");
        Console.WriteLine();

        // ---------- legacy shape, no limits[] ----------
        Console.WriteLine("-- usage-legacy.json (flat keys only, no limits[]) --");
        Snapshot legacy = Snapshot.FromUsage(Load("usage-legacy.json"));
        Dump(legacy);
        Check(legacy.Find("session") != null && legacy.Find("session").Percent == 33, "session 33% without limits[]");
        Check(legacy.Find("weekly:Sonnet") != null, "seven_day_sonnet -> weekly:Sonnet");
        Check(legacy.Find("weekly:Opus") == null, "null opus skipped");
        Check(legacy.Find("credits") == null, "extra_usage with null utilization is not a row");
        Console.WriteLine();

        // ---------- invented future shape ----------
        Console.WriteLine("-- usage-future.json (unknown kinds + unknown buckets) --");
        Snapshot fut = Snapshot.FromUsage(Load("usage-future.json"));
        Dump(fut);
        Check(fut.Find("monthly_teleport") != null, "unknown limits[] kind still renders");
        Check(fut.Find("quantum_badger") != null, "unknown named bucket still renders");
        Check(fut.Find("weekly:Opus") != null, "scoped Opus from limits[]");
        Check(fut.Find("session").Severity == "warning", "server severity carried through");
        Check(fut.Find("weekly").Severity == "critical", "critical severity carried through");
        Check(fut.Find("session").UsedDollars == 12.5, "dollars from the named key");
        Check(fut.Find("session").IsActive, "is_active from limits[]");
        Console.WriteLine();

        // ---------- statusline feed ----------
        Console.WriteLine("-- feed.json (statusline rate_limits) --");
        Snapshot feed = Snapshot.FromFeed(Load("feed.json")["rate_limits"]);
        Dump(feed);
        Check(feed.Source == Provenance.Feed, "provenance marked as feed");
        Check(feed.Find("session") != null && Math.Abs(feed.Find("session").Percent - 23.5) < 0.01, "used_percentage read");
        Check(feed.Find("session").ResetsAt.HasValue, "epoch resets_at parsed");
        Check(feed.Find("weekly") != null && Math.Abs(feed.Find("weekly").Percent - 41.2) < 0.01, "weekly from feed");
        Console.WriteLine();

        // ---------- degenerate input ----------
        Console.WriteLine("-- degenerate --");
        Check(Snapshot.FromUsage(JsonReader.Parse("")).Buckets.Count == 0, "empty string -> no buckets");
        Check(Snapshot.FromUsage(JsonReader.Parse("{}")).Buckets.Count == 0, "empty object -> no buckets");
        Check(Snapshot.FromUsage(JsonReader.Parse("[1,2]")).Buckets.Count == 0, "array root -> no buckets");
        Check(Snapshot.FromUsage(JsonReader.Parse("{\"limits\":\"nope\"}")).Buckets.Count == 0, "limits wrong type");
        Check(Snapshot.FromFeed(JNode.Null).Buckets.Count == 0, "null feed");
        Console.WriteLine();

        Console.WriteLine(_fail == 0
            ? string.Format("ALL {0} CHECKS PASSED", _pass)
            : string.Format("{0} passed, {1} FAILED", _pass, _fail));
        Environment.Exit(_fail == 0 ? 0 : 1);
    }
}
