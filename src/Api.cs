// Claude usage client.
//
// Ported from the predecessor, keeping the parts that were learned the hard way (IPv4
// pinning, credential write-back, two token hosts x two body encodings) and fixing two
// things it got wrong:
//
//   * User-Agent. It sent "ClaudeWidget". The endpoint buckets unknown agents into a much
//     harsher rate limit; it wants "claude-code/<version>". The real version is read from
//     ~/.claude/.last-update-result.json.
//   * Backoff. On failure it *tightened* polling from 5 minutes to 1, which digs the 429
//     hole deeper on an endpoint that returns no Retry-After. Now it backs off.
//
// The endpoint is undocumented and beta-gated. It can change or vanish without notice.
using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Vibespan
{
    public class UsageResult
    {
        public Snapshot Snapshot;
        public string Error;
        public bool RateLimited;
        public bool Ok { get { return Snapshot != null && Error == null; } }
    }

    public static class Api
    {
        // Public OAuth client id of Claude Code - an identifier, not a secret.
        const string ClientId = "9d1c250a-e61b-44d9-88ed-5944d1962f5e";
        const string UsageUrl = "https://api.anthropic.com/api/oauth/usage";
        const string Beta = "oauth-2025-04-20";
        static readonly string[] TokenUrls =
        {
            "https://platform.claude.com/v1/oauth/token",
            "https://console.anthropic.com/v1/oauth/token"
        };

        public static string CredPath
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                                    ".claude\\.credentials.json");
            }
        }
        static string VersionPath
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                                    ".claude\\.last-update-result.json");
            }
        }

        static long NowMs() { return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(); }

        // ---------- user agent ----------
        static string _ua;
        public static string UserAgent
        {
            get
            {
                if (_ua != null) return _ua;
                string ver = null;
                try
                {
                    if (File.Exists(VersionPath))
                        ver = JsonReader.Parse(File.ReadAllText(VersionPath))["version_to"].AsString(null);
                }
                catch { }
                _ua = "claude-code/" + (string.IsNullOrEmpty(ver) ? "2.1.0" : ver);
                return _ua;
            }
        }

        // ---------- address family ----------
        // A router that advertises an IPv6 prefix it does not route leaves HttpWebRequest
        // sitting in SYN_SENT on the AAAA record until the timeout, so no refresh ever lands.
        // Ask for IPv4, and switch back automatically if IPv6 turns out to be the only path.
        static bool _ipv4 = true;

        static IPEndPoint BindIpv4(ServicePoint sp, IPEndPoint remote, int retry)
        {
            if (remote.AddressFamily == AddressFamily.InterNetwork) return new IPEndPoint(IPAddress.Any, 0);
            throw new InvalidOperationException("IPv6 address skipped");
        }

        static HttpWebRequest NewRequest(string url)
        {
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Timeout = 20000;
            req.ReadWriteTimeout = 20000;    // otherwise a stalled stream can hang for 5 minutes
            req.UserAgent = UserAgent;
            try { req.ServicePoint.BindIPEndPointDelegate = _ipv4 ? new BindIPEndPoint(BindIpv4) : null; }
            catch { }
            return req;
        }

        static bool IsNetworkFailure(WebException we)
        {
            return we.Status == WebExceptionStatus.Timeout
                || we.Status == WebExceptionStatus.ConnectFailure
                || we.Status == WebExceptionStatus.NameResolutionFailure;
        }

        // ---------- credentials ----------
        class Oauth
        {
            public string AccessToken, RefreshToken;
            public long ExpiresAt;
        }

        static Oauth ReadCred()
        {
            try
            {
                if (!File.Exists(CredPath)) return null;
                JNode j = JsonReader.Parse(File.ReadAllText(CredPath))["claudeAiOauth"];
                if (!j.Exists) return null;
                return new Oauth
                {
                    AccessToken = j["accessToken"].AsString(null),
                    RefreshToken = j["refreshToken"].AsString(null),
                    ExpiresAt = j["expiresAt"].AsLong(0)
                };
            }
            catch { return null; }
        }

        static Oauth ReadCache()
        {
            try
            {
                if (!File.Exists(Cfg.TokenCachePath)) return null;
                JNode j = JsonReader.Parse(File.ReadAllText(Cfg.TokenCachePath));
                if (!j.Exists) return null;
                return new Oauth
                {
                    AccessToken = j["accessToken"].AsString(null),
                    RefreshToken = j["refreshToken"].AsString(null),
                    ExpiresAt = j["expiresAt"].AsLong(0)
                };
            }
            catch { return null; }
        }

        static void WriteCache(Oauth o)
        {
            try
            {
                Directory.CreateDirectory(Cfg.Dir);
                JNode j = JNode.NewObject();
                j.Set("accessToken", o.AccessToken);
                j.Set("refreshToken", o.RefreshToken);
                j.Set("expiresAt", o.ExpiresAt);
                File.WriteAllText(Cfg.TokenCachePath, j.ToString());
            }
            catch { }
        }

        static Oauth LoadBest()
        {
            Oauth cc = ReadCred(), cache = ReadCache();
            if (cc == null) return cache;
            if (cache == null) return cc;
            return cache.ExpiresAt > cc.ExpiresAt ? cache : cc;
        }

        // ----- writing the rotated token back to Claude Code -----
        // The OAuth server rotates refresh tokens: using one invalidates the previous. If we
        // keep the new token to ourselves, Claude Code is left holding a dead token and its
        // session expires within hours.
        //
        // The file carries fields this program does not model (scopes, subscriptionType,
        // rateLimitTier...) which a re-serialization would wipe, so the values are patched in
        // place instead.
        static int ValueStart(string json, string key)
        {
            string needle = "\"" + key + "\":";
            int i = json.IndexOf(needle, StringComparison.Ordinal);
            if (i < 0) return -1;
            int p = i + needle.Length;
            while (p < json.Length && char.IsWhiteSpace(json[p])) p++;
            return p;
        }

        static string SetJsonString(string json, string key, string value)
        {
            if (json == null || string.IsNullOrEmpty(value)) return null;
            int p = ValueStart(json, key);
            if (p < 0 || p >= json.Length || json[p] != '"') return null;
            int end = json.IndexOf('"', p + 1);
            if (end < 0) return null;
            return json.Substring(0, p + 1) + value + json.Substring(end);
        }

        static string SetJsonNumber(string json, string key, long value)
        {
            if (json == null) return null;
            int p = ValueStart(json, key);
            if (p < 0) return null;
            int end = p;
            while (end < json.Length && (char.IsDigit(json[end]) || json[end] == '-')) end++;
            if (end == p) return null;
            return json.Substring(0, p) + value.ToString(CultureInfo.InvariantCulture) + json.Substring(end);
        }

        static void WriteBackCredentials(Oauth no)
        {
            try
            {
                if (!File.Exists(CredPath)) return;
                string json = File.ReadAllText(CredPath);
                string upd = SetJsonString(json, "accessToken", no.AccessToken);
                upd = SetJsonString(upd, "refreshToken", no.RefreshToken);
                upd = SetJsonNumber(upd, "expiresAt", no.ExpiresAt);
                if (upd == null) { Log.Write("credentials write-back skipped: unexpected format"); return; }

                // Atomic: a truncated write here would sign Claude Code out. No BOM and no
                // trailing newline, matching the original file.
                string tmp = CredPath + ".vibespan.tmp";
                string bak = CredPath + ".vibespan.bak";
                File.WriteAllText(tmp, upd, new UTF8Encoding(false));
                try
                {
                    File.Replace(tmp, CredPath, bak);
                    try { File.Delete(bak); } catch { }
                }
                catch
                {
                    File.Copy(tmp, CredPath, true);
                    try { File.Delete(tmp); } catch { }
                }
                Log.Write("Claude Code credentials updated (refresh token rotation)");
            }
            catch (Exception e) { Log.Write("credentials write-back failed: " + e.Message); }
        }

        // ---------- http ----------
        static string HttpPost(string url, string body, string contentType)
        {
            var req = NewRequest(url);
            req.Method = "POST";
            req.ContentType = contentType;
            byte[] data = Encoding.UTF8.GetBytes(body);
            using (Stream s = req.GetRequestStream()) s.Write(data, 0, data.Length);
            using (var resp = (HttpWebResponse)req.GetResponse())
            using (var r = new StreamReader(resp.GetResponseStream()))
                return r.ReadToEnd();
        }

        static string GetToken()
        {
            Oauth o = LoadBest();
            if (o == null || string.IsNullOrEmpty(o.AccessToken))
                throw new NotSignedInException();
            if (o.ExpiresAt > 0 && NowMs() < o.ExpiresAt - 120000) return o.AccessToken;

            string jsonBody = "{\"grant_type\":\"refresh_token\",\"refresh_token\":\"" + o.RefreshToken +
                              "\",\"client_id\":\"" + ClientId + "\"}";
            string formBody = "grant_type=refresh_token&refresh_token=" + Uri.EscapeDataString(o.RefreshToken ?? "") +
                              "&client_id=" + ClientId;

            foreach (string url in TokenUrls)
            {
                for (int i = 0; i < 2; i++)
                {
                    try
                    {
                        string resp = (i == 0)
                            ? HttpPost(url, formBody, "application/x-www-form-urlencoded")
                            : HttpPost(url, jsonBody, "application/json");
                        JNode j = JsonReader.Parse(resp);
                        string at = j["access_token"].AsString(null);
                        if (!string.IsNullOrEmpty(at))
                        {
                            var no = new Oauth
                            {
                                AccessToken = at,
                                RefreshToken = j["refresh_token"].AsString(o.RefreshToken),
                                ExpiresAt = NowMs() + j["expires_in"].AsLong(3600) * 1000
                            };
                            WriteCache(no);
                            WriteBackCredentials(no);
                            Log.Write("token refresh OK via " + url);
                            return no.AccessToken;
                        }
                    }
                    catch (WebException we)
                    {
                        if (we.Response != null) we.Response.Close();   // else the ServicePoint holds it
                        Log.Write("token refresh failed (" + url + "): " + we.Status);
                    }
                    catch (Exception e) { Log.Write("token refresh failed (" + url + "): " + e.Message); }
                }
            }
            return o.AccessToken;    // last resort - it may still be valid
        }

        static Snapshot FetchOnce()
        {
            string tok = GetToken();
            var req = NewRequest(UsageUrl);
            req.Method = "GET";
            req.Headers["Authorization"] = "Bearer " + tok;
            req.Headers["anthropic-beta"] = Beta;
            using (var resp = (HttpWebResponse)req.GetResponse())
            using (var r = new StreamReader(resp.GetResponseStream()))
            {
                string body = r.ReadToEnd();
                JNode j = JsonReader.Parse(body);
                if (!j.Exists) throw new BadResponseException();
                Snapshot s = Snapshot.FromUsage(j);
                if (s.Buckets.Count == 0) throw new BadResponseException();
                return s;
            }
        }

        public class NotSignedInException : Exception { }
        public class BadResponseException : Exception { }

        /// <summary>
        /// When set, responses are read from this file instead of the network. Used by --demo
        /// for screenshots and manual testing: the real endpoint 429s without a Retry-After, so
        /// repeatedly restarting the widget against it is a genuinely bad idea.
        /// </summary>
        public static string DemoFile;

        /// <summary>Fetch usage. Never throws; failures come back on the result.</summary>
        public static UsageResult Fetch()
        {
            var result = new UsageResult();

            if (!string.IsNullOrEmpty(DemoFile))
            {
                try
                {
                    JNode j = JsonReader.Parse(File.ReadAllText(DemoFile));
                    result.Snapshot = Snapshot.FromUsage(j);
                    if (result.Snapshot.Buckets.Count == 0) result.Error = "badResponse";
                }
                catch (Exception e) { result.Error = e.Message; }
                return result;
            }

            try
            {
                result.Snapshot = FetchOnce();
                return result;
            }
            catch (NotSignedInException) { result.Error = "notSignedIn"; return result; }
            catch (BadResponseException) { result.Error = "badResponse"; return result; }
            catch (WebException we)
            {
                var hr = we.Response as HttpWebResponse;
                int code = hr == null ? 0 : (int)hr.StatusCode;
                if (hr != null) hr.Close();

                if (code == 429)
                {
                    // No Retry-After is sent. The caller backs off; hammering makes it worse.
                    Log.Write("usage rate limited (HTTP 429) - backing off");
                    result.Error = "rateLimited";
                    result.RateLimited = true;
                    return result;
                }

                if (code == 401 || code == 403)
                {
                    // Token rejected: drop our cache, which may be stale, and retry once with
                    // Claude Code's own credentials.
                    Log.Write("usage rejected (HTTP " + code + "), clearing cache and retrying");
                    try { File.Delete(Cfg.TokenCachePath); } catch { }
                    try { result.Snapshot = FetchOnce(); return result; }
                    catch (Exception e2) { result.Error = e2.Message; return result; }
                }

                if (IsNetworkFailure(we))
                {
                    bool previous = _ipv4;
                    _ipv4 = !previous;
                    try
                    {
                        result.Snapshot = FetchOnce();
                        Log.Write("network failure (" + we.Status + ") -> switched to " + (_ipv4 ? "IPv4" : "auto"));
                        return result;
                    }
                    catch { _ipv4 = previous; }
                }

                result.Error = we.Status.ToString();
                return result;
            }
            catch (Exception e) { result.Error = e.Message; return result; }
        }

        public static bool IsSignedIn { get { return File.Exists(CredPath); } }
    }
}
