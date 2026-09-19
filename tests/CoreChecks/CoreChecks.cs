using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using CampusPass.Core;

static class CoreTest
{
    static int failures;
    static int checks;

    static void Check(bool condition, string label)
    {
        checks++;
        if (condition) { Console.WriteLine("  PASS  " + label); return; }
        failures++;
        Console.WriteLine("  FAIL  " + label);
    }

    static void Equal(object actual, object expected, string label)
    {
        bool ok = Equals(actual, expected);
        checks++;
        if (ok) { Console.WriteLine("  PASS  " + label); return; }
        failures++;
        Console.WriteLine("  FAIL  " + label + "\n          expected: " + expected + "\n          actual:   " + actual);
    }

    static int Main()
    {
        try { Console.OutputEncoding = Encoding.UTF8; } catch { }

        // Same first step Program.Main takes, so the checks run against the real,
        // post-rename data location rather than an empty one.
        AppData.MigrateFromLegacy();

        // Byte-exact snapshots: restoring with WriteAllText(Encoding.UTF8) would add
        // a BOM and silently mutate the user's real config file.
        byte[] settingsSnapshot = File.Exists(AppData.SettingsPath) ? File.ReadAllBytes(AppData.SettingsPath) : null;
        byte[] credentialSnapshot = File.Exists(AppData.CredentialPath) ? File.ReadAllBytes(AppData.CredentialPath) : null;
        byte[] logSnapshot = File.Exists(AppData.LogPath) ? File.ReadAllBytes(AppData.LogPath) : null;

        try
        {
            Console.WriteLine("== settings.xml round-trip vs the real on-disk file ==");
            TestSettingsRoundTrip();

            Console.WriteLine("== legacy settings.xml missing new properties ==");
            TestBackCompatProbes();

            Console.WriteLine("== DPAPI credential round-trip ==");
            TestCredentials();

            Console.WriteLine("== LogTail ==");
            TestLogTail();

            Console.WriteLine("== PortalClient over a real socket ==");
            TestPortalClient();
        }
        finally
        {
            Restore(AppData.SettingsPath, settingsSnapshot);
            Restore(AppData.CredentialPath, credentialSnapshot);
            Restore(AppData.LogPath, logSnapshot);
        }

        Console.WriteLine();
        Console.WriteLine(failures == 0
            ? "ALL GREEN  (" + checks + " checks)"
            : failures + " FAILED of " + checks);
        return failures == 0 ? 0 : 1;
    }

    /// <summary>
    /// PortalClient writes to the shared log and LoadSettings reads the real user
    /// profile, so every file the checks can touch is put back exactly as found.
    /// </summary>
    static void Restore(string path, byte[] original)
    {
        try
        {
            if (original == null) { if (File.Exists(path)) File.Delete(path); return; }
            File.WriteAllBytes(path, original);
        }
        catch (Exception ex) { Console.WriteLine("  (could not restore " + Path.GetFileName(path) + ": " + ex.Message + ")"); }
    }

    // ---------------------------------------------------------------- settings

    static void TestSettingsRoundTrip()
    {
        if (!File.Exists(AppData.SettingsPath)) { Console.WriteLine("  SKIP  no settings.xml present"); return; }
        byte[] original = File.ReadAllBytes(AppData.SettingsPath);

        PortalSettings loaded = AppData.LoadSettings();
        Check(loaded != null, "deserialises");
        Console.WriteLine("        (current config: field=" + loaded.UsernameField +
                          ", interval=" + loaded.CheckIntervalSeconds +
                          ", autodetect=" + loaded.AutoDetect + ")");
        // Deliberately no assertion on AutoDetect here: that is the user's own
        // choice and can legitimately be either value. The back-compat behaviour
        // (element absent => true) is covered by TestBackCompatProbes.

        string temp = Path.Combine(Path.GetTempPath(), "cf_roundtrip_" + Guid.NewGuid().ToString("N") + ".xml");
        using (var stream = File.Create(temp))
            new System.Xml.Serialization.XmlSerializer(typeof(PortalSettings)).Serialize(stream, loaded);
        byte[] saved = File.ReadAllBytes(temp);
        File.Delete(temp);

        // Byte identity is not achievable and does not matter: .NET Core's
        // XmlSerializer writes encoding="utf-8" in the declaration and emits the
        // xmlns:xsd/xmlns:xsi pair in the opposite order. Both files are the same
        // document to any XmlSerializer, in either direction.
        Console.WriteLine("        (declaration drift is expected: " + original.Length + " legacy vs " + saved.Length + " new bytes)");
        PortalSettings reloaded;
        using (var reader = new StringReader(Encoding.UTF8.GetString(saved)))
            reloaded = (PortalSettings)new System.Xml.Serialization.XmlSerializer(typeof(PortalSettings)).Deserialize(reader);
        Equal(reloaded.PortalUrl, loaded.PortalUrl, "PortalUrl survives save->load");
        Equal(reloaded.SubmitUrl, loaded.SubmitUrl, "SubmitUrl survives save->load");
        Equal(reloaded.Method, loaded.Method, "Method survives save->load");
        Equal(reloaded.UsernameField, loaded.UsernameField, "UsernameField survives save->load");
        Equal(reloaded.PasswordField, loaded.PasswordField, "PasswordField survives save->load");
        Equal(reloaded.ExtraFields, loaded.ExtraFields, "ExtraFields survives save->load");
        Equal(reloaded.ConnectivityUrl, loaded.ConnectivityUrl, "ConnectivityUrl survives save->load");
        Equal(reloaded.ConnectivityExpected, loaded.ConnectivityExpected, "ConnectivityExpected survives save->load");
        Equal(reloaded.SuccessKeywords, loaded.SuccessKeywords, "SuccessKeywords survives save->load");
        Equal(reloaded.CheckIntervalSeconds, loaded.CheckIntervalSeconds, "CheckIntervalSeconds survives save->load");
        Equal(reloaded.DiscoverRedirect, loaded.DiscoverRedirect, "DiscoverRedirect survives save->load");
        Equal(reloaded.AutoDetect, loaded.AutoDetect, "AutoDetect survives save->load");

        // The legacy build never saw a \r after reading, so the POST body it built
        // came from the \n-normalised value too. Pin that so a future "fix" here is
        // recognised as a behaviour change rather than a bug fix.
        Equal(loaded.ExtraFields, "showVerify=false\nloginType=1",
            "XML newline normalisation collapses CRLF (same as the legacy build did)");
    }

    static void TestBackCompatProbes()
    {
        string legacy = "<?xml version=\"1.0\"?>\n<PortalSettings xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\">\n"
            + "  <PortalUrl>http://example.invalid/login</PortalUrl>\n  <SubmitUrl>http://example.invalid/login.do</SubmitUrl>\n  <Method>POST</Method>\n"
            + "  <UsernameField>u</UsernameField>\n  <PasswordField>p</PasswordField>\n  <ExtraFields></ExtraFields>\n"
            + "  <ConnectivityUrl>http://example.invalid/c.txt</ConnectivityUrl>\n  <ConnectivityExpected>ok</ConnectivityExpected>\n"
            + "  <SuccessKeywords>k</SuccessKeywords>\n  <CheckIntervalSeconds>60</CheckIntervalSeconds>\n  <DiscoverRedirect>true</DiscoverRedirect>\n</PortalSettings>";

        string temp = Path.Combine(Path.GetTempPath(), "cf_legacy_" + Guid.NewGuid().ToString("N") + ".xml");
        File.WriteAllText(temp, legacy, Encoding.UTF8);
        try
        {
            using (var reader = new StringReader(legacy))
            {
                var s = (PortalSettings)new System.Xml.Serialization.XmlSerializer(typeof(PortalSettings)).Deserialize(reader);
                Check(!s.AutoDetect, "absent AutoDetect defaults false from the serializer");
            }
            // The guards live in AppData.LoadSettings, which reads a fixed path, so
            // exercise the same text the way it does.
            // This one has to swap the real settings.xml because LoadSettings reads a
            // fixed path, so take and put back exact bytes.
            string real = AppData.SettingsPath;
            byte[] backup = File.Exists(real) ? File.ReadAllBytes(real) : null;
            PortalSettings viaLoader;
            try
            {
                // LoadSettings reads a fixed path, so this has to write there; the
                // directory may not exist yet on a machine that has never run the app.
                Directory.CreateDirectory(AppData.DirectoryPath);
                File.Copy(temp, real, true);
                viaLoader = AppData.LoadSettings();
            }
            finally
            {
                Restore(real, backup);
            }
            Check(viaLoader.AutoDetect, "AppData.LoadSettings forces AutoDetect=true when element absent");

            // NightMode is gone from the model, but settings.xml files already on disk
            // still carry the element. XmlSerializer must skip it, not throw.
            string withNightMode = legacy.Replace("<DiscoverRedirect>true</DiscoverRedirect>",
                "<DiscoverRedirect>true</DiscoverRedirect><NightMode>true</NightMode>");
            Check(withNightMode.Contains("<NightMode>"), "fixture carries the retired element");
            using (var reader = new StringReader(withNightMode))
            {
                var tolerated = (PortalSettings)new System.Xml.Serialization.XmlSerializer(typeof(PortalSettings)).Deserialize(reader);
                Check(tolerated != null, "a settings.xml still containing <NightMode> loads without error");
            }
        }
        finally { File.Delete(temp); }
    }

    static void TestCredentials()
    {
        AppData.SaveCredential("20191234567", "密码 Pa$$w0rd");
        string[] back = AppData.LoadCredential();
        Check(back != null, "LoadCredential returns a pair");
        if (back != null)
        {
            Equal(back[0], "20191234567", "username survives DPAPI round-trip");
            Equal(back[1], "密码 Pa$$w0rd", "unicode password survives DPAPI round-trip");
        }
        byte[] raw = File.ReadAllBytes(AppData.CredentialPath);
        Check(Encoding.UTF8.GetString(raw).IndexOf("20191234567", StringComparison.Ordinal) < 0, "credential is not stored in plaintext");
        File.Delete(AppData.CredentialPath);
    }

    // ---------------------------------------------------------------- log tail

    static void TestLogTail()
    {
        string path = Path.Combine(Path.GetTempPath(), "cf_log_" + Guid.NewGuid().ToString("N") + ".log");
        try
        {
            var sb = new StringBuilder();
            for (int i = 1; i <= 5000; i++) sb.Append("2026-09-19 10:00:00  行 ").Append(i).Append(" 中文消息").Append("\r\n");
            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);

            string[] all = LogTail.ReadLastLines(path, 5000);
            Equal(all.Length, 5000, "reads every line");
            Equal(all[0], "2026-09-19 10:00:00  行 1 中文消息", "first line intact (no dropped head)");
            Equal(all[4999], "2026-09-19 10:00:00  行 5000 中文消息", "last line intact");

            // AppData.Log creates the file with AppendAllText(Encoding.UTF8), which
            // writes a BOM. File.ReadAllText used to swallow it; the tail reader must
            // too, or the first rendered line starts with .
            string bomPath = path + ".bom";
            File.WriteAllText(bomPath, "2026-09-19 10:00:00  首行\r\n2026-09-19 10:00:01  次行\r\n", new UTF8Encoding(true));
            Check(File.ReadAllBytes(bomPath)[0] == 0xEF, "fixture really does carry a UTF-8 BOM");
            string[] bomLines = LogTail.ReadLastLines(bomPath, 10);
            Equal(bomLines[0], "2026-09-19 10:00:00  首行", "leading BOM stripped from the first line");
            File.Delete(bomPath);

            // A tail read that starts mid-file must not mistake interior bytes for a BOM.
            string midBom = path + ".mid";
            File.WriteAllText(midBom, new string('x', 70000) + "\r\nreal-last-line\r\n", Encoding.UTF8);
            string[] mid = LogTail.ReadLastLines(midBom, 1);
            Equal(mid[0], "real-last-line", "interior bytes are not mistaken for a BOM");
            File.Delete(midBom);

            string[] tail = LogTail.ReadLastLines(path, 3);
            Equal(tail.Length, 3, "tail count");
            Equal(tail[0], "2026-09-19 10:00:00  行 4998 中文消息", "tail starts in the right place");
            Equal(tail[2], "2026-09-19 10:00:00  行 5000 中文消息", "tail ends at the newest line");

            string[] none = LogTail.ReadLastLines(path + ".missing", 10);
            Equal(none.Length, 1, "missing file yields one placeholder line");
            Equal(none[0], "暂无日志", "missing file placeholder text");

            string noNewline = path + ".nonl";
            File.WriteAllText(noNewline, "a\r\nb\r\nlast-without-eol", Encoding.UTF8);
            string[] r = LogTail.ReadLastLines(noNewline, 10);
            Equal(r.Length, 3, "file without trailing newline keeps its last line");
            Equal(r[2], "last-without-eol", "final unterminated line correct");
            File.Delete(noNewline);

            // 6 MB, to prove the backward read is not proportional to file size.
            string big = path + ".big";
            using (var fs = new FileStream(big, FileMode.Create, FileAccess.Write))
            using (var w = new StreamWriter(fs, Encoding.UTF8))
                for (int i = 0; i < 100000; i++) w.WriteLine("2026-09-19 10:00:00  padding padding padding line " + i);
            long bytes = new FileInfo(big).Length;
            var sw = Stopwatch.StartNew();
            string[] bigTail = LogTail.ReadLastLines(big, 800);
            sw.Stop();
            Check(bytes > 4_000_000, "synthetic log is large (" + (bytes / 1024 / 1024) + " MB)");
            Equal(bigTail.Length, 800, "tail of a big log returns the requested count");
            Check(bigTail[799].EndsWith("99999"), "big-log tail ends on the newest line");
            Check(sw.ElapsedMilliseconds < 300, "big-log tail under 300ms (took " + sw.ElapsedMilliseconds + "ms)");
            File.Delete(big);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    // ------------------------------------------------------------ portal http

    class FakePortal : IDisposable
    {
        readonly TcpListener listener;
        readonly Thread thread;
        readonly Func<string, string> handler;
        volatile bool running = true;

        public List<string> Requests = new List<string>();
        public string LastRequest { get { lock (Requests) return Requests[Requests.Count - 1]; } }
        public int Port { get { return ((IPEndPoint)listener.LocalEndpoint).Port; } }
        public string Url(string path) { return "http://127.0.0.1:" + Port + path; }

        public FakePortal(Func<string, string> handler)
        {
            this.handler = handler;
            listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            thread = new Thread(Loop) { IsBackground = true };
            thread.Start();
        }

        void Loop()
        {
            while (running)
            {
                try
                {
                    TcpClient client = listener.AcceptTcpClient();
                    using (client)
                    using (Stream s = client.GetStream())
                    {
                        string request = ReadRequest(s);
                        lock (Requests) Requests.Add(request);
                        // Must be UTF-8: Content-Length is computed from UTF-8 bytes, and
                        // ASCII would shrink Chinese bodies to '?' and truncate the response.
                        byte[] response = Encoding.UTF8.GetBytes(handler(request));
                        s.Write(response, 0, response.Length);
                        s.Flush();
                        client.NoDelay = true;
                    }
                }
                catch { if (running) continue; break; }
            }
        }

        static string ReadRequest(Stream s)
        {
            var buffer = new byte[8192];
            var sb = new StringBuilder();
            int contentLength = -1, headEnd = -1;
            int total = 0;
            while (true)
            {
                int read = s.Read(buffer, 0, buffer.Length);
                if (read <= 0) break;
                sb.Append(Encoding.UTF8.GetString(buffer, 0, read));
                total += read;
                string soFar = sb.ToString();
                if (headEnd < 0)
                {
                    int idx = soFar.IndexOf("\r\n\r\n", StringComparison.Ordinal);
                    if (idx >= 0)
                    {
                        headEnd = idx + 4;
                        foreach (string line in soFar.Substring(0, headEnd).Split('\n'))
                        {
                            string t = line.Trim();
                            if (t.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                                contentLength = int.Parse(t.Substring(15).Trim());
                        }
                    }
                }
                if (headEnd >= 0 && (contentLength <= 0 || total >= headEnd + contentLength)) break;
                if (headEnd >= 0 && contentLength <= 0 && read < buffer.Length) break;
            }
            return sb.ToString();
        }

        public void Dispose()
        {
            running = false;
            try { listener.Stop(); } catch { }
            try { thread.Join(500); } catch { }
        }

        public static string Respond(int status, string reason, string body, params string[] extraHeaders)
        {
            byte[] b = Encoding.UTF8.GetBytes(body ?? "");
            var sb = new StringBuilder();
            sb.Append("HTTP/1.1 ").Append(status).Append(' ').Append(reason).Append("\r\n");
            sb.Append("Content-Type: text/html; charset=utf-8\r\n");
            sb.Append("Content-Length: ").Append(b.Length).Append("\r\n");
            foreach (string h in extraHeaders) sb.Append(h).Append("\r\n");
            sb.Append("Connection: close\r\n\r\n").Append(body ?? "");
            return sb.ToString();
        }
    }

    static void TestPortalClient()
    {
        const string loginPage = "<html><body><form action=\"/login.do?redirect=1\" method=\"POST\">"
            + "<input type=\"hidden\" name=\"ac_id\" value=\"42\">"
            + "<input type=\"text\" name=\"userName\" id=\"n\">"
            + "<input type=\"password\" name=\"userPwd\" id=\"p\">"
            + "<input type=\"submit\" name=\"btnSubmit\" value=\"go\">"
            + "</form></body></html>";

        // /connecttest.txt reports "online"; /offline.txt reports "captive portal",
        // so a TryLogin test can pick which pre-flight branch it wants.
        using (var portal = new FakePortal(req =>
        {
            string line = req.Split('\n')[0];
            if (line.Contains("connecttest.txt")) return FakePortal.Respond(200, "OK", "Microsoft Connect Test");
            if (line.Contains("offline.txt")) return FakePortal.Respond(200, "OK", "captive-portal-redirect");
            if (line.Contains("showLogin.do")) return FakePortal.Respond(200, "OK", loginPage);
            if (line.Contains("login.do")) return FakePortal.Respond(200, "OK", "<html>登录成功</html>");
            return FakePortal.Respond(404, "Not Found", "nope");
        }))
        {
            Console.WriteLine("-- DetectForm --");
            PortalClient.DetectedForm form = PortalClient.DetectForm(portal.Url("/showLogin.do?wlanuserip={local_ip}"));
            Check(form != null, "DetectForm found a form");
            if (form != null)
            {
                Equal(form.Method, "POST", "method");
                Check(form.SubmitUrl.EndsWith("/login.do?redirect=1"), "action resolved against the base url (" + form.SubmitUrl + ")");
                Equal(form.UsernameField, "userName", "username field chosen over submit");
                Equal(form.PasswordField, "userPwd", "password field");
                Equal(form.ExtraFields, "ac_id=42", "hidden field captured, submit excluded");
            }

            Console.WriteLine("-- HasInternet --");
            var s = PortalSettings.ChinaMobileTemplate();
            s.ConnectivityUrl = portal.Url("/connecttest.txt");
            Check(PortalClient.HasInternet(s), "HasInternet true on the expected body");
            s.ConnectivityExpected = "something else";
            Check(!PortalClient.HasInternet(s), "HasInternet false when body mismatches");
            s.ConnectivityUrl = "http://127.0.0.1:1/nope";
            Check(!PortalClient.HasInternet(s), "HasInternet false (not throwing) on connection refused");

            Console.WriteLine("-- TryLogin end to end --");
            s = PortalSettings.ChinaMobileTemplate();
            // Offline first: TryLogin short-circuits to "网络已经可以正常访问" when the
            // connectivity probe already succeeds, which would skip the POST entirely.
            s.ConnectivityUrl = portal.Url("/offline.txt");
            s.ConnectivityExpected = "Microsoft Connect Test";
            s.PortalUrl = portal.Url("/showLogin.do");
            s.SubmitUrl = portal.Url("/login.do");
            s.UsernameField = "userName";
            s.PasswordField = "userPwd";
            s.ExtraFields = "ac_id=42";
            s.SuccessKeywords = "登录成功|您已成功登录";
            s.AutoDetect = false;
            AppData.SaveCredential("student001", "s3cr3t 密码");

            string result = PortalClient.TryLogin(s);
            Equal(result, "认证成功", "TryLogin reports success on the keyword match");
            // TryLogin re-probes connectivity after the POST, so the login request is
            // not the last one the fake saw.
            string post = null;
            lock (portal.Requests)
                foreach (string rq in portal.Requests) if (rq.StartsWith("POST ")) { post = rq; break; }
            Check(post != null, "a POST reached the portal");
            if (post != null)
            {
                // AutoDetect is off here, so the configured SubmitUrl is used verbatim;
                // the ?redirect=1 resolution is covered by the DetectForm checks above.
                Check(post.Contains("POST /login.do HTTP/1.1"), "login POSTed to the configured submit url over HTTP/1.1");
                Check(post.Contains("Content-Type: application/x-www-form-urlencoded; charset=UTF-8"),
                    "Content-Type header byte-identical to the legacy build");
                Check(post.Contains("userName=student001"), "username posted urlencoded");
                Check(post.Contains("userPwd=s3cr3t%20%E5%AF%86%E7%A0%81"), "unicode password posted percent-encoded");
                Check(post.Contains("ac_id=42"), "hidden field forwarded");
                Check(post.Contains("User-Agent: Mozilla/5.0 (Windows NT 10.0; Win64; x64) CampusPass/1.0"), "legacy User-Agent sent");
            }

            Console.WriteLine("-- TryLogin short-circuits and guards --");
            s.ConnectivityUrl = portal.Url("/connecttest.txt");
            Equal(PortalClient.TryLogin(s), "网络已经可以正常访问", "already-online short-circuits before touching the portal");

            s.ConnectivityUrl = portal.Url("/offline.txt");
            File.Delete(AppData.CredentialPath);
            Equal(PortalClient.TryLogin(s), "尚未保存账号密码", "no credentials short-circuits with the legacy message");

            Console.WriteLine("-- TryLogin failure paths --");
            s = PortalSettings.ChinaMobileTemplate();
            s.ConnectivityUrl = "http://127.0.0.1:1/nope";
            s.PortalUrl = "http://127.0.0.1:1/nope";
            s.SubmitUrl = "http://127.0.0.1:1/nope";
            s.AutoDetect = false;
            AppData.SaveCredential("u", "p");
            string refused = PortalClient.TryLogin(s);
            Check(refused.StartsWith("认证失败："), "connection refused surfaces as 认证失败 (got: " + refused + ")");
            File.Delete(AppData.CredentialPath);
        }

        using (var err = new FakePortal(req => FakePortal.Respond(500, "Server Error", "boom")))
        {
            var st = PortalSettings.ChinaMobileTemplate();
            st.ConnectivityUrl = err.Url("/c.txt");
            st.PortalUrl = err.Url("/p");
            st.SubmitUrl = err.Url("/s");
            st.AutoDetect = false;
            AppData.SaveCredential("u", "p");
            string res = PortalClient.TryLogin(st);
            Check(res.Contains("HTTP 500"), "non-2xx raises HTTP <code> rather than being read as a page (got: " + res + ")");
            File.Delete(AppData.CredentialPath);
        }

        FakePortal redirector = null;
        redirector = new FakePortal(req =>
            req.Contains("connecttest")
                ? FakePortal.Respond(302, "Found", "", "Location: " + redirector.Url("/portal/showLogin.do"))
                : FakePortal.Respond(200, "OK", loginPage));
        using (redirector)
        {
            var st = PortalSettings.ChinaMobileTemplate();
            st.DiscoverRedirect = true;
            st.ConnectivityUrl = redirector.Url("/connecttest.txt");
            st.ConnectivityExpected = "Microsoft Connect Test";
            st.PortalUrl = "http://should.not/be/used";
            st.SubmitUrl = redirector.Url("/login.do");
            st.UsernameField = "userName";
            st.PasswordField = "userPwd";
            st.ExtraFields = "ac_id=42";
            st.SuccessKeywords = "登录成功";
            st.AutoDetect = false;
            AppData.SaveCredential("u1", "p1");
            string outcome = PortalClient.TryLogin(st);
            Check(outcome == "认证成功" || outcome == "已提交，但未确认联网", "redirect discovery path completes (got: " + outcome + ")");
            bool sawPortal = false, usedConfigured = false;
            lock (redirector.Requests)
                foreach (string rq in redirector.Requests)
                {
                    if (rq.Contains("/portal/showLogin.do")) sawPortal = true;
                    if (rq.Contains("should.not")) usedConfigured = true;
                }
            Check(sawPortal, "ResolvePortalUrl followed the Location header");
            Check(!usedConfigured, "the configured PortalUrl was bypassed as intended");
            File.Delete(AppData.CredentialPath);
        }
    }
}
